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
        PluginDebugSession? candidate;
        Task? wait;
        lock (_gate)
        {
            wait = _pause is not null && Applies(_pause, kernel, checkpoint.RunId)
                ? _pause.Resume.Task
                : null;
            candidate = wait is null && OwnsKernel(_attached, kernel) ? _attached : null;
        }

        if (wait is not null)
        {
            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (candidate is null ||
            !candidate.ExecutionState.ShouldStop(kernel.Manifest.PluginId, checkpoint, out var reason))
        {
            return;
        }

        var created = false;
        lock (_gate)
        {
            if (_pause is not null)
            {
                wait = Applies(_pause, kernel, checkpoint.RunId) ? _pause.Resume.Task : null;
            }
            else if (ReferenceEquals(_attached, candidate))
            {
                var pause = new DebugPause(candidate, checkpoint.RunId, candidate.PauseScope);
                _pause = pause;
                wait = pause.Resume.Task;
                created = true;
            }
        }

        if (wait is null)
        {
            return;
        }

        if (created)
        {
            candidate.ExecutionState.RecordStopped(checkpoint);
            await candidate.PublishEventAsync(
                    "stopped",
                    new
                    {
                        runId = checkpoint.RunId.ToString(),
                        pluginId = kernel.Manifest.PluginId,
                        nodeId = checkpoint.Node.Id.Value,
                        checkpointKind = checkpoint.Kind.ToString(),
                        reason
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private sealed record DebugPause(
        PluginDebugSession Session,
        SandboxRunId RunId,
        KernelDebugPauseScope Scope)
    {
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
