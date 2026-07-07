using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal sealed class FailingStartServerTransport : IServerTransport
{
    private readonly Exception _failure;
    private int _startCalls;

    public FailingStartServerTransport(Exception failure) => _failure = failure;

    public int StartCalls => Volatile.Read(ref _startCalls);

    public Task StartAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _startCalls);
        throw _failure;
    }

    public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("Accept should never run when start fails.");

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => default;
}

internal sealed class MultiConnectionServerTransport : IServerTransport
{
    private readonly System.Threading.Channels.Channel<IRpcChannel> _connections =
        System.Threading.Channels.Channel.CreateUnbounded<IRpcChannel>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
    private int _disposed;

    public void EnqueueConnection(IRpcChannel connection) => _connections.Writer.TryWrite(connection);

    public Task StartAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MultiConnectionServerTransport));
        }

        return Task.CompletedTask;
    }

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        try
        {
            return await _connections.Reader.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _connections.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _connections.Writer.TryComplete();
        return default;
    }
}

internal sealed class FaultThenAcceptServerTransport : IServerTransport
{
    private readonly int _faultCount;
    private readonly IRpcChannel _connection;
    private int _acceptCalls;
    private int _delivered;

    public FaultThenAcceptServerTransport(int faultCount, IRpcChannel connection)
    {
        _faultCount = faultCount;
        _connection = connection;
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        var call = Interlocked.Increment(ref _acceptCalls);
        if (call <= _faultCount)
        {
            throw new InvalidOperationException($"transient accept failure #{call}");
        }

        if (Interlocked.Exchange(ref _delivered, 1) == 0)
        {
            return _connection;
        }

        await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
        throw new OperationCanceledException(ct);
    }

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => default;
}

internal sealed class DisposeTrackingSingleConnectionTransport : IServerTransport
{
    private readonly IRpcChannel _connection;
    private readonly TaskCompletionSource<bool> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _accepted;
    private int _started;
    private int _disposed;

    public DisposeTrackingSingleConnectionTransport(IRpcChannel connection) => _connection = connection;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public Task StartAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _started, 1);
        return Task.CompletedTask;
    }

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _started) == 0)
        {
            throw new InvalidOperationException("Transport not started.");
        }

        if (Interlocked.Exchange(ref _accepted, 1) == 0)
        {
            return _connection;
        }

        using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), _stopped))
        {
            await _stopped.Task.ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
        throw new OperationCanceledException();
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _stopped.TrySetResult(true);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _stopped.TrySetResult(true);
        return default;
    }
}
