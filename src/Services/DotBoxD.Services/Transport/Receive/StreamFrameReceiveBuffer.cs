using System.Buffers;
using System.Buffers.Binary;

namespace DotBoxD.Services.Transport;

/// <summary>
/// Marks stream adapters whose reads return promptly with the bytes currently available. The
/// connection must also own the stream before it is allowed to read ahead.
/// </summary>
internal interface IStreamReceiveLookaheadCapable
{
}

internal struct StreamFrameReceiveBuffer
{
    public const int LookaheadCapacity = 16 * 1024;
    private const byte LookaheadMissBackoffFrames = byte.MaxValue;

    private byte[]? _buffer;
    private ushort _start;
    private ushort _end;
    private bool _readBodyWithLookahead;
    private bool _bodyLookaheadRead;
    private byte _lookaheadMissCountdown;
    private bool _writerBackedOwner;

    public readonly int Count => _end - _start;

    public readonly int PrefixBytesRemaining =>
        Math.Max(0, StreamFrameReadOperations.LengthPrefixSize - Count);

    public readonly bool ReadBodyWithLookahead => _readBodyWithLookahead;

    public bool WriterBackedOwner
    {
        readonly get => _writerBackedOwner;
        set => _writerBackedOwner = value;
    }

    public void BeginFrame()
    {
        _bodyLookaheadRead = false;
        if (Count != 0 || _lookaheadMissCountdown == 0)
        {
            _readBodyWithLookahead = true;
            return;
        }

        _lookaheadMissCountdown--;
        _readBodyWithLookahead = false;
    }

    public void DisableBodyLookahead() => _readBodyWithLookahead = false;

    public void ApplyFrameLength(int totalLength) =>
        _readBodyWithLookahead &= totalLength <= LookaheadCapacity;

    public Memory<byte> PrepareRead()
    {
        var buffer = EnsureLookaheadBuffer();
        if (_start != 0)
        {
            var count = Count;
            buffer.AsSpan(_start, count).CopyTo(buffer);
            _start = 0;
            _end = checked((ushort)count);
        }

        return buffer.AsMemory(_end, LookaheadCapacity - _end);
    }

    public int CommitRead(int count)
    {
        _end = checked((ushort)(_end + count));
        return PrefixBytesRemaining;
    }

    public void CommitBodyRead(int count)
    {
        _bodyLookaheadRead = true;
        _ = CommitRead(count);
    }

    public int ConsumeLengthPrefix()
    {
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(
            _buffer!.AsSpan(_start, StreamFrameReadOperations.LengthPrefixSize));
        Advance(StreamFrameReadOperations.LengthPrefixSize);
        return totalLength;
    }

    public int CopyBodyTo(ref StreamFrameReceiveOwner owner, int remaining)
    {
        var count = Math.Min(Count, remaining);
        if (count == 0)
        {
            return remaining;
        }

        remaining = owner.CopyBodyFrom(
            _buffer!.AsSpan(_start, count),
            remaining,
            _writerBackedOwner);
        Advance(count);
        return remaining;
    }

    public void ReturnPooledBuffer()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void ReturnPooledBufferIfEmpty()
    {
        if (Count == 0 && _buffer is not null)
        {
            if (_bodyLookaheadRead)
            {
                _lookaheadMissCountdown = LookaheadMissBackoffFrames;
            }

            ReturnPooledBuffer();
        }
    }

    internal readonly bool HasBuffer => _buffer is not null;

    private byte[] EnsureLookaheadBuffer()
    {
        if (_buffer is not null)
        {
            return _buffer;
        }

        _buffer = ArrayPool<byte>.Shared.Rent(LookaheadCapacity);
        return _buffer;
    }

    private void Advance(int count)
    {
        _start = checked((ushort)(_start + count));
        if (_start == _end)
        {
            _start = 0;
            _end = 0;
        }
    }
}
