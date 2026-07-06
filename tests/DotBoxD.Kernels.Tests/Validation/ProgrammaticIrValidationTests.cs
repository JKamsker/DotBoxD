using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ProgrammaticIrValidationTests
{
    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 0, -1)]
    public void SemVersion_rejects_negative_components(int major, int minor, int patch)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemVersion(major, minor, patch));
    }

    [Fact]
    public void SemVersion_with_rejects_negative_components()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SemVersion.One with { Patch = -1 });
    }

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
    public void ModuleValidator_returns_failed_result_for_invalid_programmatic_literal()
    {
        var module = ModuleWithBody(
            SandboxType.F64,
            [
                new ReturnStatement(
                    new LiteralExpression(new F64Value(double.NaN), new SourceSpan(0, 0)),
                    new SourceSpan(0, 0))
            ]);

        var result = new DotBoxD.Kernels.Validation.ModuleValidator().Validate(
            module,
            new BindingRegistryBuilder().Build(),
            SandboxPolicyBuilder.Create().Build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "E-CONST-F64");
    }

    [Fact]
    public void ModuleValidator_reports_null_function_parameter_entries()
    {
        var module = ModuleWithFunction(new SandboxFunction(
            "main",
            IsEntrypoint: true,
            [null!],
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, new SourceSpan(0, 0)), new SourceSpan(0, 0))]));

        var validation = ValidateWithoutException(module);

        Assert.False(validation.Succeeded);
        Assert.Contains(validation.Diagnostics, d => MentionsNullEntry(d, "parameters"));
    }

    [Fact]
    public void ModuleValidator_reports_null_function_body_entries()
    {
        var module = ModuleWithFunction(new SandboxFunction(
            "main",
            IsEntrypoint: true,
            [],
            SandboxType.Unit,
            [null!]));

        var validation = ValidateWithoutException(module);

        Assert.False(validation.Succeeded);
        Assert.Contains(validation.Diagnostics, d => MentionsNullEntry(d, "body"));
    }

    [Fact]
    public async Task Prepare_rejects_nested_collection_literal_with_forbidden_descriptor()
    {
        var module = ModuleWithValue(
            SandboxType.List(SandboxType.String),
            new ListValue(
                [SandboxValue.FromString("System.IO.File.ReadAllText")],
                SandboxType.String));

        var ex = await PrepareThrowsAsync(module);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-CLR-REF");
    }

    [Fact]
    public async Task Prepare_rejects_nested_collection_literal_with_non_finite_f64()
    {
        var module = ModuleWithValue(
            SandboxType.List(SandboxType.F64),
            new ListValue([new F64Value(double.NaN)], SandboxType.F64));

        var ex = await PrepareThrowsAsync(module);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CONST-F64");
    }

    [Fact]
    public async Task Prepare_rejects_nested_collection_literal_with_invalid_path()
    {
        var module = ModuleWithValue(
            SandboxType.List(SandboxType.SandboxPath),
            new ListValue(
                [new SandboxPathValue(new SandboxPath("../secret.txt"))],
                SandboxType.SandboxPath));

        var ex = await PrepareThrowsAsync(module);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CONST-PATH");
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

    private static SandboxModule ModuleWithFunction(SandboxFunction function)
        => new(
            "programmatic-ir-validation",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [function],
            new Dictionary<string, string>());

    private static SandboxModule ModuleWithValue(SandboxType returnType, SandboxValue value)
        => ModuleWithBody(
            returnType,
            [
                new ReturnStatement(
                    new LiteralExpression(value, new SourceSpan(0, 0)),
                    new SourceSpan(0, 0))
            ]);

    private static ModuleValidationResult ValidateWithoutException(SandboxModule module)
    {
        ModuleValidationResult? validation = null;
        var exception = Record.Exception(() => validation = ValidateModule(module));

        Assert.Null(exception);
        Assert.NotNull(validation);
        return validation!;
    }

    private static ModuleValidationResult ValidateModule(SandboxModule module)
        => new DotBoxD.Kernels.Validation.ModuleValidator().Validate(
            module,
            new BindingRegistryBuilder().Build());

    private static bool MentionsNullEntry(SandboxDiagnostic diagnostic, string collectionName)
        => diagnostic.Message.Contains(collectionName, StringComparison.OrdinalIgnoreCase)
            && diagnostic.Message.Contains("null", StringComparison.OrdinalIgnoreCase);

    private sealed record UnknownStatement(SourceSpan Span) : Statement(Span);

    private sealed record UnknownExpression(SourceSpan Span) : Expression(Span);
}
