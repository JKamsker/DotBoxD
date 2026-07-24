using System.Buffers;
using System.Buffers.Binary;
using DotBoxD.Services.Buffers;

namespace DotBoxD.LookaheadCapacityProbe;

internal sealed class LookaheadFrameReader : IDisposable
{
    private const int LengthPrefixSize = sizeof(int);
    private const int MaximumFrameLength = 16 * 1024 * 1024;

    private readonly byte[] _buffer;
    private readonly int _capacity;
    private readonly Stream _stream;
    private int _disposed;
    private int _end;
    private int _start;

    public LookaheadFrameReader(Stream stream, int capacity)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, LengthPrefixSize);

        _stream = stream;
        _capacity = capacity;
        _buffer = ArrayPool<byte>.Shared.Rent(capacity);
    }

    public async ValueTask<Payload> ReadFrameAsync()
    {
        await EnsurePrefixAsync().ConfigureAwait(false);

        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(
            _buffer.AsSpan(_start, LengthPrefixSize));
        if (totalLength < LengthPrefixSize || totalLength > MaximumFrameLength)
        {
            throw new InvalidDataException($"Invalid frame length {totalLength:N0}.");
        }

        var payload = Payload.Rent(totalLength);
        try
        {
            var bufferedLength = Math.Min(_end - _start, totalLength);
            _buffer.AsSpan(_start, bufferedLength).CopyTo(payload.Memory.Span);
            Consume(bufferedLength);

            var written = bufferedLength;
            while (written < totalLength)
            {
                var read = await _stream.ReadAsync(payload.Memory.Slice(written, totalLength - written))
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"Connection closed after {written:N0} of {totalLength:N0} frame bytes.");
                }

                written += read;
            }

            var result = payload;
            payload = null;
            return result;
        }
        finally
        {
            payload?.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }

    private void Consume(int length)
    {
        _start += length;
        if (_start == _end)
        {
            _start = 0;
            _end = 0;
        }
    }

    private async ValueTask EnsurePrefixAsync()
    {
        while (_end - _start < LengthPrefixSize)
        {
            Compact();
            var read = await _stream.ReadAsync(_buffer.AsMemory(_end, _capacity - _end))
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    $"Connection closed with {_end - _start:N0} buffered prefix bytes.");
            }

            _end += read;
        }
    }

    private void Compact()
    {
        if (_start == 0)
        {
            return;
        }

        var available = _end - _start;
        _buffer.AsSpan(_start, available).CopyTo(_buffer);
        _start = 0;
        _end = available;
    }
}
