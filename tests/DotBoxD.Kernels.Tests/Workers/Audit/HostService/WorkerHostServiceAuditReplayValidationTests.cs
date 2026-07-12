using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers.Audit;

public sealed class WorkerHostServiceAuditReplayValidationTests
{
    [Fact]
    public async Task Worker_rejects_custom_host_service_binding_audit_without_replay_evidence()
    {
        var service = new ProbeWorld();
        var worker = new ForgedHostServiceWorker("forged-target");
        using var host = Host(worker, service);
        var module = await host.ImportJsonAsync(HostServiceJson("legit-target"));
        var plan = await host.PrepareAsync(module, Policy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
        Assert.Equal(0, service.Calls);
    }

    private static SandboxHost Host(ISandboxWorkerClient worker, IProbeWorld service)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBindingsFrom(service);
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .Grant("probe.read.value", new { }, SandboxEffect.HostStateRead)
            .WithFuel(1_000)
            .Build();

    private static string HostServiceJson(string target)
        => $$"""
        {
          "id": "worker-host-service-audit-replay-validation",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "probe.read.value", "reason": "read host probe values" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "host.probe.getValue",
                    "args": [{ "string": "{{target}}" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private interface IProbeWorld
    {
        [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
        [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int GetValue(string id);
    }

    private sealed class ProbeWorld : IProbeWorld
    {
        public int Calls { get; private set; }

        public int GetValue(string id)
        {
            Calls++;
            return id == "legit-target" ? 42 : 0;
        }
    }

    private sealed class ForgedHostServiceWorker(string auditedTarget) : ISandboxWorkerClient
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
            var usage = Usage(plan);
            var audit = new InMemoryAuditSink();
            var timestamp = DateTimeOffset.UtcNow;

            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                timestamp,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan)));
            audit.Write(new SandboxAuditEvent(
                runId,
                BindingAuditKinds.BindingCall,
                timestamp,
                true,
                BindingId: "host.probe.getValue",
                CapabilityId: "probe.read.value",
                Effect: SandboxEffect.HostStateRead,
                ResourceId: $"entity:{auditedTarget}",
                Fields: BindingFields(plan)));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.FromInt32(42),
                ResourceUsage = usage,
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }

        private static SandboxResourceUsage Usage(ExecutionPlan plan)
            => new(
                FuelUsed: 50,
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
            fields["fuelUsed"] = "50";
            fields["hostCalls"] = "1";
            return fields;
        }

        private static Dictionary<string, string> BindingFields(ExecutionPlan plan)
            => new(StringComparer.Ordinal)
            {
                ["resourceKind"] = "host-service",
                ["durationMs"] = "0",
                ["moduleHash"] = plan.ModuleHash,
                ["policyHash"] = plan.PolicyHash
            };
    }
}
