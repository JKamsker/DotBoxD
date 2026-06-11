using SafeIR.Compiler;
using SafeIR.Hosting;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class CompiledArtifactGuardTests
{
    [Fact]
    public async Task Compiled_artifact_manifest_mismatch_is_rejected_before_delegate_runs()
    {
        var compiler = new TamperedCompiler(artifact => artifact with {
            Manifest = artifact.Manifest with { PlanHash = "other-plan" }
        });
        var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.False(compiler.DelegateExecuted);
    }

    [Fact]
    public async Task Compiled_artifact_runtime_form_mismatch_is_rejected_before_delegate_runs()
    {
        var compiler = new TamperedCompiler(artifact => artifact with {
            RuntimeForm = CompiledRuntimeFormKind.LoadedAssembly
        });
        var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.False(compiler.DelegateExecuted);
    }

    private static SandboxHost HostWithCompiler(ISandboxCompiler compiler)
        => SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });

    private static async Task<ExecutionPlan> PreparePurePlanAsync(SandboxHost host)
    {
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        SandboxHost host,
        ExecutionPlan plan)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static ArtifactManifest Manifest(ExecutionPlan plan, string artifactHash)
        => new(
            1,
            "tampered-cache-key",
            plan.ModuleHash,
            plan.PlanHash,
            plan.PolicyHash,
            plan.BindingManifestHash,
            "tampered-runtime",
            "tampered-compiler",
            "tampered-verifier",
            "1.0.0",
            "net10.0",
            ["dynamic-method"],
            artifactHash,
            DateTimeOffset.UtcNow);

    private sealed class TamperedCompiler(Func<CompiledArtifact, CompiledArtifact> tamper) : ISandboxCompiler
    {
        public bool DelegateExecuted { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            var artifact = new CompiledArtifact(
                [],
                "tampered-artifact",
                Manifest(plan, "tampered-artifact"),
                new VerificationResult(true, [], "tampered-artifact", "tampered-verifier", DateTimeOffset.UtcNow),
                (_, _) => {
                    DelegateExecuted = true;
                    return SandboxValue.FromInt32(123);
                },
                CompiledRuntimeFormKind.DynamicMethod);
            return ValueTask.FromResult(tamper(artifact));
        }
    }
}
