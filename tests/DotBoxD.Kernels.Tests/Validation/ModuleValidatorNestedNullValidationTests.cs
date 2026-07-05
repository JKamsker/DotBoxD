using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorNestedNullValidationTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Theory]
    [MemberData(nameof(NullExpressionCases))]
    public void ModuleValidator_reports_null_nested_expression_members(
        string scenario,
        SandboxModule module,
        string[] expectedTerms)
    {
        ModuleValidationResult? result = null;

        var exception = Record.Exception(() => result = Validate(module));

        Assert.True(exception is null, $"{scenario}: {exception}");
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => DiagnosticMentions(d, expectedTerms));
    }

    [Theory]
    [MemberData(nameof(NullBlockEntryCases))]
    public void ModuleValidator_reports_null_nested_block_entries(
        string scenario,
        SandboxModule module,
        string[] expectedTerms)
    {
        ModuleValidationResult? result = null;

        var exception = Record.Exception(() => result = Validate(module));

        Assert.True(exception is null, $"{scenario}: {exception}");
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => DiagnosticMentions(d, expectedTerms));
    }

    public static TheoryData<string, SandboxModule, string[]> NullExpressionCases()
        => new()
        {
            {
                "return value",
                ModuleWithBody(SandboxType.Unit, [new ReturnStatement(null!, Span)]),
                ["return", "null"]
            },
            {
                "expression statement value",
                ModuleWithBody(
                    SandboxType.Unit,
                    [new ExpressionStatement(null!, Span), UnitReturn()]),
                ["expression", "null"]
            },
            {
                "assignment value",
                ModuleWithBody(
                    SandboxType.Unit,
                    [new AssignmentStatement("x", null!, Span), UnitReturn()]),
                ["assignment", "null"]
            },
            {
                "unary operand",
                ModuleWithBody(
                    SandboxType.I32,
                    [new ReturnStatement(new UnaryExpression("-", null!, Span), Span)]),
                ["operand", "null"]
            },
            {
                "binary left operand",
                ModuleWithBody(
                    SandboxType.I32,
                    [new ReturnStatement(new BinaryExpression(null!, "+", I32(1), Span), Span)]),
                ["left", "null"]
            },
            {
                "call argument",
                ModuleWithIdentityHelper(
                    [new ReturnStatement(new CallExpression("identity", [null!], null, Span), Span)]),
                ["argument", "null"]
            },
            {
                "if condition",
                ModuleWithBody(
                    SandboxType.Unit,
                    [
                        new IfStatement(null!, [UnitReturn()], [UnitReturn()], Span)
                    ]),
                ["condition", "null"]
            },
            {
                "for range start",
                ModuleWithBody(
                    SandboxType.Unit,
                    [
                        new ForRangeStatement("i", null!, I32(1), [UnitReturn()], Span)
                    ]),
                ["for range start", "null"]
            }
        };

    public static TheoryData<string, SandboxModule, string[]> NullBlockEntryCases()
        => new()
        {
            {
                "if then block",
                ModuleWithBody(
                    SandboxType.Unit,
                    [
                        new IfStatement(True(), [null!, UnitReturn()], [UnitReturn()], Span)
                    ]),
                ["then", "null"]
            },
            {
                "if else block",
                ModuleWithBody(
                    SandboxType.Unit,
                    [
                        new IfStatement(True(), [UnitReturn()], [null!, UnitReturn()], Span)
                    ]),
                ["else", "null"]
            },
            {
                "while body",
                ModuleWithBody(
                    SandboxType.Unit,
                    [
                        new WhileStatement(True(), [null!], Span),
                        UnitReturn()
                    ]),
                ["body", "null"]
            },
            {
                "function body",
                ModuleWithBody(SandboxType.Unit, [null!, UnitReturn()]),
                ["body", "null"]
            }
        };

    private static ModuleValidationResult Validate(SandboxModule module)
        => new ModuleValidator().Validate(module, new BindingRegistry([]));

    private static SandboxModule ModuleWithIdentityHelper(IReadOnlyList<Statement> body)
        => new(
            "module-validator-nested-null-validation",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "identity",
                    IsEntrypoint: false,
                    [new Parameter("value", SandboxType.I32)],
                    SandboxType.I32,
                    [new ReturnStatement(new VariableExpression("value", Span), Span)]),
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.I32,
                    body)
            ],
            new Dictionary<string, string>());

    private static SandboxModule ModuleWithBody(SandboxType returnType, IReadOnlyList<Statement> body)
        => new(
            "module-validator-nested-null-validation",
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

    private static bool DiagnosticMentions(SandboxDiagnostic diagnostic, IReadOnlyList<string> expectedTerms)
    {
        for (var i = 0; i < expectedTerms.Count; i++)
        {
            if (!diagnostic.Message.Contains(expectedTerms[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static ReturnStatement UnitReturn()
        => new(new LiteralExpression(SandboxValue.Unit, Span), Span);

    private static LiteralExpression True()
        => new(SandboxValue.FromBool(true), Span);

    private static LiteralExpression I32(int value)
        => new(SandboxValue.FromInt32(value), Span);
}
