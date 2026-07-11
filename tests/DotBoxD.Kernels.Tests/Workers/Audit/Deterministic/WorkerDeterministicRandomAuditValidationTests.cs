using System.Globalization;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers.Audit.Deterministic;

public sealed class WorkerDeterministicRandomAuditValidationTests
{
    [Fact]
    public async Task Worker_accepts_signed_deterministic_random_audit_fields()
    {
        var logicalNow = DateTimeOffset.Parse("2026-07-05T11:34:28Z", CultureInfo.InvariantCulture);
        var worker = new ValidDeterministicRandomWorker(logicalNow, -10, 10);
        using var host = Host(worker);
        var module = await host.ImportJsonAsync(RandomJson(-10, 10));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantRandom()
                .Deterministic(logicalNow, randomSeed: 123)
                .WithFuel(1_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
    }

    [Fact]
    public async Task Worker_rejects_deterministic_random_result_outside_seed_sequence()
    {
        var logicalNow = DateTimeOffset.Parse("2026-07-05T11:34:28Z", CultureInfo.InvariantCulture);
        var worker = new ForgedDeterministicRandomWorker(logicalNow);
        using var host = Host(worker);
        var module = await host.ImportJsonAsync(RandomJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantRandom()
                .Deterministic(logicalNow, randomSeed: 123)
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

    private static SandboxHost Host(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddRandomBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static SandboxResourceUsage Usage(ExecutionPlan plan)
        => new(
            FuelUsed: 3,
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
        fields["fuelUsed"] = usage.FuelUsed.ToString(CultureInfo.InvariantCulture);
        fields["hostCalls"] = usage.HostCalls.ToString(CultureInfo.InvariantCulture);
        return fields;
    }

    private static Dictionary<string, string> RandomFields(ExecutionPlan plan)
        => new(StringComparer.Ordinal)
        {
            ["resourceKind"] = "random",
            ["durationMs"] = "0",
            ["moduleHash"] = plan.ModuleHash,
            ["policyHash"] = plan.PolicyHash
        };

    private static Dictionary<string, string> RandomFields(
        ExecutionPlan plan,
        int minInclusive,
        int maxExclusive,
        int value)
    {
        var fields = RandomFields(plan);
        fields["minInclusive"] = minInclusive.ToString(CultureInfo.InvariantCulture);
        fields["maxExclusive"] = maxExclusive.ToString(CultureInfo.InvariantCulture);
        fields["value"] = value.ToString(CultureInfo.InvariantCulture);
        return fields;
    }

    private static string RandomJson(int minInclusive = 0, int maxExclusive = 10)
        => $$"""
        {
          "id": "worker-deterministic-random-audit-validation",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "random" }],
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
                    "call": "random.nextI32",
                    "args": [{ "i32": {{minInclusive}} }, { "i32": {{maxExclusive}} }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private sealed class ValidDeterministicRandomWorker(
        DateTimeOffset logicalNow,
        int minInclusive,
        int maxExclusive) : ISandboxWorkerClient
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
            var context = new SandboxContext(
                runId,
                plan.Policy,
                new ResourceMeter(plan.Budget),
                new BindingRegistry([]),
                audit,
                cancellationToken);
            var value = context.NextRandomInt32(minInclusive, maxExclusive);
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                logicalNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan, usage)));
            audit.Write(new SandboxAuditEvent(
                runId,
                "BindingCall",
                logicalNow,
                true,
                BindingId: "random.nextI32",
                CapabilityId: "random",
                Effect: SandboxEffect.Random,
                ResourceId: "random:i32",
                Fields: RandomFields(plan, minInclusive, maxExclusive, value)));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.FromInt32(value),
                ResourceUsage = usage,
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }
    }

    private sealed class ForgedDeterministicRandomWorker(DateTimeOffset logicalNow) : ISandboxWorkerClient
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
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                logicalNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan, usage)));
            audit.Write(new SandboxAuditEvent(
                runId,
                "BindingCall",
                logicalNow,
                true,
                BindingId: "random.nextI32",
                CapabilityId: "random",
                Effect: SandboxEffect.Random,
                ResourceId: "random:i32",
                Fields: RandomFields(plan)));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.FromInt32(999),
                ResourceUsage = usage,
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }
    }
}
