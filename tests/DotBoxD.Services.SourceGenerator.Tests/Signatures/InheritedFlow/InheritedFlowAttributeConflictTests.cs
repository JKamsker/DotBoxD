using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures.InheritedFlow;

public class InheritedFlowAttributeConflictTests
{
    [Fact]
    public void DuplicateInheritedMethodsWithRenamedNotNullIfNotNullReference_AcceptService()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Diagnostics.CodeAnalysis;

            namespace Regress.RenamedInheritedNotNullIfNotNullReference
            {
                public interface ILeft
                {
                    [return: NotNullIfNotNull(nameof(value))]
                    string? Normalize(string? value);
                }

                public interface IRight
                {
                    [return: NotNullIfNotNull(nameof(input))]
                    string? Normalize(string? input);
                }

                [RpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var (compilation, runResult) = Run(source);

        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("inherited") &&
            d.GetMessage().Contains("flow attributes"));
    }

    [Fact]
    public void DuplicateInheritedMethodsWithDifferentParameterFlowAttributes_RejectService()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Diagnostics.CodeAnalysis;

            namespace Regress.DuplicateInheritedParameterFlowAttributes
            {
                public interface ILeft
                {
                    void Save([AllowNull] string? value);
                }

                public interface IRight
                {
                    void Save([DisallowNull] string? value);
                }

                [RpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var (compilation, runResult) = Run(source);

        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("inherited") &&
            d.GetMessage().Contains("attribute"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("ICombined."));
    }

    private static (CSharpCompilation Compilation, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        return (compilation, driver.GetRunResult());
    }
}
