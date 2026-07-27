using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

/// <summary>
/// Preserves the public custom-interpreter boundary while executing the same built-in implementation.
/// A trusted built-in optimization must not recognize this wrapper as the built-in interpreter itself.
/// </summary>
internal sealed class ForwardingSandboxInterpreter : ISandboxInterpreter
{
    private readonly SandboxInterpreter _inner = new();

    public ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
        => _inner.ExecuteAsync(plan, entrypoint, input, options, cancellationToken);
}
