using DotBoxD.Hosting;
using DotBoxD.Hosting.Http.Hosting;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerHttpAuditGrantValidationTests
{
    [Fact]
    public async Task Worker_http_audit_resource_must_match_active_http_grant()
    {
        var worker = new ForgedHttpWorker(
            resourceId: "https://evil.example.com/config",
            networkBytesRead: 128,
            networkBytesWritten: 64);
        using var host = HttpHost(worker);
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
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
    }

    [Fact]
    public async Task Worker_http_audit_resource_allows_query_and_fragment_when_grant_matches()
    {
        var worker = new ForgedHttpWorker(
            resourceId: "https://api.example.com/config?env=prod#v1",
            networkBytesRead: 128,
            networkBytesWritten: 64);
        using var host = HttpHost(worker);
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config?env=prod#v1"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
    }

    [Fact]
    public async Task Worker_http_audit_must_respect_active_http_grant_timeout()
    {
        var worker = new ForgedHttpWorker(
            resourceId: "https://api.example.com/config",
            networkBytesRead: 128,
            networkBytesWritten: 64,
            durationMs: "50.000");
        using var host = HttpHost(worker);
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, timeout: TimeSpan.FromMilliseconds(1))
            .WithFuel(5_000)
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
    }

    private static SandboxHost HttpHost(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddNetworkBindings(FakeInvoker("ok"));
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private sealed class ForgedHttpWorker(
        string resourceId,
        long networkBytesRead,
        long networkBytesWritten,
        string? durationMs = null) : ISandboxWorkerClient
    {
        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = SandboxValue.FromString("forged");
            var budget = new ResourceMeter(plan.Budget);
            budget.ChargeHostCall("net.http.get");
            budget.ChargeFuel(75);
            budget.ChargeNetworkRead(networkBytesRead);
            budget.ChargeNetworkWrite(networkBytesWritten);
            budget.ChargeValue(value);

            var runId = options.RunId ?? SandboxRunId.New();
            var startedAt = DateTimeOffset.UtcNow;
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                startedAt,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: RunSummaryAuditFields.Create(plan, budget, ExecutionMode.Interpreted, "None")));
            audit.Write(new SandboxAuditEvent(
                runId,
                "BindingCall",
                startedAt,
                true,
                BindingId: "net.http.get",
                CapabilityId: "net.http.get",
                Effect: SandboxEffect.Network,
                ResourceId: resourceId,
                Bytes: networkBytesRead,
                Fields: BindingFields(
                    plan,
                    "network",
                    startedAt,
                    networkBytesRead,
                    networkBytesWritten,
                    durationMs)));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = value,
                ResourceUsage = budget.Snapshot(),
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }

        private static Dictionary<string, string> BindingFields(
            ExecutionPlan plan,
            string resourceKind,
            DateTimeOffset startedAt,
            long bytesRead,
            long bytesWritten,
            string? durationMs)
        {
            var fields = new Dictionary<string, string>(
                BindingAuditFields.Create(
                    resourceKind,
                    startedAt,
                    plan.ModuleHash,
                    plan.PolicyHash,
                    plan.Policy.Deterministic,
                    bytesRead: bytesRead,
                    bytesWritten: bytesWritten),
                StringComparer.Ordinal);
            if (durationMs is not null)
            {
                fields["durationMs"] = durationMs;
            }

            return fields;
        }
    }
}
