using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Generated;

internal sealed class CompiledCacheCancellationFixture : IDisposable
{
    private readonly Func<CancellationToken, Task<CompiledArtifact>> _request;
    private readonly Action? _dispose;
    private readonly IControlledWork _work;

    internal CompiledCacheCancellationFixture(string kind, ExecutionPlan plan, CompiledArtifact artifact)
    {
        switch (kind)
        {
            case "ArtifactDelegate":
            case "ArtifactCompiler":
                var compilation = new ControlledWork<CompiledArtifact>(artifact);
                var artifacts = new CompiledArtifactExecutionCache();
                var compiler = new ControlledCompiler(compilation);
                _work = compilation;
                _request = kind == "ArtifactDelegate"
                    ? ct => artifacts.GetAsync(plan, "main", compilation.Start, ct).AsTask()
                    : ct => artifacts.GetAsync(plan, "main", compiler, ct).AsTask();
                break;
            case "ExecutableDelegate":
                var execution = new ControlledWork<CompiledExecutable>(new CompiledExecutable(artifact, "Miss"));
                var executables = new CompiledExecutableExecutionCache();
                _work = execution;
                _request = async ct => (await executables.GetAsync(plan, "main", execution.Start, ct)).Artifact;
                _dispose = executables.Dispose;
                break;
            case "ExecutableCache":
            case "Materialized":
                var materialization = new ControlledWork<MaterializedCompiledArtifact>(new MaterializedCompiledArtifact(artifact, null));
                var materialized = new CompiledExecutableCache((_, _, _, ct) => materialization.Start(ct));
                var outer = new CompiledExecutableExecutionCache();
                _work = materialization;
                _request = kind == "ExecutableCache"
                    ? async ct => (await outer.GetAsync(plan, "main", materialized, artifact, ct)).Artifact
                    : async ct => (await materialized.GetAsync(artifact, plan, "main", ct)).Artifact;
                _dispose = () =>
                {
                    outer.Dispose();
                    materialized.Dispose();
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    internal int Calls => _work.Calls;
    internal Task Work => _work.Task;
    internal Task<CompiledArtifact> RequestAsync(CancellationToken cancellationToken) => _request(cancellationToken);
    internal void Fail(Exception error) => _work.Fail(error);
    internal void Complete() => _work.Complete();
    internal void Reset() => _work.Reset();
    public void Dispose() => _dispose?.Invoke();

    internal static async Task<(ExecutionPlan Plan, CompiledArtifact Artifact)> CreateInputsAsync()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        return (plan, CompiledArtifactTestFactory.LoadedAssembly(
            plan, CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123)));
    }

    private interface IControlledWork
    {
        int Calls { get; }
        Task Task { get; }
        void Fail(Exception error);
        void Complete();
        void Reset();
    }

    private sealed class ControlledWork<T>(T result) : IControlledWork
    {
        private TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public Task Task => _completion.Task;

        internal ValueTask<T> Start(CancellationToken cancellationToken)
        {
            Assert.False(cancellationToken.CanBeCanceled);
            Calls++;
            return new ValueTask<T>(_completion.Task);
        }

        public void Fail(Exception error) => _completion.SetException(error);
        public void Complete() => _completion.SetResult(result);
        public void Reset() => _completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ControlledCompiler(ControlledWork<CompiledArtifact> work) : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(ExecutionPlan plan, CompileOptions options, CancellationToken cancellationToken) =>
            work.Start(cancellationToken);
    }
}
