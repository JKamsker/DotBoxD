using DotBoxD.Kernels.Debugging;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Debugging;

internal static class PluginDebugStopPublisher
{
    public static async ValueTask PublishAsync(
        PluginDebugSession session,
        InstalledKernel kernel,
        SandboxDebugCheckpoint checkpoint,
        string reason,
        CancellationToken cancellationToken)
    {
        session.ExecutionState.RecordStopped(kernel.Manifest.PluginId, checkpoint, reason);
        await session.PublishEventAsync(
                "stopped",
                new
                {
                    runId = checkpoint.RunId.ToString(),
                    pluginId = kernel.Manifest.PluginId,
                    nodeId = checkpoint.Node.Id.Value,
                    checkpointKind = checkpoint.Kind.ToString(),
                    reason,
                    error = checkpoint.Error is null
                        ? null
                        : new
                        {
                            code = checkpoint.Error.Code.ToString(),
                            message = checkpoint.Error.SafeMessage
                        }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
