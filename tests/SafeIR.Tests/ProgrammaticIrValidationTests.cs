namespace SafeIR.Tests;

public sealed class ProgrammaticIrValidationTests
{
    [Fact]
    public async Task Prepare_rejects_unknown_programmatic_statement_shape()
    {
        var module = ModuleWithBody(
            SandboxType.Unit,
            [
                new UnknownStatement(new SourceSpan(0, 0)),
                new ReturnStatement(new LiteralExpression(SandboxValue.Unit, new SourceSpan(0, 0)), new SourceSpan(0, 0))
            ]);

        var ex = await PrepareThrowsAsync(module);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-STMT-UNKNOWN");
    }

    [Fact]
    public async Task Prepare_rejects_unknown_programmatic_expression_shape()
    {
        var module = ModuleWithBody(
            SandboxType.I32,
            [
                new ReturnStatement(new UnknownExpression(new SourceSpan(0, 0)), new SourceSpan(0, 0))
            ]);

        var ex = await PrepareThrowsAsync(module);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-EXPR-UNKNOWN");
    }

    [Fact]
    public async Task Prepare_rejects_programmatic_collection_literal_type_mismatch()
    {
        var module = ModuleWithBody(
            SandboxType.List(SandboxType.I32),
            [
                new ReturnStatement(
                    new LiteralExpression(
                        new ListValue([SandboxValue.FromString("wrong")], SandboxType.I32),
                        new SourceSpan(0, 0)),
                    new SourceSpan(0, 0))
            ]);

        var ex = await PrepareThrowsAsync(module);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CONST-VALUE");
    }

    [Fact]
    public async Task Runtime_charges_programmatic_collection_literals_against_policy()
    {
        var host = SandboxTestHost.Create();
        var module = ModuleWithBody(
            SandboxType.List(SandboxType.I32),
            [
                new ReturnStatement(
                    new LiteralExpression(
                        new ListValue(
                            [
                                SandboxValue.FromInt32(1),
                                SandboxValue.FromInt32(2),
                                SandboxValue.FromInt32(3)
                            ],
                            SandboxType.I32),
                        new SourceSpan(0, 0)),
                    new SourceSpan(0, 0))
            ]);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithMaxListLength(2)
                .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    private static async Task<SandboxValidationException> PrepareThrowsAsync(SandboxModule module)
    {
        var host = SandboxTestHost.Create();
        return await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));
    }

    private static SandboxModule ModuleWithBody(SandboxType returnType, IReadOnlyList<Statement> body)
        => new(
            "programmatic-ir-validation",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    returnType,
                    body)
            ],
            new Dictionary<string, string>());

    private sealed record UnknownStatement(SourceSpan Span) : Statement(Span);

    private sealed record UnknownExpression(SourceSpan Span) : Expression(Span);
}
