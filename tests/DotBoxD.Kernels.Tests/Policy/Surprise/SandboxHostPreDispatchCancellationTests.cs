using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class SandboxHostPreDispatchCancellationTests
{
    [Fact]
    public async Task Pre_canceled_execution_reports_cancelled_before_revoked_capability_denial()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(PureModuleWithLoggingRequest());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantLogging()
                .WithFuel(1_000)
                .Build());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        host.RevokeCapability("log.write", "audit channel disabled");
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            canceled.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "CapabilityRevoked");
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "PolicyDenied");
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, summary.ErrorCode);
        Assert.Equal("False", summary.Fields!["executionDispatched"]);
    }

    private static string PureModuleWithLoggingRequest()
        => """
        {
          "id": "pre-canceled-log-request",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "log.write", "reason": "audit messages" }
          ],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """;
}
