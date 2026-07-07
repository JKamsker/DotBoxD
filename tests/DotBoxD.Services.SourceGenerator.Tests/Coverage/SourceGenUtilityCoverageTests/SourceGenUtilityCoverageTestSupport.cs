using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

internal static class SourceGenUtilityCoverageTestSupport
{
    internal static string ExtensionsTextFor(params string[] sources)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(sources);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        return runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "DotBoxDRpcExtensions.g.cs")
            .SourceText.ToString();
    }

    internal static string DiagnosticPathFor(params SyntaxTree[] trees)
    {
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(trees);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        var diagnostic = runResult.Diagnostics.First(d =>
            d.Id == "DBXS003" && d.GetMessage().Contains("would collide"));
        return diagnostic.Location.GetLineSpan().Path;
    }

    internal static string GetFooProxy(GeneratorDriverRunResult runResult) =>
        runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IFoo.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();

    internal static void AssertRejectedForTupleNames(GeneratorDriverRunResult runResult)
    {
        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "DBXS003" && d.GetMessage().Contains("incompatible tuple element names"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    internal static (CSharpCompilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        return (compilation.AddSyntaxTrees(runResult.GeneratedTrees), runResult);
    }

    internal static void AssertCompiles(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }
}
