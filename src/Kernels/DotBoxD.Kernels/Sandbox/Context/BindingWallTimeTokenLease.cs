using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Sandbox;

/// <summary>
/// Temporarily links one binding call to its run token while sharing the run's fixed wall-time
/// deadline source. Disposing a lease removes only that call's run-token registration.
/// </summary>
internal readonly struct BindingWallTimeTokenLease : IDisposable
{
    private readonly SharedWallTimeTokenSource? _source;
    private readonly CancellationTokenRegistration _runCancellationRegistration;

    public BindingWallTimeTokenLease(
        SharedWallTimeTokenSource source,
        CancellationTokenRegistration runCancellationRegistration)
    {
        _source = source;
        _runCancellationRegistration = runCancellationRegistration;
    }

    public CancellationToken Token => _source?.Token ?? default;

    public bool IsCancellationRequested => _source?.IsCancellationRequested == true;

    public void Dispose() => _runCancellationRegistration.Dispose();
}
