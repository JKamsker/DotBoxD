using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Core.CacheLifetime;

internal static class CompiledProviderLifetimeFixture
{
    internal static ISandboxCompiler Compiler(bool custom)
    {
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());
        return custom ? new ForwardingCompiler(compiler) : compiler;
    }

    internal static SandboxHost Host(ISandboxCompiler compiler)
        => SandboxHost.Create(builder => builder.UseInterpreter().UseCompilerIfAvailable(compiler));

    internal static ExecutionPlan Prepare(SandboxHost host, string id = "compiler-lifetime")
    {
        var module = host.ImportJsonAsync(SandboxTestHost.PureScoreJson(id)).GetAwaiter().GetResult();
        return host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithFuel(1_000).WithWallTime(TimeSpan.FromSeconds(10)).Build()).GetAwaiter().GetResult();
    }

    internal static CompiledArtifact Compile(ISandboxCompiler compiler, ExecutionPlan plan)
        => compiler.CompileAsync(plan, new CompileOptions("main"), CancellationToken.None).GetAwaiter().GetResult();

    internal static void Execute(SandboxHost host, ExecutionPlan plan)
    {
        var result = host.ExecuteAsync(plan, "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false })
            .GetAwaiter().GetResult();
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (SandboxHost Host, WeakReference[] References) CreateDisposed(bool custom, int count)
    {
        var compiler = Compiler(custom);
        var host = Host(compiler);
        var references = new List<WeakReference> { new(compiler) };
        for (var i = 0; i < count; i++)
        {
            var plan = Prepare(host, $"compiler-retention-{i}");
            references.Add(new WeakReference(Compile(compiler, plan)));
            Execute(host, plan);
        }
        host.Dispose();
        AssertCallerCompilerOwned(compiler);
        return (host, references.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (SandboxHost Host, ExecutionPlan Plan, WeakReference[] References) CreateLive(bool custom)
    {
        var compiler = Compiler(custom);
        var host = Host(compiler);
        var plan = Prepare(host);
        var artifact = Compile(compiler, plan);
        Execute(host, plan);
        return (host, plan, [new WeakReference(compiler), new WeakReference(artifact)]);
    }

    internal static void AssertCallerCompilerOwned(ISandboxCompiler compiler)
    {
        if (compiler is ForwardingCompiler forwarding)
        {
            Assert.Equal(0, forwarding.DisposeCalls);
        }
    }

    internal static async Task AssertCollectedAsync(WeakReference[] references)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            Collect();
            if (references.All(reference => !reference.IsAlive))
            {
                return;
            }
            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));
        Assert.All(references, reference => Assert.False(reference.IsAlive,
            "Disposed compilation owners must release their compiler and cached artifacts."));
    }

    internal static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    internal static ValueTask<CompiledExecutable> Request(
        CompiledExecutionProvider provider, ExecutionPlan plan, bool publish)
        => publish
            ? provider.GetAndPublishCompletedExecutableAsync(plan, "main", CancellationToken.None)
            : provider.GetAsync(plan, "main", CancellationToken.None);

    internal sealed class PendingCompiler : ISandboxCompiler
    {
        internal TaskCompletionSource<CompiledArtifact> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan, CompileOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            return new ValueTask<CompiledArtifact>(Completion.Task);
        }
    }

    private sealed class ForwardingCompiler(ISandboxCompiler inner) : ISandboxCompiler, IDisposable
    {
        internal int DisposeCalls { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan, CompileOptions options, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(DisposeCalls != 0, this);
            return inner.CompileAsync(plan, options, cancellationToken);
        }

        public void Dispose() => DisposeCalls++;
    }
}
