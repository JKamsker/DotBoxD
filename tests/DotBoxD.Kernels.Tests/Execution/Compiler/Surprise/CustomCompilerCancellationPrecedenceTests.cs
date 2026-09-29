using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Execution;

public sealed class CustomCompilerCancellationPrecedenceTests
{
    [Fact]
    public async Task Compiler_fault_after_cancelling_caller_token_reports_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var compiler = new CancellingFaultingCompiler(cancellation);
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            cancellation.Token);

        Assert.True(compiler.ReceivedCallerToken);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.False(result.ExecutionDispatched);
        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, summary.ErrorCode);
    }

    private sealed class CancellingFaultingCompiler(CancellationTokenSource cancellation) : ISandboxCompiler
    {
        public bool ReceivedCallerToken { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            ReceivedCallerToken = cancellationToken == cancellation.Token;
            cancellation.Cancel();
            return ValueTask.FromException<CompiledArtifact>(new InvalidOperationException("compiler fault"));
        }
    }
}
