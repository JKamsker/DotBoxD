using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter;

public sealed class InterpreterNestingLimitTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public async Task Deep_programmatic_expression_fails_with_sandbox_error()
    {
        using var host = SandboxTestHost.Create();
        var plan = await host.PrepareAsync(
            Module(expressionDepth: 256),
            SandboxPolicyBuilder.Create().WithFuel(10_000).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error?.Code);
        Assert.Contains("interpreter nesting", result.Error?.SafeMessage, StringComparison.Ordinal);
    }

    private static SandboxModule Module(int expressionDepth)
    {
        Expression value = new LiteralExpression(SandboxValue.FromInt32(1), Span);
        for (var depth = 0; depth < expressionDepth; depth++)
        {
            value = new UnaryExpression("-", value, Span);
        }

        return new SandboxModule(
            "deep-expression",
            SemVersion.One,
            SemVersion.One,
            [],
            [new SandboxFunction(
                "main",
                IsEntrypoint: true,
                [],
                SandboxType.I32,
                [new ReturnStatement(value, Span)])],
            new Dictionary<string, string>());
    }
}
