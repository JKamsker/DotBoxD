namespace DotBoxD.Kernels.Tests.Hosting;

using static InterpreterSecurityValidationTestSupport;

public sealed class InterpreterDebugTraceSecurityValidationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Debug_trace_rejects_fuel_outside_plan_budget(bool aboveMaximum)
    {
        var interpreter = new TransformingInterpreter((plan, result) => MutateTrace(
            result,
            "fuelRemaining",
            (aboveMaximum ? plan.Budget.MaxFuel + 1 : -1).ToString(
                System.Globalization.CultureInfo.InvariantCulture)));

        var outcome = await ExecuteWithDebugTraceAsync(interpreter);

        AssertRejectedWithoutPublication(outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Debug_trace_accepts_fuel_at_plan_budget_boundaries(bool atMaximum)
    {
        var interpreter = new TransformingInterpreter((plan, result) => MutateTrace(
            result,
            "fuelRemaining",
            (atMaximum ? plan.Budget.MaxFuel : 0).ToString(
                System.Globalization.CultureInfo.InvariantCulture)));

        var outcome = await ExecuteWithDebugTraceAsync(interpreter);

        Assert.True(outcome.Result.Succeeded, outcome.Result.Error?.SafeMessage);
        Assert.Equal(outcome.Result.AuditEvents, outcome.Observed);
    }

    [Fact]
    public async Task Binding_trace_must_be_referenced_by_its_stated_function()
    {
        var interpreter = new TransformingInterpreter((_, result) => MutateTrace(
            result,
            "functionId",
            "helper",
            requireBindingTrace: true));

        var outcome = await ExecuteWithDebugTraceAsync(interpreter);

        AssertRejectedWithoutPublication(outcome);
    }

    private static ValueTask<InterpreterValidationOutcome> ExecuteWithDebugTraceAsync(
        TransformingInterpreter interpreter)
        => ExecuteAsync(
            MathModuleWithUnrelatedHelper(),
            Policy(),
            interpreter,
            options: new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                EnableDebugTrace = true
            });

    private static SandboxExecutionResult MutateTrace(
        SandboxExecutionResult result,
        string fieldName,
        string fieldValue,
        bool requireBindingTrace = false)
        => ReplaceFirstAudit(
            result,
            auditEvent => auditEvent.Kind == "DebugTrace" &&
                (!requireBindingTrace || auditEvent.Fields!["category"] == "binding"),
            auditEvent => auditEvent with
            {
                Message = AuditMarker,
                Fields = WithField(auditEvent.Fields!, fieldName, fieldValue)
            });
}
