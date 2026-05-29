namespace ShaRPC.Core.Client;

/// <summary>
/// Interface for the ShaRPC client.
/// </summary>
public interface IShaRpcClient : IAsyncDisposable
{
    /// <summary>
    /// Connects to the server.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Invokes an RPC method.
    /// </summary>
    Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Invokes an RPC method with no request body.
    /// </summary>
    Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default);

    /// <summary>
    /// Invokes an RPC method with no response body.
    /// </summary>
    Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Invokes an RPC method with neither a request nor a response body. Sends an empty
    /// payload rather than serializing a placeholder argument.
    /// </summary>
    Task InvokeAsync(
        string service,
        string method,
        CancellationToken ct = default);

    /// <summary>
    /// Invokes a method on a specific server-side sub-service instance previously
    /// obtained via a root call. Mirrors <see cref="InvokeAsync{TRequest,TResponse}(string,string,TRequest,CancellationToken)"/>
    /// but threads the opaque <paramref name="instanceId"/> issued by the server's
    /// instance registry through the wire request.
    /// </summary>
    Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Invokes an instance-scoped RPC method with no request body and a response payload.
    /// </summary>
    Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default);

    /// <summary>
    /// Invokes an instance-scoped RPC method with a request body and no response payload.
    /// </summary>
    Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Invokes an instance-scoped RPC method with neither a request nor a response body.
    /// Sends an empty payload rather than serializing a placeholder argument.
    /// </summary>
    Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default);

    /// <summary>
    /// Gets whether the client is connected.
    /// </summary>
    bool IsConnected { get; }
}
