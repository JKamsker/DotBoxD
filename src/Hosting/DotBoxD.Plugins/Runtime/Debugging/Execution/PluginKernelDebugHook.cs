using DotBoxD.Kernels.Debugging;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginKernelDebugHook(
    PluginDebugCoordinator coordinator,
    InstalledKernel kernel) : ISandboxExecutionDebugHook
{
    public ValueTask OnCheckpointAsync(
        SandboxDebugCheckpoint checkpoint,
        CancellationToken cancellationToken)
        => coordinator.OnCheckpointAsync(kernel, checkpoint, cancellationToken);
}
