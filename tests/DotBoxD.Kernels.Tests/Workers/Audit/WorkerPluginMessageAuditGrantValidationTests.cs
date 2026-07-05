using System.Globalization;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers.Audit;

public sealed class WorkerPluginMessageAuditGrantValidationTests
{
    [Fact]
    public async Task Worker_rejects_plugin_message_audit_that_violates_host_message_grant()
    {
        var worker = new ForgedPluginMessageWorker("blocked", "toolong");
        using var host = Host(worker);
        var module = await host.ImportJsonAsync(HostMessageJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["allowed"], maxMessageLength: 2)
            .WithFuel(1_000)
            .Build());

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_rejects_plugin_message_audit_when_message_exceeds_reported_length()
    {
        var worker = new ForgedPluginMessageWorker("allowed", "toolong", reportedMessageLength: 2);
        using var host = Host(worker);
        var module = await host.ImportJsonAsync(HostMessageJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["allowed"], maxMessageLength: 2)
            .WithFuel(1_000)
            .Build());

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost Host(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(new InMemoryPluginMessageSink());
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(SandboxHost host, ExecutionPlan plan)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

    private static SandboxResourceUsage Usage(ExecutionPlan plan)
        => new(
            FuelUsed: 5,
            MaxFuel: plan.Budget.MaxFuel,
            LoopIterations: 0,
            AllocatedBytes: 0,
            HostCalls: 1,
            FileBytesRead: 0,
            FileBytesWritten: 0,
            NetworkBytesRead: 0,
            NetworkBytesWritten: 0,
            LogEvents: 0,
            CollectionElements: 0,
            StringBytes: 0);

    private static Dictionary<string, string> SummaryFields(ExecutionPlan plan)
    {
        var fields = new Dictionary<string, string>(
            RunSummaryAuditFields.Create(plan, new ResourceMeter(plan.Budget), ExecutionMode.Interpreted, "None"),
            StringComparer.Ordinal);
        fields["fuelUsed"] = "5";
        fields["hostCalls"] = "1";
        return fields;
    }

    private static Dictionary<string, string> PluginMessageFields(
        ExecutionPlan plan,
        string message,
        int? reportedMessageLength)
        => new(StringComparer.Ordinal)
        {
            ["resourceKind"] = "plugin-message",
            ["durationMs"] = "0",
            ["moduleHash"] = plan.ModuleHash,
            ["policyHash"] = plan.PolicyHash,
            ["messageLength"] = (reportedMessageLength ?? message.Length).ToString(CultureInfo.InvariantCulture)
        };

    private static string HostMessageJson()
        => """
        {
          "id": "worker-plugin-message-audit-grant-validation",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write", "reason": "send host messages" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "host.message.send",
                    "args": [
                      { "string": "allowed" },
                      { "string": "ok" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private sealed class ForgedPluginMessageWorker(
        string TargetId,
        string Message,
        int? reportedMessageLength = null) : ISandboxWorkerClient
    {
        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runId = options.RunId ?? SandboxRunId.New();
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan)));
            audit.Write(new SandboxAuditEvent(
                runId,
                "PluginMessage",
                DateTimeOffset.UtcNow,
                true,
                BindingId: PluginMessageBindings.SendBindingId,
                CapabilityId: PluginMessageBindings.CapabilityId,
                Effect: SandboxEffect.HostStateWrite,
                ResourceId: $"target:{TargetId}",
                Message: Message,
                Fields: PluginMessageFields(plan, Message, reportedMessageLength)));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.Unit,
                ResourceUsage = Usage(plan),
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }
    }
}
