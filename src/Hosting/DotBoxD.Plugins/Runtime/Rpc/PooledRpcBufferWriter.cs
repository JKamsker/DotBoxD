namespace DotBoxD.Plugins.Runtime.Rpc;

using System.Buffers;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> backed by an array rented from <see cref="ArrayPool{T}"/>, used to encode
/// a single <c>RunLocal</c> / server-extension payload without the per-event <c>MemoryStream</c> + <c>ToArray</c>
/// the codec's <c>byte[]</c> overloads incur. The writer object itself is pooled (rent-on-<see cref="Rent"/>,
/// return-on-<see cref="Dispose"/>) so the encode half approaches zero steady-state allocations.
/// </summary>
/// <remarks>
/// <para>
/// The lifetime contract is: <see cref="Rent"/> a writer, encode into it, hand <see cref="WrittenMemory"/> to a
/// transport, and only <see cref="Dispose"/> it <i>after</i> that transport has finished copying the bytes —
/// the rented array aliases <see cref="WrittenMemory"/>, so returning it to the pool while a send still reads
/// from it would be a use-after-free. The remote push path satisfies this by disposing in a <c>using</c> whose
/// scope ends after <c>await push(...)</c> completes.
/// </para>
/// <para>
/// Cannot reuse <c>DotBoxD.Services.Buffers.PooledBufferWriter</c>: <c>DotBoxD.Plugins</c> does not reference
/// <c>DotBoxD.Services</c> (layering). This is the minimal equivalent for the codec's hot path.
/// </para>
/// </remarks>
internal sealed class PooledRpcBufferWriter : IBufferWriter<byte>, IDisposable
{
    // Largest single-dimension array the runtime allows (== Array.MaxLength).
    private const int MaxArrayLength = 0x7FFFFFC7;
    private const int DefaultCapacity = 256;

    [ThreadStatic]
    private static PooledRpcBufferWriter? s_threadCached;
    private static readonly object s_globalGate = new();
    private static PooledRpcBufferWriter? s_globalCached;

    private byte[]? _buffer;
    private int _written;
    private PooledRpcBufferWriter? _nextCached;

    private PooledRpcBufferWriter(int initialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 1));
        _written = 0;
    }

    /// <summary>The bytes written so far. Valid only until <see cref="Dispose"/> returns the array to the pool.</summary>
    public ReadOnlyMemory<byte> WrittenMemory =>
        (_buffer ?? throw new ObjectDisposedException(nameof(PooledRpcBufferWriter))).AsMemory(0, _written);

    /// <summary>
    /// Rents a pooled writer. The caller owns it until <see cref="Dispose"/>; concurrent <see cref="Rent"/> calls
    /// (e.g. across an <c>await</c>) never vend the same instance because the cache slot is claimed atomically.
    /// </summary>
    public static PooledRpcBufferWriter Rent(int initialCapacity = DefaultCapacity)
    {
        var writer = Interlocked.Exchange(ref s_threadCached, null);
        if (writer is null)
        {
            lock (s_globalGate)
            {
                writer = s_globalCached;
                if (writer is not null)
                {
                    s_globalCached = writer._nextCached;
                    writer._nextCached = null;
                }
            }
        }

        if (writer is null)
        {
            return new PooledRpcBufferWriter(initialCapacity);
        }

        writer.Reset(initialCapacity);
        return writer;
    }

    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledRpcBufferWriter));
        if ((long)_written + count > buffer.Length)
        {
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");
        }

        _written += count;
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

    /// <summary>Returns the rented array to the pool and the writer to the cache. Idempotent.</summary>
    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is null)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(buffer);
        _written = 0;

        if (Interlocked.CompareExchange(ref s_threadCached, this, null) is null)
        {
            return;
        }

        lock (s_globalGate)
        {
            _nextCached = s_globalCached;
            s_globalCached = this;
        }
    }

    private void Reset(int initialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 1));
        _written = 0;
        _nextCached = null;
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledRpcBufferWriter));
        var required = (long)_written + Math.Max(sizeHint, 1);
        if (required <= buffer.Length)
        {
            return;
        }

        if (required > MaxArrayLength)
        {
            throw new OutOfMemoryException(
                $"Requested buffer capacity ({required}) exceeds the maximum array length ({MaxArrayLength}).");
        }

        var newSize = (int)Math.Min(Math.Max(required, (long)buffer.Length * 2), MaxArrayLength);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        Array.Copy(buffer, 0, newBuffer, 0, _written);
        _buffer = newBuffer;
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
