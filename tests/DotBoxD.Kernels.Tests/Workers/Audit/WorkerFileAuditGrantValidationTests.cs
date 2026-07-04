using System.Text;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerFileAuditGrantValidationTests
{
    [Fact]
    public async Task Worker_rejects_file_write_audit_that_violates_file_grant_constraints()
    {
        using var temp = TempDirectory.Create();
        using var host = FileHost(new ForgedFileWriteWorker("blocked.txt", "ok"));
        var module = await host.ImportJsonAsync(FileWriteJson("allowed.json", "ok"));
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .Grant(
                "file.write",
                new Dictionary<string, string>
                {
                    ["root"] = temp.Path,
                    ["allowCreate"] = "true",
                    ["allowOverwrite"] = "false",
                    ["maxBytesPerRun"] = "1024",
                    ["allowedExtensions"] = ".json"
                },
                SandboxEffect.FileWrite | SandboxEffect.Audit,
                limits => limits with { MaxFileBytesWritten = 1024 })
            .WithFuel(1_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
        Assert.False(File.Exists(Path.Combine(temp.Path, "blocked.txt")));
    }

    private static SandboxHost FileHost(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddFileBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "worker-file-audit-grant-validation",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write", "reason": "test file write" }],
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
                      { "path": "{{path}}" },
                      { "string": "{{text}}" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private sealed class ForgedFileWriteWorker(string auditPath, string text) : ISandboxWorkerClient
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
            var byteCount = Encoding.UTF8.GetByteCount(text);
            var usage = Usage(plan, byteCount);
            var audit = new InMemoryAuditSink();
            var timestamp = DateTimeOffset.UtcNow;

            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                timestamp,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan, byteCount)));
            audit.Write(new SandboxAuditEvent(
                runId,
                "BindingCall",
                timestamp,
                true,
                BindingId: "file.writeText",
                CapabilityId: "file.write",
                Effect: SandboxEffect.FileWrite,
                ResourceId: $"sandbox://file.write/{auditPath}",
                Bytes: byteCount,
                Fields: BindingAuditFields.Create(
                    "file",
                    timestamp,
                    plan.ModuleHash,
                    plan.PolicyHash,
                    plan.Policy.Deterministic,
                    bytesWritten: byteCount)));

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

        private static SandboxResourceUsage Usage(ExecutionPlan plan, long fileBytesWritten)
            => new(
                FuelUsed: 50,
                MaxFuel: plan.Budget.MaxFuel,
                LoopIterations: 0,
                AllocatedBytes: 0,
                HostCalls: 1,
                FileBytesRead: 0,
                FileBytesWritten: fileBytesWritten,
                NetworkBytesRead: 0,
                NetworkBytesWritten: 0,
                LogEvents: 0,
                CollectionElements: 0,
                StringBytes: 0);

        private static Dictionary<string, string> SummaryFields(ExecutionPlan plan, long fileBytesWritten)
        {
            var fields = new Dictionary<string, string>(
                RunSummaryAuditFields.Create(plan, new ResourceMeter(plan.Budget), ExecutionMode.Interpreted, "None"),
                StringComparer.Ordinal)
            {
                ["fuelUsed"] = "50",
                ["hostCalls"] = "1",
                ["fileBytesWritten"] = fileBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            return fields;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
