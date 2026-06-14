namespace DotBoxD.Kernels.Interpreter;

using DotBoxD.Kernels;

public interface ISandboxInterpreter
{
    ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken);
}
