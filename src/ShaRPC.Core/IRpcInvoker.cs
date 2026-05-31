namespace ShaRPC.Core;

/// <summary>
/// The call surface a generated proxy uses to invoke methods the other side provides.
/// Implemented by <see cref="ShaRPC.Core.RpcPeer"/> and (transitively) by
/// <see cref="ShaRPC.Core.Client.IShaRpcClient"/>. This is the transport-agnostic invoke
/// contract with no notion of "client" or "connect" — a peer simply forwards calls.
/// </summary>
public interface IRpcInvoker
{
    /// <summary>Invokes a method with a request body and a response body.</summary>
    Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>Invokes a method with no request body and a response body.</summary>
    Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default);

    /// <summary>Invokes a method with a request body and no response body.</summary>
    Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>Invokes a method with neither a request nor a response body.</summary>
    Task InvokeAsync(
        string service,
        string method,
        CancellationToken ct = default);

    /// <summary>Invokes a method on a specific remote sub-service instance.</summary>
    Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>Invokes an instance-scoped method with no request body and a response body.</summary>
    Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default);

    /// <summary>Invokes an instance-scoped method with a request body and no response body.</summary>
    Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>Invokes an instance-scoped method with neither a request nor a response body.</summary>
    Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default);
}
