namespace DotBoxD.Kernels.Debugging;

/// <summary>
/// Receives interpreter checkpoints. The returned task may remain incomplete to pause execution.
/// </summary>
public interface ISandboxExecutionDebugHook
{
    ValueTask OnCheckpointAsync(
        SandboxDebugCheckpoint checkpoint,
        CancellationToken cancellationToken);
}

