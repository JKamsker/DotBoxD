namespace DotBoxD.Services.Transport;

/// <summary>
/// Represents a server-side transport that accepts incoming connections.
/// </summary>
public interface IServerTransport : IAsyncDisposable
{
    /// <summary>
    /// Starts listening for connections.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Accepts an incoming connection.
    /// </summary>
    Task<IRpcChannel> AcceptAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops listening for connections.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <remarks>
    /// If disposal re-enters the owning <c>RpcHost</c>, await that call from the disposal execution
    /// context. Do not dispatch it through APIs that suppress <see cref="ExecutionContext"/> flow,
    /// because the host cannot distinguish that detached work from an unrelated concurrent disposal.
    /// </remarks>
}
