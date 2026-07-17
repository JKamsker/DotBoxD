using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Compiler.Emitters;

namespace DotBoxD.Hosting.Execution.Compiled;

internal sealed class CompiledExecutionProvider(ISandboxCompiler? compiler) : IDisposable
{
    private readonly CompiledArtifactExecutionCache _artifacts = new();
    private readonly CompiledExecutableExecutionCache _executables = new();
    private readonly CompiledExecutableCache _materialized = new();

    public bool IsAvailable => compiler is not null &&
        (compiler is not ReflectionEmitSandboxCompiler || RuntimeFeature.IsDynamicCodeSupported);

    public ValueTask<CompiledExecutable> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
        => compiler is ReflectionEmitSandboxCompiler { UsesPersistentCache: false }
            ? GetCachedReflectionExecutableAsync(plan, entrypoint, cancellationToken)
            : GetCompilerExecutableAsync(plan, entrypoint, cancellationToken);

    public void Dispose() => _materialized.Dispose();

    private async ValueTask<CompiledExecutable> GetCompilerExecutableAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        var artifact = await compiler!.CompileAsync(plan, new CompileOptions(entrypoint), cancellationToken)
            .ConfigureAwait(false);
        return await _materialized.GetAsync(artifact, plan, entrypoint, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<CompiledExecutable> GetCachedReflectionExecutableAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        var artifact = await _artifacts.GetAsync(
                plan,
                entrypoint,
                compiler!,
                cancellationToken)
            .ConfigureAwait(false);
        return await _executables.GetAsync(
                plan,
                entrypoint,
                _materialized,
                artifact,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
