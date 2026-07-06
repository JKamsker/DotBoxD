using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>
/// Owns one received wire frame. The frame can be backed by the legacy <see cref="Payload"/>
/// owner or by a pooled writer transferred by a low-allocation transport.
/// <para>
/// This is a value type and may be copied. Ownership of the underlying <see cref="Payload"/> or
/// <see cref="PooledBufferWriter"/> transfers only through <see cref="DetachPayload"/>; otherwise
/// callers dispose it through <see cref="Dispose"/>. Follow the boolean contract returned by
/// <c>RpcPeerFrameProcessor.ShouldDisposeAsync</c> inside <c>RpcPeerReadLoop</c>: <see langword="true"/>
/// means the caller owns the frame, <see langword="false"/> means the read loop retained ownership
/// (for example, a <c>StreamItem</c>). Both backing owners are idempotent, so double-dispose is safe.
/// </para>
/// </summary>
public struct RpcFrame : IDisposable
{
    private Payload? _payload;
    private PooledBufferWriter? _writer;

    public RpcFrame(Payload payload)
    {
        _payload = payload ?? throw new ArgumentNullException(nameof(payload));
        _writer = null;
    }

    public RpcFrame(PooledBufferWriter writer)
    {
        _payload = null;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public ReadOnlyMemory<byte> Memory
    {
        get
        {
            if (_payload is { } payload)
            {
                return payload.Memory;
            }

            if (_writer is { } writer)
            {
                return writer.WrittenMemory;
            }

            throw new ObjectDisposedException(nameof(RpcFrame));
        }
    }

    public int Length => Memory.Length;

    public Payload DetachPayload()
    {
        if (_payload is { } payload)
        {
            _ = payload.Memory;
            _payload = null;
            return payload;
        }

        if (_writer is { } writer)
        {
            _ = writer.WrittenMemory;
            _writer = null;
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
    }
}
