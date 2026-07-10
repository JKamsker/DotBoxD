using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<InstalledKernel> _kernels = new(ReferenceEqualityComparer.Instance);
    private PluginDebugSession? _attached;
    private DebugPause? _pause;
    private bool _disposed;

    public PluginDebugCoordinator(PluginRemoteDebugOptions? options)
    {
        options ??= new PluginRemoteDebugOptions();
        options.Validate();
        Options = options with { AllowedPauseScopes = options.SnapshotAllowedPauseScopes().ToArray() };
        AllowedPauseScopes = Options.AllowedPauseScopes.ToHashSet();
    }

    public PluginRemoteDebugOptions Options { get; }

    public IReadOnlySet<KernelDebugPauseScope> AllowedPauseScopes { get; }

    public bool TryAttach(PluginDebugSession session, KernelDebugPauseScope pauseScope)
    {
        InstalledKernel[] kernels;
        lock (_gate)
        {
            if (_disposed || !Options.Enabled || (_attached is not null && !ReferenceEquals(_attached, session)))
            {
                return false;
            }

            _attached = session;
            kernels = HookedKernels(session, pauseScope);
        }

        foreach (var kernel in kernels)
        {
            kernel.SetDebugHook(new PluginKernelDebugHook(this, kernel));
        }

        return true;
    }

    public void RegisterKernel(InstalledKernel kernel)
    {
        PluginKernelDebugHook? hook = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _kernels.Add(kernel);
            if (_attached is not null && ShouldHook(kernel, _attached, _attached.PauseScope))
            {
                hook = new PluginKernelDebugHook(this, kernel);
            }
        }

        kernel.SetDebugHook(hook);
        kernel.RegisterRevocationCallback(UnregisterKernel);
    }

    public ValueTask WaitForDispatchAsync(InstalledKernel kernel, CancellationToken cancellationToken)
    {
        Task? wait;
        lock (_gate)
        {
            wait = _pause is not null && Applies(_pause, kernel, runId: null)
                ? _pause.Resume.Task
                : null;
        }

        return wait is null ? default : new ValueTask(wait.WaitAsync(cancellationToken));
    }

    public async ValueTask OnCheckpointAsync(
        InstalledKernel kernel,
        SandboxDebugCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var target = FindCheckpointTarget(kernel, checkpoint.RunId);

        if (target.Wait is not null)
        {
            await target.Wait.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (target.Session is null)
        {
            return;
        }

        var reason = await PluginDebugBreakpointEvaluator.StopReasonAsync(
                target.Session,
                kernel.Manifest.PluginId,
                checkpoint,
                cancellationToken)
            .ConfigureAwait(false);
        if (reason is null)
        {
            return;
        }

        var pause = AcquirePause(target.Session, kernel, checkpoint.RunId);
        if (pause.Wait is null)
        {
            return;
        }

        if (pause.Created)
        {
            await PluginDebugStopPublisher.PublishAsync(target.Session, kernel, checkpoint, reason, cancellationToken)
                .ConfigureAwait(false);
        }

        await pause.Wait.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private CheckpointTarget FindCheckpointTarget(InstalledKernel kernel, SandboxRunId runId)
    {
        lock (_gate)
        {
            var wait = _pause is not null && Applies(_pause, kernel, runId)
                ? _pause.Resume.Task
                : null;
            var session = wait is null && OwnsKernel(_attached, kernel) ? _attached : null;
            return new CheckpointTarget(session, wait);
        }
    }

    private PauseAcquisition AcquirePause(
        PluginDebugSession candidate,
        InstalledKernel kernel,
        SandboxRunId runId)
    {
        lock (_gate)
        {
            if (_pause is not null)
            {
                var wait = Applies(_pause, kernel, runId) ? _pause.Resume.Task : null;
                return new PauseAcquisition(wait, Created: false);
            }

            if (!ReferenceEquals(_attached, candidate))
            {
                return default;
            }

            var pause = new DebugPause(candidate, runId, candidate.PauseScope);
            _pause = pause;
            return new PauseAcquisition(pause.Resume.Task, Created: true);
        }
    }

    public bool Resume(PluginDebugSession session, string runId)
    {
        TaskCompletionSource? resume = null;
        lock (_gate)
        {
            if (_pause is not null &&
                ReferenceEquals(_pause.Session, session) &&
                string.Equals(_pause.RunId.ToString(), runId, StringComparison.Ordinal))
            {
                resume = _pause.Resume;
                _pause = null;
            }
        }

        if (resume is null)
        {
            return false;
        }

        session.ExecutionState.RemoveStopped(runId);
        resume.TrySetResult();
        return true;
    }

    public bool IsBreakpointVerified(
        PluginDebugSession session,
        string pluginId,
        SandboxNodeId nodeId)
    {
        InstalledKernel? kernel;
        lock (_gate)
        {
            kernel = _kernels.FirstOrDefault(candidate =>
                ReferenceEquals(candidate.OwnerId, session.Owner) &&
                string.Equals(candidate.Manifest.PluginId, pluginId, StringComparison.Ordinal));
        }

        return kernel is not null && SandboxNodeMap.Create(kernel.Package.Module).Nodes.Any(node => node.Id == nodeId);
    }

    public void Detach(PluginDebugSession session)
    {
        InstalledKernel[] kernels;
        TaskCompletionSource? resume;
        lock (_gate)
        {
            if (!ReferenceEquals(_attached, session))
            {
                return;
            }

            _attached = null;
            kernels = _kernels.ToArray();
            resume = _pause?.Resume;
            _pause = null;
        }

        session.ExecutionState.ClearStops();
        foreach (var kernel in kernels)
        {
            kernel.SetDebugHook(null);
        }

        resume?.TrySetResult();
    }

    public void Dispose()
    {
        PluginDebugSession? attached;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            attached = _attached;
        }

        if (attached is not null)
        {
            Detach(attached);
            attached.DetachFromServer();
        }
    }

    private InstalledKernel[] HookedKernels(PluginDebugSession session, KernelDebugPauseScope pauseScope)
        => _kernels.Where(kernel => ShouldHook(kernel, session, pauseScope)).ToArray();

    private static bool ShouldHook(
        InstalledKernel kernel,
        PluginDebugSession session,
        KernelDebugPauseScope pauseScope)
        => pauseScope == KernelDebugPauseScope.Server || ReferenceEquals(kernel.OwnerId, session.Owner);

    private static bool OwnsKernel(PluginDebugSession? session, InstalledKernel kernel)
        => session is not null && ReferenceEquals(kernel.OwnerId, session.Owner);

    private static bool Applies(DebugPause pause, InstalledKernel kernel, SandboxRunId? runId)
        => pause.Scope switch
        {
            KernelDebugPauseScope.Server => true,
            KernelDebugPauseScope.PluginSession => ReferenceEquals(kernel.OwnerId, pause.Session.Owner),
            KernelDebugPauseScope.Execution => Equals(runId, pause.RunId),
            _ => false
        };

    private void UnregisterKernel(InstalledKernel kernel)
    {
        lock (_gate)
        {
            _kernels.Remove(kernel);
        }
    }

    private readonly record struct CheckpointTarget(PluginDebugSession? Session, Task? Wait);

    private readonly record struct PauseAcquisition(Task? Wait, bool Created);

    private sealed record DebugPause(
        PluginDebugSession Session,
        SandboxRunId RunId,
        KernelDebugPauseScope Scope)
    {
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
