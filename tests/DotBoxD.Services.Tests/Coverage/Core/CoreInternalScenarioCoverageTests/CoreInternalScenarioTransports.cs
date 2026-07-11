using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal sealed class AlwaysFaultServerTransport : IServerTransport
{
    private int _stopped;

    public bool WasStopped => Volatile.Read(ref _stopped) != 0;

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("accept always faults");

    public Task StopAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _stopped, 1);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => default;
}

internal sealed class GatedStartServerTransport : IServerTransport
{
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _stopped;
    private int _disposed;

    public TaskCompletionSource<bool> StartEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool WasStopped => Volatile.Read(ref _stopped) != 0;

    public bool WasDisposed => Volatile.Read(ref _disposed) != 0;

    public void ReleaseStart() => _release.TrySetResult(true);

    public async Task StartAsync(CancellationToken ct = default)
    {
        StartEntered.TrySetResult(true);
        await _release.Task.ConfigureAwait(false);
    }

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
        throw new OperationCanceledException(ct);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _stopped, 1);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _release.TrySetResult(true);
        return default;
    }
}
