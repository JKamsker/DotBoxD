using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

/// <summary>
/// Options for <see cref="RpcPeer"/> and <see cref="RpcHost"/>.
/// </summary>
public sealed class RpcPeerOptions
{
    /// <summary>Default per-call timeout for proxies created by this peer.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional service provider for dispatcher factories that resolve dependencies.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// When <see langword="true"/>, inbound request frames are answered with an explicit
    /// "this peer does not accept inbound calls" error rather than a "service not found"
    /// error. Use it to make a get-only ("client") peer's one-directional intent explicit.
    /// </summary>
    public bool RejectInboundCalls { get; set; }

    /// <summary>Maximum queued inbound frames. Null uses unbounded queues.</summary>
    public int? InboundQueueCapacity { get; set; }

    /// <summary>Policy used when <see cref="InboundQueueCapacity"/> is set and the queue is full.</summary>
    public ShaRpcQueueFullMode QueueFullMode { get; set; } = ShaRpcQueueFullMode.Wait;
}
