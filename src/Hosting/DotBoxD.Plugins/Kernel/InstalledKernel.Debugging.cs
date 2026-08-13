using DotBoxD.Kernels.Debugging;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Plugins.Kernel;

public sealed partial class InstalledKernel
{
    internal PluginDebugCoordinator? DebugCoordinator { get; }

    internal void RegisterWithDebugger() => DebugCoordinator?.RegisterKernel(this);

    internal void SetDebugHook(ISandboxExecutionDebugHook? hook)
    {
        var current = Volatile.Read(ref _executionOptions);
        Volatile.Write(ref _executionOptions, current with { DebugHook = hook });
    }

    private ValueTask WaitForDebugDispatchAsync(CancellationToken cancellationToken)
        => DebugCoordinator?.WaitForDispatchAsync(this, cancellationToken) ?? default;
}
