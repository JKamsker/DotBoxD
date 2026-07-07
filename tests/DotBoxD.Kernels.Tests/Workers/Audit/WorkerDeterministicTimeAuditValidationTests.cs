using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers.Audit;

public sealed class WorkerDeterministicTimeAuditValidationTests
{
    private static readonly DateTimeOffset LogicalNow =
        new(2026, 7, 5, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Worker_result_with_deterministic_time_value_mismatch_is_rejected()
    {
        using var host = TimeHost(new ForgedDeterministicTimeWorker(LogicalNow));
        var module = await host.ImportJsonAsync(TimeJson());
        var policy = SandboxPolicyBuilder.Create()
            .GrantTimeNow()
            .Deterministic(LogicalNow, randomSeed: 123)
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
    }

    private static SandboxHost TimeHost(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddTimeBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static string TimeJson()
        => """
        {
          "id": "worker-deterministic-time-validation",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "time.now", "reason": "deterministic clock" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I64",
              "body": [
                { "op": "return", "value": { "call": "time.nowUnixMillis", "args": [] } }
              ]
            }
          ]
        }
        """;

    private sealed class ForgedDeterministicTimeWorker(DateTimeOffset logicalNow) : ISandboxWorkerClient
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
            var budget = new ResourceMeter(plan.Budget);
            budget.ChargeHostCall("time.nowUnixMillis");
            budget.ChargeFuel(2);

            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                logicalNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: RunSummaryAuditFields.Create(plan, budget, ExecutionMode.Interpreted, "None")));
            var fields = new Dictionary<string, string>(
                BindingAuditFields.Create(
                    "clock",
                    logicalNow,
                    plan.ModuleHash,
                    plan.PolicyHash,
                    deterministic: true),
                StringComparer.Ordinal)
            {
                [SafeTimeBindingNames.NowUnixMillisAuditField] =
                    logicalNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            audit.Write(new SandboxAuditEvent(
                runId,
                "BindingCall",
                logicalNow,
                true,
                BindingId: "time.nowUnixMillis",
                CapabilityId: "time.now",
                Effect: SandboxEffect.Time,
                ResourceId: "clock:utc",
                Fields: fields));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.FromInt64(logicalNow.ToUnixTimeMilliseconds() + 1),
                ResourceUsage = budget.Snapshot(),
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }
    }
}
