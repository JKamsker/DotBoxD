using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerAuditByteResourceValidationTests
{
    [Fact]
    public async Task Worker_rejects_file_write_audit_when_usage_underreports_written_bytes()
    {
        var worker = new ForgedFileWriteWorker();
        using var host = FileHost(worker);
        var module = await host.ImportJsonAsync(FileWriteJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantFileWrite(Path.GetTempPath(), 1024, allowCreate: true, allowOverwrite: true)
                .AllowRuntimeAsync()
                .WithFuel(1_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost FileHost(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddFileBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static string FileWriteJson()
        => """
        {
          "id": "worker-audit-byte-validation-file-write",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "file.write", "reason": "test write" }
          ],
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
                    "call": "file.writeText",
                    "args": [
                      { "path": "result.txt" },
                      { "string": "abcde" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private sealed class ForgedFileWriteWorker : ISandboxWorkerClient
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
            var usage = new SandboxResourceUsage(
                FuelUsed: SafeFileBindings.WriteText.CostModel.BaseFuel,
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
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan, usage)));
            audit.Write(new SandboxAuditEvent(
                runId,
                "BindingCall",
                DateTimeOffset.UtcNow,
                true,
                BindingId: "file.writeText",
                CapabilityId: "file.write",
                Effect: SandboxEffect.FileWrite,
                ResourceId: "file:result.txt",
                Bytes: 5,
                Fields: BindingFields(plan)));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.Unit,
                ResourceUsage = usage,
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }

        private static Dictionary<string, string> SummaryFields(
            ExecutionPlan plan,
            SandboxResourceUsage usage)
        {
            var fields = new Dictionary<string, string>(
                RunSummaryAuditFields.Create(
                    plan,
                    new ResourceMeter(plan.Budget),
                    ExecutionMode.Interpreted,
                    "None"),
                StringComparer.Ordinal);
            fields["fuelUsed"] = usage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields["hostCalls"] = usage.HostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return fields;
        }

        private static Dictionary<string, string> BindingFields(ExecutionPlan plan)
            => new(StringComparer.Ordinal)
            {
                ["resourceKind"] = "file",
                ["durationMs"] = "0",
                ["moduleHash"] = plan.ModuleHash,
                ["policyHash"] = plan.PolicyHash,
                ["bytesWritten"] = "5"
            };
    }
}
