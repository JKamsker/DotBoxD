using System.Net;
using System.Net.Sockets;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.Tcp;

/// <summary>
/// TCP client transport implementation.
/// </summary>
public sealed class TcpTransport : ITransport
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private TcpConnection? _connection;
    private bool _disposed;

    public TcpTransport(string host, int port)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
    }

    public IConnection? Connection => _connection;

    public bool IsConnected => _connection?.IsConnected ?? false;

    /// <summary>
    /// Inter-read idle timeout applied to this connection's in-progress frame reads (slow-loris
    /// defense). <see langword="null"/> uses <see cref="TcpConnection.DefaultFrameReadIdleTimeout"/>;
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables it. See <see cref="TcpConnection"/>.
    /// </summary>
    public TimeSpan? FrameReadIdleTimeout { get; init; }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TcpTransport));
        }

        if (_connection != null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        _client = new TcpClient();

        // Wrap ConnectAsync with cancellation support for .NET Standard 2.1
        var connectTask = _client.ConnectAsync(_host, _port);
        var completedTask = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, ct));

        if (completedTask != connectTask)
        {
            _client.Dispose();
            ct.ThrowIfCancellationRequested();
        }

        await connectTask;
        _connection = new TcpConnection(_client, FrameReadIdleTimeout);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }

        _client?.Dispose();
    }
}
