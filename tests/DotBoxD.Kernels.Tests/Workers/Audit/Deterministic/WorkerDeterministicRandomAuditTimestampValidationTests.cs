using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerDeterministicRandomAuditTimestampValidationTests
{
    [Fact]
    public async Task Worker_rejects_deterministic_random_audit_with_wall_clock_timestamp()
    {
        var worker = new ForgedRandomAuditTimestampWorker();
        using var host = RandomHost(worker);
        var module = await host.ImportJsonAsync(RandomJson());
        var plan = await host.PrepareAsync(module, DeterministicRandomPolicy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost RandomHost(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddRandomBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static SandboxPolicy DeterministicRandomPolicy()
        => new(
            "deterministic-random-worker-audit",
            SandboxEffects.Pure | SandboxEffect.Random,
            [new CapabilityGrant("random", new Dictionary<string, string>())],
            new ResourceLimits(),
            Deterministic: true,
            LogicalNow: null,
            RandomSeed: 123);

    private static string RandomJson()
        => """
        {
          "id": "worker-deterministic-random-audit-timestamp",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "random", "reason": "test random" }],
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
                    "args": [{ "i32": 0 }, { "i32": 10 }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private sealed class ForgedRandomAuditTimestampWorker : ISandboxWorkerClient
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
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan, usage)));
            audit.Write(new SandboxAuditEvent(
                runId,
                "BindingCall",
                DateTimeOffset.UtcNow,
                true,
                BindingId: "random.nextI32",
                CapabilityId: "random",
                Effect: SandboxEffect.Random,
                ResourceId: "random:i32",
                Fields: RandomFields(
                    plan,
                    minInclusive: 0,
                    maxExclusive: 10,
                    value: FirstDeterministicRandomInt32(seed: 123, minInclusive: 0, maxExclusive: 10))));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.FromInt32(FirstDeterministicRandomInt32(seed: 123, minInclusive: 0, maxExclusive: 10)),
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
                FuelUsed: SafeRandomBindings.NextI32.CostModel.BaseFuel,
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
                RunSummaryAuditFields.Create(plan, new ResourceMeter(plan.Budget), ExecutionMode.Interpreted, "None"),
                StringComparer.Ordinal);
            fields["fuelUsed"] = usage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields["hostCalls"] = usage.HostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return fields;
        }

        private static Dictionary<string, string> RandomFields(
            ExecutionPlan plan,
            int minInclusive,
            int maxExclusive,
            int value)
        {
            var fields = WorkerAuditValidationTestSupport.BindingFields(plan, "random");
            fields["minInclusive"] = minInclusive.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields["maxExclusive"] = maxExclusive.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields["value"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return fields;
        }

        private static int FirstDeterministicRandomInt32(ulong seed, int minInclusive, int maxExclusive)
        {
            var range = (ulong)((long)maxExclusive - minInclusive);
            var threshold = (1UL << 32) % range;
            var state = seed;
            while (true)
            {
                var value = NextUInt32(ref state);
                if (value >= threshold)
                {
                    return checked((int)(minInclusive + (long)(value % range)));
                }
            }
        }

        private static uint NextUInt32(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                var value = state;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (uint)(value >> 32);
            }
        }
    }
}
