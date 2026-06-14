namespace DotBoxD.Services;

/// <summary>
/// DotBoxD-defined remote error type names.
/// </summary>
public static class RpcErrorTypes
{
    /// <summary>Remote type name used when an internal service failure is hidden from the caller.</summary>
    public const string InternalError = "DotBoxDRpcInternalError";

    /// <summary>Remote type name used when a peer rejects all inbound calls by configuration.</summary>
    public const string InboundRejected = "DotBoxDRpcInboundRejected";

    /// <summary>Remote type name used when the requested service is not registered.</summary>
    public const string ServiceNotFound = "DotBoxDServiceNotFound";

    /// <summary>Remote type name used when the requested method is not found on the service.</summary>
    public const string MethodNotFound = "DotBoxDMethodNotFound";

    /// <summary>Remote type name used when a sub-service instance is missing or has expired.</summary>
    public const string InstanceNotFound = "DotBoxDRpcInstanceNotFound";

    /// <summary>Remote type name used when the peer drops an inbound call because its queue is full.</summary>
    public const string QueueFull = "DotBoxDRpcQueueFull";

    /// <summary>Remote type name used when a protocol-level error is detected.</summary>
    public const string ProtocolError = "DotBoxDRpcProtocolError";
}
