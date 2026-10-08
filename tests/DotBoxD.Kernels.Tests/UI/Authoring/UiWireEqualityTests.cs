namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiWireEqualityTests
{
    [Theory]
    [InlineData("{ var left = 1.0m; var right = 1m; return left == right; }")]
    [InlineData("{ var left = 1.0m; var right = 1m; return left != right; }")]
    [InlineData("{ var left = 1.0m; var right = 1.0m; return left == right; }")]
    [InlineData("{ var left = 0.0m; var right = 0m; return left == right; }")]
    public void Decimal_wire_equality_fails_closed_at_compile_time(string body)
        => UiBooleanGeneratorFixture.AssertUnsupported(body);

    [Theory]
    [InlineData("System.DateTime", "==")]
    [InlineData("System.DateTime", "!=")]
    [InlineData("System.DateTimeOffset", "==")]
    [InlineData("System.DateTimeOffset", "!=")]
    [InlineData("System.Threading.CancellationToken", "==")]
    [InlineData("System.Threading.CancellationToken", "!=")]
    public void Framework_wire_comparisons_without_scalar_identity_fail_closed(string type, string operation)
    {
        var result = UiGeneratorFixture.Generate($$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.UI.Authoring;
            public static partial class Counter
            {
                [HostBinding("review.read", "review.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                public static {{type}} Read() => default;
                [UiLocalHandler] public static bool Handle()
                {
                    var left = Read();
                    var right = Read();
                    return left {{operation}} right;
                }
            }
            """);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Empty(result.Result.GeneratedTrees);
    }
}
