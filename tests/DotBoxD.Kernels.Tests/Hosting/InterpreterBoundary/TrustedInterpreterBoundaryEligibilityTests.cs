using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Hosting;

public sealed class TrustedInterpreterBoundaryEligibilityTests
{
    [Fact]
    public async Task Exact_built_in_canonical_suppressed_success_is_eligible()
    {
        var fixture = await CreateFixtureAsync();

        Assert.True(IsEligible(fixture));
    }

    [Theory]
    [InlineData(OptionDisqualifier.AutoMode)]
    [InlineData(OptionDisqualifier.CompiledMode)]
    [InlineData(OptionDisqualifier.WorkerProcess)]
    [InlineData(OptionDisqualifier.UnsuppressedAudit)]
    [InlineData(OptionDisqualifier.DebugTrace)]
    public async Task Ineligible_options_retain_full_validation(OptionDisqualifier disqualifier)
    {
        var fixture = await CreateFixtureAsync();
        var options = DisqualifiedOptions(fixture.Options, disqualifier);

        Assert.False(IsEligible(fixture, options: options));
    }

    [Theory]
    [InlineData(ResultDisqualifier.MissingValue)]
    [InlineData(ResultDisqualifier.CompiledMode)]
    [InlineData(ResultDisqualifier.NotDispatched)]
    [InlineData(ResultDisqualifier.UnexpectedArtifact)]
    [InlineData(ResultDisqualifier.WrongModuleHash)]
    [InlineData(ResultDisqualifier.WrongPlanHash)]
    [InlineData(ResultDisqualifier.WrongPolicyHash)]
    [InlineData(ResultDisqualifier.HostCall)]
    [InlineData(ResultDisqualifier.FileRead)]
    [InlineData(ResultDisqualifier.FileWrite)]
    [InlineData(ResultDisqualifier.NetworkRead)]
    [InlineData(ResultDisqualifier.NetworkWrite)]
    [InlineData(ResultDisqualifier.LogEvent)]
    public async Task Ineligible_result_envelopes_retain_full_validation(ResultDisqualifier disqualifier)
    {
        var fixture = await CreateFixtureAsync();
        var result = DisqualifiedResult(fixture.Result, disqualifier);

        Assert.False(IsEligible(fixture, result: result));
    }

    [Fact]
    public async Task Forwarding_interpreter_type_is_not_eligible_for_bypass()
    {
        var fixture = await CreateFixtureAsync();
        var forwarding = new TransformingInterpreter((_, result) => result);

        Assert.False(IsEligible(fixture, interpreter: forwarding));
    }

    [Fact]
    public async Task Binding_bearing_entrypoint_is_not_eligible_for_bypass()
    {
        var fixture = await CreateFixtureAsync();
        var references = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["main"] = new HashSet<string>(["test.binding"], StringComparer.Ordinal)
        };
        var bindingPlan = TrustedInterpreterBoundaryTestSupport.WithBindingReferences(
            fixture.Plan,
            references);

