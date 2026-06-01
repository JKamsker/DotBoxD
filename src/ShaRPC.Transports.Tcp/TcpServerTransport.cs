using System.Net;
using System.Net.Sockets;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.Tcp;

/// <summary>
/// TCP server transport implementation.
/// </summary>
public sealed class TcpServerTransport : IServerTransport
{
    private readonly IPAddress _address;
    private readonly int _port;
    private TcpListener? _listener;
    private bool _disposed;
    private bool _started;

    public TcpServerTransport(int port) : this(IPAddress.Any, port)
    {
    }

    public TcpServerTransport(IPAddress address, int port)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _port = port;
    }

    public TcpServerTransport(string address, int port)
    {
        _address = IPAddress.Parse(address);
        _port = port;
    }

    /// <summary>
    /// Gets the bound endpoint after <see cref="StartAsync"/> succeeds.
    /// </summary>
    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint as IPEndPoint;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TcpServerTransport));
        }

        if (_started)
        {
            throw new InvalidOperationException("Server already started.");
        }

        _listener = new TcpListener(_address, _port);
        _listener.Start();
        _started = true;

        return Task.CompletedTask;
    }

    public async Task<IConnection> AcceptAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TcpServerTransport));
        }

        if (_listener == null)
        {
            throw new InvalidOperationException("Server not started.");
        }

        // Wrap AcceptTcpClientAsync with cancellation support for .NET Standard 2.1
        var acceptTask = _listener.AcceptTcpClientAsync();

        while (!acceptTask.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            await Task.WhenAny(acceptTask, Task.Delay(100, ct));
        }

        var client = await acceptTask;
        return new TcpConnection(client);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _listener?.Stop();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return default;
        }

        _disposed = true;
        _listener?.Stop();

        return default;
    }
}
