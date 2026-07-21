using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext
{
    private SharedWallTimeTokenSource? _sharedWallTimeToken;

    internal BindingWallTimeTokenLease CreateBindingWallTimeToken()
    {
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
        // Preserve the public ownership contract: cancelable callers receive a source they own,
        // while non-cancelable runs reuse the context-owned deadline source.
        if (!CancellationToken.CanBeCanceled)
        {
            var shared = GetOrCreateSharedWallTimeToken();
            shared.ArmDeadline(Budget.RemainingWallTime());
            return shared;
        }

        var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(Budget.RemainingWallTime());
        return timeout;
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
            created.DisposeOwned();
            return shared;
        }

        return created;
    }
}
