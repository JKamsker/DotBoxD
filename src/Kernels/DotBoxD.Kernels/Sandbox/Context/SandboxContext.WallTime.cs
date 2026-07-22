using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext : IDisposable
{
    private SharedWallTimeTokenSource? _sharedWallTimeToken;

    internal BindingWallTimeTokenLease CreateBindingWallTimeToken()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        CancellationToken.ThrowIfCancellationRequested();
        Budget.CheckDeadline();
        var shared = GetOrCreateSharedWallTimeToken();
        var registration = CancellationToken.CanBeCanceled
            ? CancellationToken.UnsafeRegister(
                static state => ((SharedWallTimeTokenSource)state!).Cancel(),
                shared)
            : default;
        return new BindingWallTimeTokenLease(shared, registration);
    }

    public CancellationTokenSource CreateWallTimeToken()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var remaining = Budget.RemainingWallTime();
        var timeout = CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(CancellationToken)
            : new CancellationTokenSource();
        timeout.CancelAfter(remaining);
        return timeout;
    }

    internal void ReleaseExecutionResources()
    {
        ClearCompiledReturnValidation();
        Interlocked.Exchange(ref _sharedWallTimeToken, null)?.Dispose();
    }

    private SharedWallTimeTokenSource GetOrCreateSharedWallTimeToken()
    {
        var shared = Volatile.Read(ref _sharedWallTimeToken);
        if (shared is not null)
        {
            return shared;
        }

        var created = new SharedWallTimeTokenSource();
        created.ArmDeadline(Budget.RemainingWallTime());
        shared = Interlocked.CompareExchange(ref _sharedWallTimeToken, created, null);
        if (shared is not null)
        {
            created.Dispose();
            return shared;
        }

        return created;
    }
}
