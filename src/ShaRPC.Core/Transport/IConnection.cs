using ShaRPC.Core.Buffers;

namespace ShaRPC.Core.Transport;

/// <summary>
/// Represents a bidirectional connection for sending and receiving data.
/// </summary>
public interface IConnection : IAsyncDisposable
{
    /// <summary>
    /// Sends data over the connection.
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>
    /// Receives a framed message from the connection. The caller owns the returned
    /// <see cref="Payload"/> and must dispose it. A payload with <see cref="Payload.Length"/>
    /// of 0 (e.g. <see cref="Payload.Empty"/>) signals the connection was closed.
    /// </summary>
    Task<Payload> ReceiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets whether the connection is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets a string representation of the remote endpoint.
    /// </summary>
    string RemoteEndpoint { get; }
}
