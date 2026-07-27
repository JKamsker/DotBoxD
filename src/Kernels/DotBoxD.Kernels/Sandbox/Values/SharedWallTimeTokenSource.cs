namespace DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// A wall-time <see cref="CancellationTokenSource"/> shared for one sandbox-context generation.
/// The resource meter owns a fixed absolute deadline, so binding dispatch arms this source once
/// and temporarily links cancelable run tokens through allocation-free registrations.
/// </summary>
/// <remarks>
/// The owning context disposes this source at the execution boundary. Public wall-time token
/// requests receive a separate caller-owned source and cannot cancel or dispose this internal timer.
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

}
