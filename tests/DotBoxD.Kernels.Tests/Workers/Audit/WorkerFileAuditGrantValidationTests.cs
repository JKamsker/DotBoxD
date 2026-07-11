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
        var worker = new ForgedFileWriteWorker("blocked.txt", "ok", ".txt", "create");
        using var host = FileHost(worker);
        var module = await host.ImportJsonAsync(FileWriteJson("allowed.json", "ok"));
        var policy = FileWritePolicy(temp.Path, allowCreate: true, allowOverwrite: false, allowedExtensions: ".json");
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
        Assert.Contains(
            worker.Result!.AuditEvents,
            e => e is
            {
                Kind: "BindingCall",
                BindingId: "file.writeText",
                CapabilityId: "file.write",
                Success: true,
                ResourceId: "sandbox://file.write/blocked.txt"
            });
    }

    [Fact]
    public async Task Worker_accepts_redacted_file_write_audit_with_extension_evidence()
    {
        using var temp = TempDirectory.Create();
        var worker = new ForgedFileWriteWorker("[redacted]", "ok", ".txt", "create");
        using var host = FileHost(worker);
        var module = await host.ImportJsonAsync(FileWriteJson("token.txt", "ok"));
        var policy = FileWritePolicy(temp.Path, allowCreate: true, allowOverwrite: false, allowedExtensions: ".txt");
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_rejects_file_write_audit_that_forges_disallowed_create()
    {
        using var temp = TempDirectory.Create();
        var worker = new ForgedFileWriteWorker("created.txt", "ok", ".txt", "create");
        using var host = FileHost(worker);
        var module = await host.ImportJsonAsync(FileWriteJson("created.txt", "ok"));
        var policy = FileWritePolicy(temp.Path, allowCreate: false, allowOverwrite: true, allowedExtensions: ".txt");
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_rejects_file_write_audit_that_forges_disallowed_overwrite()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(System.IO.Path.Combine(temp.Path, "existing.txt"), "before");
        var worker = new ForgedFileWriteWorker("existing.txt", "ok", ".txt", "overwrite");
        using var host = FileHost(worker);
        var module = await host.ImportJsonAsync(FileWriteJson("existing.txt", "ok"));
        var policy = FileWritePolicy(temp.Path, allowCreate: true, allowOverwrite: false, allowedExtensions: ".txt");
        var plan = await host.PrepareAsync(module, policy);

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

    private static SandboxPolicy FileWritePolicy(
        string root,
        bool allowCreate,
        bool allowOverwrite,
        string allowedExtensions)
        => SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .Grant(
                "file.write",
                new Dictionary<string, string>
                {
                    ["root"] = root,
                    ["allowCreate"] = allowCreate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["allowOverwrite"] = allowOverwrite.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["maxBytesPerRun"] = "1024",
                    ["allowedExtensions"] = allowedExtensions
                },
                SandboxEffect.FileWrite | SandboxEffect.Audit,
                limits => limits with { MaxFileBytesWritten = 1024 })
            .WithFuel(1_000)
            .Build();

    private sealed class ForgedFileWriteWorker(
        string auditPath,
        string text,
        string? pathExtension = null,
        string? writeDisposition = null) : ISandboxWorkerClient
    {
        public SandboxExecutionResult? Result { get; private set; }

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
                Fields: BindingFields(plan, timestamp, byteCount, pathExtension, writeDisposition)));

            Result = new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.Unit,
                ResourceUsage = usage,
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            };
            return ValueTask.FromResult(Result);
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

        private static Dictionary<string, string> BindingFields(
            ExecutionPlan plan,
            DateTimeOffset timestamp,
            long byteCount,
            string? pathExtension,
            string? writeDisposition)
        {
            var fields = new Dictionary<string, string>(
                BindingAuditFields.Create(
                    "file",
                    timestamp,
                    plan.ModuleHash,
                    plan.PolicyHash,
                    plan.Policy.Deterministic,
                    bytesWritten: byteCount),
                StringComparer.Ordinal);
            if (pathExtension is not null)
            {
                fields["pathExtension"] = pathExtension;
            }

            if (writeDisposition is not null)
            {
                fields["writeDisposition"] = writeDisposition;
            }

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
