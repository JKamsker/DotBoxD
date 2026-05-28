using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class InheritedTupleElementNameTests
{
    [Fact]
    public void DuplicateInheritedMethodsWithSameTupleElementNames_Deduplicate()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.DuplicateInheritedSameTupleNames
            {
                public interface ILeft
                {
                    int Echo((int A, int B) value);
                }

                public interface IRight
                {
                    int Echo((int A, int B) value);
                }

                [ShaRpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC003");
        GetProxy(runResult).Should().Contain("public int Echo((int A, int B) value)");
    }

    [Fact]
    public void DuplicateInheritedMethodsWithDifferentTupleElementNames_RejectService()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.DuplicateInheritedTupleNames
            {
                public interface ILeft
                {
                    int Echo((int A, int B) value);
                }

                public interface IRight
                {
                    int Echo((int X, int Y) value);
                }

                [ShaRpcService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("incompatible tuple element names"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    private static string GetProxy(GeneratorDriverRunResult runResult) =>
        runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IFoo.ShaRpcProxy.g.cs"))
            .SourceText.ToString();

    private static (CSharpCompilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (compilation.AddSyntaxTrees(runResult.GeneratedTrees), runResult);
    }

    private static void AssertCompiles(CSharpCompilation compilation)
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
