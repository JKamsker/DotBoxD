using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Hosting;

public sealed class InterpreterResultValidationTests
{
    private const string UntrustedAuditMarker = "untrusted custom interpreter evidence";

    [Theory]
    [InlineData(MalformedResultKind.SuccessWithoutValue)]
    [InlineData(MalformedResultKind.FailureWithoutError)]
    [InlineData(MalformedResultKind.WrongValueType)]
    [InlineData(MalformedResultKind.UnsafeFailureError)]
    [InlineData(MalformedResultKind.WrongModuleHash)]
    [InlineData(MalformedResultKind.WrongPlanHash)]
    [InlineData(MalformedResultKind.WrongPolicyHash)]
    [InlineData(MalformedResultKind.CompiledMode)]
    [InlineData(MalformedResultKind.NotDispatched)]
    [InlineData(MalformedResultKind.UnexpectedArtifact)]
    [InlineData(MalformedResultKind.OverBudgetFuel)]
    [InlineData(MalformedResultKind.IncoherentSummary)]
    [InlineData(MalformedResultKind.MultipleRunIds)]
    public async Task Malformed_custom_interpreter_result_is_replaced_before_audit_publication(MalformedResultKind malformedResultKind)
    {
        var interpreter = new MalformedResultInterpreter(malformedResultKind);
        var observed = new List<SandboxAuditEvent>();
        using var host = CreateHost(interpreter, observed.Add);
        var plan = await PrepareAsync(host);
        var runId = SandboxRunId.New();

        var result = await ExecuteAsync(host, plan, runId, suppressSuccessfulSummary: false);

        Assert.Contains(
            interpreter.ReturnedResult!.AuditEvents,
            auditEvent => auditEvent.Message == UntrustedAuditMarker);
        AssertSanitizedHostFailure(result, plan, runId);
        Assert.Equal(result.AuditEvents, observed);
        Assert.DoesNotContain(observed, auditEvent => auditEvent.Message == UntrustedAuditMarker);
    }

    [Fact]
    public async Task Post_execution_cancellation_does_not_legitimize_a_malformed_result()
    {
        using var cancellation = new CancellationTokenSource();
        var interpreter = new MalformedResultInterpreter(
            MalformedResultKind.SuccessWithoutValue,
            cancellation);
        var observed = new List<SandboxAuditEvent>();
        using var host = CreateHost(interpreter, observed.Add);
        var plan = await PrepareAsync(host);
        var runId = SandboxRunId.New();

        var result = await ExecuteAsync(
            host,
            plan,
            runId,
            suppressSuccessfulSummary: false,
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        AssertSanitizedHostFailure(result, plan, runId);
        Assert.Equal(result.AuditEvents, observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_custom_interpreter_result_preserves_successful_summary_suppression(
        bool suppressSuccessfulSummary)
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = CreateHost(new SandboxInterpreter(), observed.Add);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(
            host,
            plan,
            SandboxRunId.New(),
            suppressSuccessfulSummary);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(suppressSuccessfulSummary ? 0 : 1, result.AuditEvents.Count(e => e.Kind == "RunSummary"));
        Assert.Equal(result.AuditEvents, observed);
    }

    private static SandboxHost CreateHost(
        ISandboxInterpreter interpreter,
        Action<SandboxAuditEvent> observer)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter(interpreter);
            builder.ForwardAuditEventsTo(observer);
        });

