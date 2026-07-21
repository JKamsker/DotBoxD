namespace DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// A wall-time <see cref="CancellationTokenSource"/> shared for one sandbox-context generation.
/// The resource meter owns a fixed absolute deadline, so binding dispatch arms this source once
/// and temporarily links cancelable run tokens through allocation-free registrations.
/// </summary>
/// <remarks>
/// The public compatibility path can still return this source and callers may dispose it, so
/// <see cref="Dispose(bool)"/> remains a no-op. The owning context uses <see cref="DisposeOwned"/>
/// when replacing a recycled generation.
/// </remarks>
internal sealed class SharedWallTimeTokenSource : CancellationTokenSource
{
    public void ArmDeadline(TimeSpan remaining)
    {
        // CancelAfter reuses the source's internal timer after the first call,
        // so re-arming the shared instance does not allocate per binding call.
        // A source that has already fired (run is aborting) ignores this.
        if (!IsCancellationRequested)
        {
            CancelAfter(remaining);
        }
    }

    public void DisposeOwned() => base.Dispose(disposing: true);

    // Public CreateWallTimeToken callers may dispose the shared source they receive.
    protected override void Dispose(bool disposing)
    {
    }
}
