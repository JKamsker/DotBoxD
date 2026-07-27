using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>
/// Optional low-allocation transport contract for channels that can transfer ownership
/// of complete pooled frames instead of copying them into a new <see cref="Payload"/>.
/// </summary>
public interface IRpcFrameChannel : IRpcValueTaskChannel
{
    /// <summary>
    /// Sends a complete pooled frame and transfers its ownership to the channel.
    /// </summary>
    /// <remarks>
    /// Ownership transfers immediately when this invocation returns a <see cref="ValueTask"/>,
    /// including an already faulted or canceled result. The channel then owns
    /// <paramref name="frame"/> through terminal success, failure, or cancellation and must dispose
    /// it; the caller must not read, detach, or dispose it. If the invocation throws synchronously,
    /// ownership remains with the caller, which must dispose the frame. The implementation must drop
    /// its raw writer reference when the returned operation terminates.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default);

    /// <summary>
    /// Receives a complete frame whose ownership transfers to the caller on successful completion.
    /// </summary>
    /// <remarks>
    /// The caller must consume the returned logical frame exactly once by calling either
    /// <see cref="RpcFrame.DetachPayload"/> or <see cref="RpcFrame.Dispose"/>.
    /// </remarks>
    ValueTask<RpcFrame> ReceiveFrameValueAsync(CancellationToken ct = default);
}
