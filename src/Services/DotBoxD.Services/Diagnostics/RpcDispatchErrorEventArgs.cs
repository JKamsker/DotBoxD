namespace DotBoxD.Services.Diagnostics;

/// <summary>
/// Describes an inbound request dispatch or response-send failure.
/// </summary>
public sealed class RpcDispatchErrorEventArgs : EventArgs
{
    public RpcDispatchErrorEventArgs(
        string remoteEndpoint,
        int messageId,
        string serviceName,
        string methodName,
        string? instanceId,
        Exception error)
    {
        RemoteEndpoint = DiagnosticArgumentGuard.RequireNonBlank(
            remoteEndpoint,
            nameof(remoteEndpoint),
            "Remote endpoint");
        MessageId = messageId;
        ServiceName = DiagnosticArgumentGuard.RequireNonBlank(serviceName, nameof(serviceName), "Service name");
        MethodName = DiagnosticArgumentGuard.RequireNonBlank(methodName, nameof(methodName), "Method name");
        InstanceId = instanceId;
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    /// <summary>The remote endpoint string of the channel that sent the request.</summary>
    public string RemoteEndpoint { get; }

    /// <summary>The inbound request message id.</summary>
    public int MessageId { get; }

    /// <summary>The requested service name.</summary>
    public string ServiceName { get; }

    /// <summary>The requested method name.</summary>
    public string MethodName { get; }

    /// <summary>The requested service instance id, when the request targets an instance.</summary>
    public string? InstanceId { get; }

    /// <summary>The dispatch or response-send failure.</summary>
    public Exception Error { get; }

}
