using DotBoxD.Kernels.Interpreter;

namespace DotBoxD.Kernels.Tests.Hosting;

using static InterpreterSecurityValidationTestSupport;

public sealed class InterpreterRunSummarySecurityValidationTests
{
    [Fact]
    public async Task Interpreted_summary_requires_none_cache_status()
    {
        var interpreter = SummaryMutatingInterpreter(fields => WithField(fields, "cacheStatus", "Hit"));

        var outcome = await ExecuteAsync(
            MathModuleWithUnrelatedHelper(),
            Policy(),
            interpreter);

        AssertRejectedWithoutPublication(outcome);
    }

    [Fact]
    public async Task Interpreted_summary_forbids_materialization_status()
    {
        var interpreter = SummaryMutatingInterpreter(
            fields => WithField(fields, "materializationStatus", "Materialized"));

        var outcome = await ExecuteAsync(
            MathModuleWithUnrelatedHelper(),
            Policy(),
            interpreter);

        AssertRejectedWithoutPublication(outcome);
    }

    [Theory]
    [InlineData("runtimeForm", "LoadedAssembly")]
    [InlineData("cacheKey", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("artifactHash", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public async Task Existing_compiled_summary_fields_remain_forbidden(
        string fieldName,
        string fieldValue)
    {
        var interpreter = SummaryMutatingInterpreter(fields => WithField(fields, fieldName, fieldValue));

        var outcome = await ExecuteAsync(
            MathModuleWithUnrelatedHelper(),
            Policy(),
            interpreter);

        AssertRejectedWithoutPublication(outcome);
    }

    private static ISandboxInterpreter SummaryMutatingInterpreter(
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> mutate)
        => new TransformingInterpreter((_, result) => ReplaceFirstAudit(
            result,
            auditEvent => auditEvent.Kind == "RunSummary",
            auditEvent => auditEvent with
            {
                Message = AuditMarker,
                Fields = mutate(auditEvent.Fields!)
            }));
}