        Assert.Equal(fixture.Plan.ModuleHash, bindingPlan.ModuleHash);
        Assert.Equal(fixture.Plan.PlanHash, bindingPlan.PlanHash);
        Assert.False(IsEligible(fixture, plan: bindingPlan));
    }

    [Fact]
    public async Task Missing_entrypoint_binding_metadata_is_not_eligible_for_bypass()
    {
        var fixture = await CreateFixtureAsync();

        Assert.False(IsEligible(fixture, entrypoint: "missing"));
    }

    [Fact]
    public async Task Noncanonical_nonempty_and_failed_results_are_not_eligible_for_bypass()
    {
        var fixture = await CreateFixtureAsync();
        var noncanonicalEmpty = fixture.Result with
        {
            AuditEvents = new OwnedAuditEventSnapshot([])
        };
        var nonemptyAudit = fixture.Result with
        {
            AuditEvents =
            [
                new SandboxAuditEvent(
                    SandboxRunId.New(),
                    "Unexpected",
                    DateTimeOffset.UnixEpoch,
                    true)
            ]
        };
        var failed = FailedResult(fixture);

        Assert.Empty(noncanonicalEmpty.AuditEvents);
        Assert.NotSame(InMemoryAuditSink.EmptyEventSnapshot, noncanonicalEmpty.AuditEvents);
        Assert.False(IsEligible(fixture, result: noncanonicalEmpty));
        Assert.NotEmpty(nonemptyAudit.AuditEvents);
        Assert.False(IsEligible(fixture, result: nonemptyAudit));
        Assert.False(failed.Succeeded);
        Assert.Same(InMemoryAuditSink.EmptyEventSnapshot, failed.AuditEvents);
        Assert.False(IsEligible(fixture, result: failed));
    }

    private static bool IsEligible(
        EligibilityFixture fixture,
        ISandboxInterpreter? interpreter = null,
        ExecutionPlan? plan = null,
        SandboxExecutionOptions? options = null,
        SandboxExecutionResult? result = null,
        string entrypoint = "main")
        => InterpreterExecutionBoundary.CanReturnBuiltInResultWithoutValidation(
            interpreter ?? fixture.Interpreter,
            plan ?? fixture.Plan,
            entrypoint,
            options ?? fixture.Options,
            result ?? fixture.Result);

    private static SandboxExecutionOptions DisqualifiedOptions(
        SandboxExecutionOptions eligible,
        OptionDisqualifier disqualifier)
        => disqualifier switch
        {
            OptionDisqualifier.AutoMode => eligible with { Mode = ExecutionMode.Auto },
            OptionDisqualifier.CompiledMode => eligible with { Mode = ExecutionMode.Compiled },
            OptionDisqualifier.WorkerProcess => eligible with { Isolation = SandboxIsolation.WorkerProcess },
            OptionDisqualifier.UnsuppressedAudit => eligible with { SuppressSuccessfulRunSummaryAudit = false },
            OptionDisqualifier.DebugTrace => eligible with { EnableDebugTrace = true },
            _ => throw new ArgumentOutOfRangeException(nameof(disqualifier), disqualifier, null)
        };

    private static SandboxExecutionResult DisqualifiedResult(
        SandboxExecutionResult eligible,
        ResultDisqualifier disqualifier)
        => disqualifier switch
        {
            ResultDisqualifier.MissingValue => eligible with { Value = null },
            ResultDisqualifier.CompiledMode => eligible with { ActualMode = ExecutionMode.Compiled },
            ResultDisqualifier.NotDispatched => eligible with { ExecutionDispatched = false },
            ResultDisqualifier.UnexpectedArtifact => eligible with { ArtifactHash = "unexpected" },
            ResultDisqualifier.WrongModuleHash => eligible with { ModuleHash = eligible.ModuleHash + "x" },
            ResultDisqualifier.WrongPlanHash => eligible with { PlanHash = eligible.PlanHash + "x" },
            ResultDisqualifier.WrongPolicyHash => eligible with { PolicyHash = eligible.PolicyHash + "x" },
            ResultDisqualifier.HostCall => WithUsage(eligible, eligible.ResourceUsage with { HostCalls = 1 }),
            ResultDisqualifier.FileRead => WithUsage(eligible, eligible.ResourceUsage with { FileBytesRead = 1 }),
            ResultDisqualifier.FileWrite => WithUsage(eligible, eligible.ResourceUsage with { FileBytesWritten = 1 }),
            ResultDisqualifier.NetworkRead => WithUsage(eligible, eligible.ResourceUsage with { NetworkBytesRead = 1 }),
            ResultDisqualifier.NetworkWrite => WithUsage(eligible, eligible.ResourceUsage with { NetworkBytesWritten = 1 }),
            ResultDisqualifier.LogEvent => WithUsage(eligible, eligible.ResourceUsage with { LogEvents = 1 }),
            _ => throw new ArgumentOutOfRangeException(nameof(disqualifier), disqualifier, null)
        };

    private static SandboxExecutionResult WithUsage(
        SandboxExecutionResult result,
        SandboxResourceUsage usage)
        => result with { ResourceUsage = usage };

    private static SandboxExecutionResult FailedResult(EligibilityFixture fixture)
        => new()
        {
            Succeeded = false,
            Error = new SandboxError(SandboxErrorCode.InvalidInput, "expected failure"),
            ResourceUsage = fixture.Result.ResourceUsage,
            AuditEvents = InMemoryAuditSink.EmptyEventSnapshot,
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = fixture.Plan.ModuleHash,
            PlanHash = fixture.Plan.PlanHash,
            PolicyHash = fixture.Plan.PolicyHash
        };

    private static async Task<EligibilityFixture> CreateFixtureAsync()
    {
        using var host = TrustedInterpreterBoundaryTestSupport.CreateBuiltInHost();
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule);
        var options = TrustedInterpreterBoundaryTestSupport.SuppressedOptions(
            runId: SandboxRunId.New());
        var interpreter = new SandboxInterpreter();
        var result = await interpreter.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            options,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, Assert.IsType<I32Value>(result.Value).Value);
        Assert.Same(InMemoryAuditSink.EmptyEventSnapshot, result.AuditEvents);
        Assert.True(plan.BindingReferences.TryGetValue("main", out var bindings));
        Assert.Empty(bindings);
        return new EligibilityFixture(interpreter, plan, options, result);
    }

    public enum OptionDisqualifier
    {
        AutoMode,
        CompiledMode,
        WorkerProcess,
        UnsuppressedAudit,
        DebugTrace
    }

    public enum ResultDisqualifier
    {
        MissingValue,
        CompiledMode,
        NotDispatched,
        UnexpectedArtifact,
        WrongModuleHash,
        WrongPlanHash,
        WrongPolicyHash,
        HostCall,
        FileRead,
        FileWrite,
        NetworkRead,
        NetworkWrite,
        LogEvent
    }

    private sealed record EligibilityFixture(
        SandboxInterpreter Interpreter,
        ExecutionPlan Plan,
        SandboxExecutionOptions Options,
        SandboxExecutionResult Result);
}
