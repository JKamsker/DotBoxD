using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

/// <summary>
/// Options for <see cref="RpcPeer"/> and <see cref="RpcHost"/>.
/// </summary>
public sealed class RpcPeerOptions
{
    public const int DefaultInboundQueueCapacity = 1024;
    public const int DefaultMaxPendingRequests = 4096;

    private int? _inboundQueueCapacity = DefaultInboundQueueCapacity;
    private int _maxPendingRequests = DefaultMaxPendingRequests;
    private ShaRpcQueueFullMode _queueFullMode = ShaRpcQueueFullMode.Wait;

    /// <summary>Default per-call timeout for proxies created by this peer.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional service provider for dispatcher factories that resolve dependencies.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; init; }

    /// <summary>
    /// When <see langword="true"/>, inbound request frames are answered with an explicit
    /// "this peer does not accept inbound calls" error rather than a "service not found"
    /// error. Use it to make a get-only ("client") peer's one-directional intent explicit.
    /// This is not an authentication or authorization boundary.
    /// </summary>
    public bool RejectInboundCalls { get; init; }

    /// <summary>
    /// Maximum queued inbound requests. The default applies bounded read-side backpressure. Null
    /// dispatches inbound requests immediately, does not cap concurrent dispatch work, and should
    /// only be used with trusted peers or externally bounded transports. In wait mode, request
    /// admission waits for bounded dispatch queue space.
    /// </summary>
    public int? InboundQueueCapacity
    {
        get => _inboundQueueCapacity;
        init
        {
            if (value is <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(InboundQueueCapacity),
                    value,
                    "Inbound queue capacity must be greater than zero.");
            }

            _inboundQueueCapacity = value;
        }
    }

    /// <summary>Maximum concurrent outbound calls waiting for responses.</summary>
    public int MaxPendingRequests
    {
        get => _maxPendingRequests;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxPendingRequests),
                    value,
                    "Maximum pending requests must be greater than zero.");
            }

            _maxPendingRequests = value;
        }
    }

    /// <summary>Policy used when <see cref="InboundQueueCapacity"/> is set and the request queue is full.</summary>
    public ShaRpcQueueFullMode QueueFullMode
    {
        get => _queueFullMode;
        init
        {
            if (!Enum.IsDefined(typeof(ShaRpcQueueFullMode), value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(QueueFullMode),
                    value,
                    "Unknown queue full mode.");
            }

            _queueFullMode = value;
        }
    }
}
