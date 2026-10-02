using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerAuditResourceValidatorCancellationTests
{
    private const string BindingId = "audit.probe";
    private const string CapabilityId = "audit.probe.read";

    [Fact]
    public async Task Worker_result_is_cancelled_when_audit_resource_validator_cancels_the_caller()
    {
        using var callerCancellation = new CancellationTokenSource();
        var validatorCalls = 0;
        var worker = new SuccessfulAuditWorker();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(AuditedBinding((_, _) =>
            {
                validatorCalls++;
                callerCancellation.Cancel();
                return true;
            }));
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });
        var plan = await host.PrepareAsync(Module(), Policy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess },
            callerCancellation.Token);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.Equal(1, validatorCalls);
        Assert.Equal(1, worker.Calls);
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.False(result.ExecutionDispatched);
        var summary = Assert.Single(result.AuditEvents, audit => audit.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, summary.ErrorCode);
        Assert.DoesNotContain(result.AuditEvents, audit => audit.Success);
    }

    private static BindingDescriptor AuditedBinding(BindingAuditResourceValidator validator)
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.HostStateRead | SandboxEffect.Audit,
            CapabilityId,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.ReadOnlyExternal,
            static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)),
            GrantValidator: static (_, _) => { },
            AuditResourceValidator: validator);

    private static SandboxModule Module()
        => new(
            "worker-audit-resource-validator-cancellation",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [new CapabilityRequest(CapabilityId, "validate worker audit evidence")],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [
                        new ReturnStatement(
                            new CallExpression(BindingId, [], null, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .Grant(
                CapabilityId,
                new { scope = "audit" },
                SandboxEffect.HostStateRead | SandboxEffect.Audit)
            .WithFuel(1_000)
            .Build();

    private sealed class SuccessfulAuditWorker : ISandboxWorkerClient
    {
        public int Calls { get; private set; }

        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var runId = options.RunId ?? SandboxRunId.New();
            var usage = Usage(plan);
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
                BindingAuditKinds.BindingCall,
                DateTimeOffset.UtcNow,
                true,
                BindingId: BindingId,
                CapabilityId: CapabilityId,
                Effect: SandboxEffect.HostStateRead | SandboxEffect.Audit,
                ResourceId: "audit:probe",
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

        private static SandboxResourceUsage Usage(ExecutionPlan plan)
            => new(
                FuelUsed: 1,
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
                StringComparer.Ordinal)
            {
                ["fuelUsed"] = "1",
                ["hostCalls"] = "1"
            };
            return fields;
        }

        private static Dictionary<string, string> BindingFields(ExecutionPlan plan)
            => new(StringComparer.Ordinal)
            {
                ["resourceKind"] = "audit-probe",
                ["durationMs"] = "0",
                ["moduleHash"] = plan.ModuleHash,
                ["policyHash"] = plan.PolicyHash
            };
    }
}
