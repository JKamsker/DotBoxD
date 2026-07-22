using System.Runtime.CompilerServices;
using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>
/// Owns one received wire frame. The frame can be backed by the legacy <see cref="Payload"/>
/// owner or by a pooled writer transferred by a low-allocation transport.
/// <para>
/// Copies of this value alias one logical owner, so callers must coordinate detachment. A
/// writer-backed owner can be detached only once across all aliases; stale writer aliases fail
/// closed and cannot access or dispose a later pooled-writer lease. <see cref="Dispose"/> is
/// idempotent across aliases and both backing owners. Follow the boolean contract returned by
/// <c>RpcPeerFrameProcessor.ShouldDisposeAsync</c> inside <c>RpcPeerReadLoop</c>:
/// <see langword="true"/> means the caller owns the frame, <see langword="false"/> means the read
/// loop retained ownership (for example, a <c>StreamItem</c>).
/// </para>
/// </summary>
public struct RpcFrame : IDisposable
{
    private object? _owner;
    private long _writerLeaseToken;

    public RpcFrame(Payload payload)
    {
        _owner = payload ?? throw new ArgumentNullException(nameof(payload));
        _writerLeaseToken = 0;
    }

    public RpcFrame(PooledBufferWriter writer)
    {
        _owner = writer ?? throw new ArgumentNullException(nameof(writer));
        _writerLeaseToken = writer.LeaseToken;
    }

    public ReadOnlyMemory<byte> Memory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var owner = _owner ?? throw new ObjectDisposedException(nameof(RpcFrame));
            if (_writerLeaseToken == 0)
            {
                return ((Payload)owner).Memory;
            }

            return ((PooledBufferWriter)owner).GetWrittenMemory(_writerLeaseToken);
        }
    }

    public int Length => Memory.Length;

    internal bool IsWriterBacked => _writerLeaseToken != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Payload DetachPayload()
    {
        var owner = _owner ?? throw new ObjectDisposedException(nameof(RpcFrame));
        if (_writerLeaseToken == 0)
        {
            var payload = (Payload)owner;
            _ = payload.Memory;
            _owner = null;
            return payload;
        }

        var detached = ((PooledBufferWriter)owner).DetachLeasePayload(_writerLeaseToken);
        _owner = null;
        _writerLeaseToken = 0;
        return detached;
    }

    internal RpcFrame MaterializePayloadOwner() =>
        IsWriterBacked ? new RpcFrame(DetachPayload()) : this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        var owner = _owner;
        _owner = null;
        if (owner is null)
        {
            return;
        }

        var writerLeaseToken = _writerLeaseToken;
        _writerLeaseToken = 0;
        if (writerLeaseToken == 0)
        {
            ((Payload)owner).Dispose();
        }
        else
        {
            ((PooledBufferWriter)owner).DisposeLease(writerLeaseToken);
        }
    }
}
