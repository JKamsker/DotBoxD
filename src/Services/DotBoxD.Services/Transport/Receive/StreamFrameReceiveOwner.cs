using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>Owns the selected frame buffer while a serialized stream receive is in progress.</summary>
internal struct StreamFrameReceiveOwner
{
    private object? _owner;

    public readonly bool IsAllocated => _owner is not null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Initialize(
        int totalLength,
        bool writerBacked,
        ReadOnlySpan<byte> lengthPrefix)
    {
        if (_owner is not null)
        {
            throw new InvalidOperationException("A receive frame buffer is already initialized.");
        }

        if (writerBacked)
        {
            var writer = PooledBufferWriter.Rent(totalLength, totalLength);
            _owner = writer;
            lengthPrefix.CopyTo(writer.GetSpan(lengthPrefix.Length));
            writer.Advance(lengthPrefix.Length);
            return;
        }

        var payload = Payload.Rent(totalLength);
        _owner = payload;
        lengthPrefix.CopyTo(payload.Memory.Span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Initialize(int totalLength, bool writerBacked)
    {
        Span<byte> lengthPrefix = stackalloc byte[StreamFrameReadOperations.LengthPrefixSize];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, totalLength);
        Initialize(totalLength, writerBacked, lengthPrefix);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Memory<byte> PrepareBodyRead(int remaining, bool writerBacked)
    {
        var owner = _owner ?? throw new InvalidOperationException(
            "The receive frame buffer is not initialized.");
        if (writerBacked)
        {
            return ((PooledBufferWriter)owner).GetMemory(remaining).Slice(0, remaining);
        }

        var payload = (Payload)owner;
        return payload.Memory.Slice(payload.Length - remaining, remaining);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void AdvanceBodyRead(int count, bool writerBacked)
    {
        var owner = _owner;
        if (writerBacked && owner is not null)
        {
            ((PooledBufferWriter)owner).Advance(count);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CopyBodyFrom(ReadOnlySpan<byte> source, int remaining, bool writerBacked)
    {
        var count = Math.Min(source.Length, remaining);
        if (count == 0)
        {
            return remaining;
        }

        source[..count].CopyTo(PrepareBodyRead(remaining, writerBacked).Span);
        AdvanceBodyRead(count, writerBacked);

        return remaining - count;
    }

    public readonly int GetTargetBodyLength(int remaining) =>
        _owner switch
        {
            Payload payload => payload.Length - StreamFrameReadOperations.LengthPrefixSize,
            PooledBufferWriter writer =>
                writer.WrittenCount + remaining - StreamFrameReadOperations.LengthPrefixSize,
            _ => StreamFrameReadOperations.LengthPrefixSize,
        };

    public readonly int GetBodyBytesRead(int remaining) =>
        _owner switch
        {
            Payload payload =>
                payload.Length - StreamFrameReadOperations.LengthPrefixSize - remaining,
            PooledBufferWriter writer =>
                writer.WrittenCount - StreamFrameReadOperations.LengthPrefixSize,
            _ => 0,
        };

    public RpcFrame TransferFrame(bool writerBacked)
    {
        var owner = _owner ?? throw new InvalidOperationException(
            "The receive frame buffer is not initialized.");
        _owner = null;
        return writerBacked
            ? new RpcFrame((PooledBufferWriter)owner)
            : new RpcFrame((Payload)owner);
    }

    public void Dispose(bool writerBacked)
    {
        var owner = _owner;
        _owner = null;
        if (writerBacked)
        {
            ((PooledBufferWriter?)owner)?.Dispose();
        }
        else
        {
            ((Payload?)owner)?.Dispose();
        }
    }
}
