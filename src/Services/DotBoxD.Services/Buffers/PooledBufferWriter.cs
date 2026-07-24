using System.Buffers;
using System.Runtime.CompilerServices;

namespace DotBoxD.Services.Buffers;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> backed by an array rented from <see cref="ArrayPool{T}"/>.
/// Either hand the written bytes off via <see cref="DetachPayload"/> or release them via
/// <see cref="Dispose"/> — never both.
/// </summary>
public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    // Largest single-dimension array the runtime allows (== Array.MaxLength, which netstandard2.1 lacks).
    private const int MaxArrayLength = 0x7FFFFFC7;
    private byte[]? _buffer;
    private int _written;
    private int _maxWritten = int.MaxValue;
    private bool _returnToCache;
    private PooledBufferWriterLease _lease = PooledBufferWriterLease.Create();
    // A retained byte[] in a hot slot, or the next writer while this entry is in locked overflow.
    internal object? CachedResource { get; set; }
    internal int CacheThreadId { get; set; }

    public PooledBufferWriter(int initialCapacity = 256)
    {
        if (initialCapacity <= 0)
        {
            initialCapacity = 256;
        }

        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    /// <summary>
    /// Rents a writer object for internal ownership-transfer framing paths. The public constructor
    /// keeps disposed writers permanently unusable; callers of this pool must not retain a raw writer
    /// reference after transferring its ownership.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static PooledBufferWriter Rent(int initialCapacity = 256, int maxWritten = int.MaxValue)
    {
        if (maxWritten <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWritten));
        }

        return PooledBufferWriterPool.Rent(initialCapacity, maxWritten);
    }

    internal int RetainedBufferLength => (CachedResource as byte[])?.Length ?? 0;
    internal long LeaseToken => _lease.CaptureActiveToken();

    /// <summary>
    /// The bytes written so far.
    /// </summary>
    public ReadOnlyMemory<byte> WrittenMemory =>
        (_buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter))).AsMemory(0, _written);

    /// <summary>
    /// The number of bytes written so far.
    /// </summary>
    public int WrittenCount
    {
        get
        {
            _ = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
            return _written;
        }
    }

    internal Span<byte> WrittenSpan =>
        (_buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter))).AsSpan(0, _written);

    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
        // Widen to 64-bit: _written + count in 32-bit signed arithmetic can overflow to a negative value,
        // making this guard pass and silently corrupting _written (symmetric with the EnsureCapacity fix).
        var requested = (long)_written + count;
        if (requested > buffer.Length)
        {
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");
        }

        ThrowIfBudgetExceeded(requested);
        _written += count;
    }

    internal void Rewind(int writtenCount)
    {
        if (writtenCount < 0 || writtenCount > _written)
        {
            throw new ArgumentOutOfRangeException(nameof(writtenCount));
        }

        _ = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
        _written = writtenCount;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer!.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer!.AsSpan(_written);
    }

    /// <summary>
    /// Hands the rented array and written length to a new <see cref="Payload"/> and relinquishes
    /// ownership. The writer must not be used afterward.
    /// </summary>
    public Payload DetachPayload()
    {
        var token = _lease.BeginPublicDetach();
        var buffer = Interlocked.Exchange(ref _buffer, null)
            ?? throw new InvalidOperationException("Buffer has already been detached or disposed.");
        _lease.CompletePublicDetach(token);
        return new Payload(buffer, _written);
    }

    /// <summary>
    /// Returns the rented array to the pool. A no-op after <see cref="DetachPayload"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_lease.TryReturnCurrent())
        {
            ReleaseReturnedLease();
        }
    }

    internal ReadOnlyMemory<byte> GetWrittenMemory(long leaseToken)
    {
        if (!_lease.IsActive(leaseToken))
        {
            throw new ObjectDisposedException(nameof(PooledBufferWriter));
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
        var written = _written;
        if (!_lease.IsActive(leaseToken))
        {
            throw new ObjectDisposedException(nameof(PooledBufferWriter));
        }

        return buffer.AsMemory(0, written);
    }

    internal Payload DetachLeasePayload(long leaseToken)
    {
        if (!_lease.TryBeginFrameDetach(leaseToken))
        {
            throw new ObjectDisposedException(nameof(PooledBufferWriter));
        }

        var buffer = Interlocked.Exchange(ref _buffer, null)
            ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
        var payload = new Payload(buffer, _written);
        _lease.CompleteFrameDetach(leaseToken);
        ReleaseReturnedLease();
        return payload;
    }

    internal void DisposeLease(long leaseToken)
    {
        if (_lease.TryReturn(leaseToken))
        {
            ReleaseReturnedLease();
        }
    }

    internal static PooledBufferWriter CreatePooled(int initialCapacity, int maxWritten) =>
        new(NormalizeInitialCapacity(initialCapacity, maxWritten))
        {
            _maxWritten = maxWritten,
            _returnToCache = true,
        };

    internal void ResetForRent(byte[]? retainedBuffer, int initialCapacity, int maxWritten)
    {
        _maxWritten = maxWritten;
        var requiredCapacity = NormalizeInitialCapacity(initialCapacity, maxWritten);
        if (retainedBuffer is null || retainedBuffer.Length < requiredCapacity)
        {
            ReturnBuffer(retainedBuffer);
            retainedBuffer = ArrayPool<byte>.Shared.Rent(requiredCapacity);
        }

        _buffer = retainedBuffer;
        _written = 0;
        _lease.Reset();
    }

    internal void PrepareForCache() => _written = 0;

    internal static void ReturnBuffer(byte[]? buffer)
    {
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReleaseReturnedLease()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (_returnToCache)
        {
            PooledBufferWriterPool.Return(this, buffer);
        }
        else
        {
            ReturnBuffer(buffer);
        }
    }

    private void EnsureCapacity(int sizeHint)
    {
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));

        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        // Widen to 64-bit: _written + sizeHint in 32-bit signed arithmetic can overflow to a negative
        // value, making the guard below pass and handing back a span SMALLER than sizeHint (an
        // IBufferWriter<T> contract break the caller cannot detect).
        var required = (long)_written + Math.Max(sizeHint, 1);
        ThrowIfBudgetExceeded(required);
        if (required <= buffer.Length)
        {
            return;
        }

        if (required > MaxArrayLength)
        {
            // The request cannot be satisfied by a single array; refuse rather than silently truncate.
            // OutOfMemoryException mirrors System.Buffers.ArrayBufferWriter<T> for this exact case
            // (the IBufferWriter<T> contract); covered by Round5_PooledBufferWriterOverflowTests.
            throw new OutOfMemoryException(
                $"Requested buffer capacity ({required}) exceeds the maximum array length ({MaxArrayLength}).");
        }

        var newSize = (int)Math.Min(Math.Max(required, (long)buffer.Length * 2), MaxArrayLength);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        Array.Copy(buffer, 0, newBuffer, 0, _written);
        _buffer = newBuffer;
        ArrayPool<byte>.Shared.Return(buffer);
    }

    private static int NormalizeInitialCapacity(int initialCapacity, int maxWritten)
    {
        if (initialCapacity <= 0)
        {
            initialCapacity = 256;
        }

        return Math.Min(initialCapacity, maxWritten);
    }

    private void ThrowIfBudgetExceeded(long requested)
    {
        if (_maxWritten != int.MaxValue && requested > _maxWritten)
        {
            throw new InvalidDataException(
                $"Requested buffer capacity ({requested}) exceeds the configured limit ({_maxWritten}).");
        }
    }
}