    private static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(
            SandboxTestHost.PureScoreJson("interpreter-result-validation"));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxRunId runId,
        bool suppressSuccessfulSummary,
        CancellationToken cancellationToken = default)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                RunId = runId,
                SuppressSuccessfulRunSummaryAudit = suppressSuccessfulSummary
            },
            cancellationToken);

    private static void AssertSanitizedHostFailure(
        SandboxExecutionResult result,
        ExecutionPlan plan,
        SandboxRunId runId)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal("interpreter execution failed", result.Error.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        Assert.Null(result.ArtifactHash);
        Assert.Equal(plan.ModuleHash, result.ModuleHash);
        Assert.Equal(plan.PlanHash, result.PlanHash);
        Assert.Equal(plan.PolicyHash, result.PolicyHash);
        Assert.Equal(new ResourceMeter(plan.Budget).Snapshot(), result.ResourceUsage);

        var summary = Assert.Single(result.AuditEvents);
        Assert.Equal("RunSummary", summary.Kind);
        Assert.Equal(runId, summary.RunId);
        Assert.Equal(1, summary.SequenceNumber);
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.HostFailure, summary.ErrorCode);
        Assert.Equal($"module:{plan.ModuleHash}", summary.ResourceId);
        Assert.Equal("Interpreted", summary.Fields!["executionMode"]);
        Assert.Equal("True", summary.Fields["executionDispatched"]);
        Assert.Equal(plan.ModuleHash, summary.Fields["moduleHash"]);
        Assert.Equal(plan.PlanHash, summary.Fields["planHash"]);
        Assert.Equal(plan.PolicyHash, summary.Fields["policyHash"]);
        Assert.Equal("0", summary.Fields["fuelUsed"]);
    }

    public enum MalformedResultKind
    {
        SuccessWithoutValue,
        FailureWithoutError,
        WrongValueType,
        UnsafeFailureError,
        WrongModuleHash,
        WrongPlanHash,
        WrongPolicyHash,
        CompiledMode,
        NotDispatched,
        UnexpectedArtifact,
        OverBudgetFuel,
        IncoherentSummary,
        MultipleRunIds
    }
    private sealed class MalformedResultInterpreter(MalformedResultKind kind, CancellationTokenSource? cancellation = null)
        : ISandboxInterpreter
    {
        private readonly SandboxInterpreter _inner = new();

        public SandboxExecutionResult? ReturnedResult { get; private set; }

        public async ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken)
        {
            var valid = kind is MalformedResultKind.FailureWithoutError or MalformedResultKind.UnsafeFailureError
                ? await _inner.ExecuteAsync(plan, "missing", input, options, CancellationToken.None)
                    .ConfigureAwait(false)
                : await _inner.ExecuteAsync(plan, entrypoint, input, options, CancellationToken.None)
                    .ConfigureAwait(false);
            valid = valid with { AuditEvents = MarkAudit(valid.AuditEvents) };
            ReturnedResult = Malform(valid, plan, kind);
            cancellation?.Cancel();
            return ReturnedResult;
        }

        private static SandboxExecutionResult Malform(
            SandboxExecutionResult valid,
            ExecutionPlan plan,
            MalformedResultKind kind)
            => kind switch
            {
                MalformedResultKind.SuccessWithoutValue => valid with { Value = null },
                MalformedResultKind.FailureWithoutError => WithoutError(valid),
                MalformedResultKind.WrongValueType => valid with { Value = SandboxValue.FromString("wrong") },
                MalformedResultKind.UnsafeFailureError => valid with
                {
                    Error = new SandboxError(valid.Error!.Code, "authorization bearer secret token")
                },
                MalformedResultKind.WrongModuleHash => valid with { ModuleHash = new string('a', 64) },
                MalformedResultKind.WrongPlanHash => valid with { PlanHash = new string('b', 64) },
                MalformedResultKind.WrongPolicyHash => valid with { PolicyHash = new string('c', 64) },
                MalformedResultKind.CompiledMode => valid with { ActualMode = ExecutionMode.Compiled },
                MalformedResultKind.NotDispatched => valid with { ExecutionDispatched = false },
                MalformedResultKind.UnexpectedArtifact => valid with { ArtifactHash = new string('d', 64) },
                MalformedResultKind.OverBudgetFuel => WithOverBudgetFuel(valid, plan),
                MalformedResultKind.IncoherentSummary => WithIncoherentSummary(valid),
                MalformedResultKind.MultipleRunIds => WithMultipleRunIds(valid),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };

        private static SandboxExecutionResult WithoutError(SandboxExecutionResult failed)
            => new()
            {
                Succeeded = false,
                ResourceUsage = failed.ResourceUsage,
                AuditEvents = failed.AuditEvents,
                ActualMode = failed.ActualMode,
                ExecutionDispatched = failed.ExecutionDispatched,
                ModuleHash = failed.ModuleHash,
                PlanHash = failed.PlanHash,
                PolicyHash = failed.PolicyHash
            };

        private static SandboxExecutionResult WithOverBudgetFuel(
            SandboxExecutionResult valid,
            ExecutionPlan plan)
        {
            var fuelUsed = plan.Budget.MaxFuel + 1;
            var audit = valid.AuditEvents.Select(auditEvent => auditEvent.Kind == "RunSummary"
                ? auditEvent with
                {
                    Fields = WithField(
                        auditEvent.Fields!,
                        "fuelUsed",
                        fuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture))
                }
                : auditEvent).ToArray();
            return valid with
            {
                ResourceUsage = valid.ResourceUsage with { FuelUsed = fuelUsed },
                AuditEvents = audit
            };
        }

        private static SandboxExecutionResult WithIncoherentSummary(SandboxExecutionResult valid)
        {
            var summary = Assert.Single(valid.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
            var incoherent = new SandboxAuditEvent(
                summary.RunId,
                summary.Kind,
                summary.Timestamp,
                false,
                ResourceId: summary.ResourceId,
                ErrorCode: SandboxErrorCode.HostFailure,
                Message: UntrustedAuditMarker,
                Fields: summary.Fields,
                SequenceNumber: summary.SequenceNumber);
            return valid with { AuditEvents = [incoherent] };
        }

        private static SandboxExecutionResult WithMultipleRunIds(SandboxExecutionResult valid)
        {
            var summary = Assert.Single(valid.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
            return valid with
            {
                AuditEvents = [summary, summary with { RunId = SandboxRunId.New(), SequenceNumber = 2 }]
            };
        }

        private static SandboxAuditEvent[] MarkAudit(IReadOnlyList<SandboxAuditEvent> events)
            => events.Select(auditEvent => auditEvent with { Message = UntrustedAuditMarker }).ToArray();

        private static IReadOnlyDictionary<string, string> WithField(
            IReadOnlyDictionary<string, string> fields,
            string name,
            string value)
        {
            var copy = new Dictionary<string, string>(fields, StringComparer.Ordinal)
            {
                [name] = value
            };
            return copy;
        }
    }
}
