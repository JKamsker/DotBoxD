using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiPartialMethodAuthoringTests
{
    [Fact]
    public void Local_handler_on_partial_definition_uses_implemented_part_body()
    {
        var result = UiGeneratorFixture.Generate("""
            using DotBoxD.UI.Authoring;
            public static partial class Counter
            {
                [UiLocalHandler] public static partial int Increment(int value);
                public static partial int Increment(int value) => value + 1;
            }
            """);

        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXU001");
        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var source = Assert.Single(result.Result.GeneratedTrees).ToString();
        Assert.Contains("IncrementUiKernel", source, StringComparison.Ordinal);
    }
}
