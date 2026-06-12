using ShaRPC.Core.Buffers;

namespace ShaRPC.Core.Transport;

/// <summary>
/// Owns one received wire frame. The frame can be backed by the legacy <see cref="Payload"/>
/// owner or by a pooled writer transferred by a low-allocation transport.
/// </summary>
public struct RpcFrame : IDisposable
{
    private Payload? _payload;
    private PooledBufferWriter? _writer;
    private ReadOnlyMemory<byte> _memory;

    public RpcFrame(Payload payload)
    {
        _payload = payload;
        _writer = null;
        _memory = payload.Memory;
    }

    public RpcFrame(PooledBufferWriter writer)
    {
        _payload = null;
        _writer = writer;
        _memory = writer.WrittenMemory;
    }

    public ReadOnlyMemory<byte> Memory => _memory;

    public int Length => _memory.Length;

    public Payload DetachPayload()
    {
        if (_payload is { } payload)
        {
            _payload = null;
            _memory = ReadOnlyMemory<byte>.Empty;
            return payload;
        }

        if (_writer is { } writer)
        {
            _writer = null;
            _memory = ReadOnlyMemory<byte>.Empty;
            var detached = writer.DetachPayload();
            writer.Dispose();
            return detached;
        }

        throw new ObjectDisposedException(nameof(RpcFrame));
    }

    public void Dispose()
    {
        _payload?.Dispose();
        _payload = null;
        _writer?.Dispose();
        _writer = null;
        _memory = ReadOnlyMemory<byte>.Empty;
    }
}
