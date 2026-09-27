using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Compiler.Emitters;

namespace DotBoxD.Hosting.Execution.Compiled;

internal sealed class CompiledExecutionProvider : IDisposable
{
    private ISandboxCompiler? _compiler;
    private readonly CompiledArtifactExecutionCache _artifacts = new();
    private readonly CompiledExecutableExecutionCache _executables = new();
    private readonly CompiledExecutableCache _materialized = new();

    public CompiledExecutionProvider(ISandboxCompiler? compiler) => _compiler = compiler;

    public bool IsAvailable
    {
        get
        {
            var compiler = Volatile.Read(ref _compiler);
            return compiler is not null &&
                (compiler is not ReflectionEmitSandboxCompiler || RuntimeFeature.IsDynamicCodeSupported);
        }
    }

    public ValueTask<CompiledExecutable> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        var compiler = ReadCompiler();
        return compiler is ReflectionEmitSandboxCompiler { UsesPersistentCache: false }
            ? GetCachedReflectionExecutableAsync(plan, entrypoint, cancellationToken)
            : GetCompilerExecutableAsync(plan, entrypoint, cancellationToken);
    }

    internal ValueTask<CompiledExecutable> GetAndPublishCompletedExecutableAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        var compiler = ReadCompiler();
        if (compiler is not
            ReflectionEmitSandboxCompiler { UsesPersistentCache: false } reflectionCompiler)
        {
            return GetCompilerExecutableAsync(plan, entrypoint, cancellationToken);
        }

        var hotState = _executables.GetOrCreateHotEntry();
        return GetAndPublishCachedReflectionExecutableAsync(
            hotState,
            reflectionCompiler,
            plan,
            entrypoint,
            cancellationToken);
    }

    public bool TryGetCompletedExecutable(
        ExecutionPlan plan,
        string entrypoint,
        out CompiledExecutable executable)
    {
        if (Volatile.Read(ref _compiler) is not ReflectionEmitSandboxCompiler { UsesPersistentCache: false })
        {
            executable = default;
            return false;
        }

        return _executables.TryGetHot(plan, entrypoint, out executable);
    }

    public bool TryGetCachedCompletedExecutable(
        ExecutionPlan plan,
        string entrypoint,
        out CompiledExecutable executable)
    {
        if (Volatile.Read(ref _compiler) is not ReflectionEmitSandboxCompiler { UsesPersistentCache: false } ||
            _executables.HasHotCapacity)
        {
            executable = default;
            return false;
        }

        return _executables.TryGetCompletedExactWithoutTouch(plan, entrypoint, out executable);
    }

    public void Dispose()
    {
        // Close materialization admission before invalidating executable entries so a racing
        // lookup cannot publish a newly materialized delegate after cache teardown.
        if (!_materialized.TryBeginDispose())
        {
            return;
        }

        var compiler = Interlocked.Exchange(ref _compiler, null);
        try
        {
            _artifacts.Dispose();
            if (compiler is ReflectionEmitSandboxCompiler { UsesPersistentCache: false })
            {
                _executables.Dispose();
            }
        }
        finally
        {
            _materialized.CompleteDispose();
        }
    }

    internal bool HasHotExecutableFor(ExecutionPlan plan, string entrypoint)
        => _executables.HasHot(plan, entrypoint);

    internal bool CanPublishCompletedExecutable
        => Volatile.Read(ref _compiler) is ReflectionEmitSandboxCompiler { UsesPersistentCache: false } &&
           _executables.HasHotCapacity;

    private ISandboxCompiler? ReadCompiler()
    {
        // Snapshot before checking disposal so an admitted invocation cannot read a cleared field.
        var compiler = Volatile.Read(ref _compiler);
        _materialized.ThrowIfDisposed();
        return compiler;
    }

    private async ValueTask<CompiledExecutable> GetCompilerExecutableAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        var compiler = ReadCompiler();
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
        var compiler = ReadCompiler();
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

    private async ValueTask<CompiledExecutable> GetAndPublishCachedReflectionExecutableAsync(
        CompiledExecutableHotEntry hotState,
        ReflectionEmitSandboxCompiler compiler,
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        var artifact = await _artifacts.GetAsync(
                plan,
                entrypoint,
                compiler,
                cancellationToken)
            .ConfigureAwait(false);
        var executable = await _executables.GetAsync(
                plan,
                entrypoint,
                _materialized,
                artifact,
                cancellationToken)
            .ConfigureAwait(false);
        _executables.TryPublishMostRecentCompletedExact(plan, entrypoint, hotState);

        return executable;
    }
}
