using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Cancellation;

public sealed class SandboxInterpreterPreCanceledMissingEntrypointTests
{
    [Fact]
    public async Task ExecuteAsync_reports_cancellation_before_validating_a_missing_entrypoint()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new SandboxInterpreter().ExecuteAsync(
            plan,
            "missing",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions(),
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(0, result.ResourceUsage.FuelUsed);
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "BindingCall");
        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, summary.ErrorCode);
    }
}
