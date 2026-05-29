using System.Buffers;

namespace ShaRPC.Core.Buffers;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> backed by an array rented from <see cref="ArrayPool{T}"/>.
/// Either hand the written bytes off via <see cref="DetachPayload"/> or release them via
/// <see cref="Dispose"/> — never both.
/// </summary>
public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[]? _buffer;
    private int _written;

    public PooledBufferWriter(int initialCapacity = 256)
    {
        if (initialCapacity <= 0)
        {
            initialCapacity = 256;
        }

        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _written = 0;
    }

    /// <summary>
    /// The bytes written so far.
    /// </summary>
    public ReadOnlyMemory<byte> WrittenMemory =>
        (_buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter))).AsMemory(0, _written);

    /// <summary>
    /// The number of bytes written so far.
    /// </summary>
    public int WrittenCount => _written;

    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
        if (_written + count > buffer.Length)
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

    /// <summary>
    /// Hands the rented array and written length to a new <see cref="Payload"/> and relinquishes
    /// ownership. The writer must not be used afterward.
    /// </summary>
    public Payload DetachPayload()
    {
        var buffer = _buffer ?? throw new InvalidOperationException("Buffer has already been detached or disposed.");
        _buffer = null;
        return new Payload(buffer, _written);
    }

    /// <summary>
    /// Returns the rented array to the pool. A no-op after <see cref="DetachPayload"/>.
    /// </summary>
    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null)
        {
            return;
        }

        _buffer = null;
        ArrayPool<byte>.Shared.Return(buffer);
    }

    private void EnsureCapacity(int sizeHint)
    {
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));

        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        var required = _written + Math.Max(sizeHint, 1);
        if (required <= buffer.Length)
        {
            return;
        }

        var newSize = Math.Max(required, buffer.Length * 2);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        Array.Copy(buffer, 0, newBuffer, 0, _written);
        _buffer = newBuffer;
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
