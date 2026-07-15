using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.AttemptResult;

internal static class CompiledAttemptPathTestSupport
{
    public static SandboxHost HostWithCompiler(ISandboxCompiler compiler)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });

    public static async ValueTask<ExecutionPlan> PreparePurePlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson("compiled-attempt-path"));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    public static async ValueTask<ExecutionPlan> PrepareArithmeticFailurePlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync("""
        {
          "id": "compiled-attempt-runtime-failure",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "op": "div", "left": { "i32": 1 }, "right": { "i32": 0 } }
                }
              ]
            }
          ]
        }
        """);
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    public static SandboxValue PureInput()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

    public static SandboxExecutionOptions CompiledOptions(
        SandboxRunId runId,
        bool allowFallback = false,
        bool suppressSuccessfulSummary = false)
        => new()
        {
            Mode = ExecutionMode.Compiled,
            AllowFallbackToInterpreter = allowFallback,
            RunId = runId,
            SuppressSuccessfulRunSummaryAudit = suppressSuccessfulSummary
        };
}

internal sealed class SandboxErrorCompiler(SandboxErrorCode code) : ISandboxCompiler
{
    public ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
        => throw new SandboxRuntimeException(new SandboxError(code, "compiler rejected the plan"));
}

internal sealed class CancelledCompiler : ISandboxCompiler
{
    public ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
        => throw new OperationCanceledException("compiler cancelled");
}

internal sealed class HostFailureCompiler : ISandboxCompiler
{
    public ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("compiler failed unexpectedly");
}

internal sealed class CompilerGate
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WaitUntilEnteredAsync()
        => await _entered.Task.WaitAsync(WaitTimeout);

    public async ValueTask WaitForReleaseAsync(CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Release() => _release.TrySetResult();
}

internal sealed class GatedSuccessCompiler : ISandboxCompiler
{
    private readonly ReflectionEmitSandboxCompiler _inner = new(new GeneratedAssemblyVerifier());

    public CompilerGate Gate { get; } = new();

    public async ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
    {
        await Gate.WaitForReleaseAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.CompileAsync(plan, options, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GatedSandboxErrorCompiler(SandboxErrorCode code) : ISandboxCompiler
{
    public CompilerGate Gate { get; } = new();

    public async ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
    {
        await Gate.WaitForReleaseAsync(cancellationToken).ConfigureAwait(false);
        throw new SandboxRuntimeException(new SandboxError(code, "delayed compiler rejection"));
    }
}
