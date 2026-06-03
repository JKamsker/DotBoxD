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

    public IRpcChannel? Connection => _connection;

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

        // netstandard2.1 has no CancellationToken overload for ConnectAsync, so race it against an
        // infinite delay bound to the token. The delay is cancelled in the finally so its timer and
        // token registration are released on the success path instead of lingering for ct's lifetime.
        var connectTask = _client.ConnectAsync(_host, _port);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            if (await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, connectCts.Token)) != connectTask)
            {
                _client.Dispose();
                // The connect attempt now faults against the disposed client; observe it so it does not
                // surface later as an unobserved task exception.
                ObserveFault(connectTask);
                ct.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            connectCts.Cancel();
        }

        await connectTask;
        _connection = new TcpConnection(_client, FrameReadIdleTimeout);
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

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
