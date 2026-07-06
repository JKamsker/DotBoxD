using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures.InheritedCallerInfo;

public class InheritedCallerInfoConflictTests
{
    [Fact]
    public void DuplicateInheritedMethodsWithDifferentCallerInfoAttributes_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Runtime.CompilerServices;

            namespace Regress.InheritedCallerInfoConflict
            {
                public interface ILeft
                {
                    void Trace([CallerMemberName] string member = "");
                }

                public interface IRight
                {
                    void Trace([CallerFilePath] string member = "");
                }

                [RpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        AssertNoInputCompilerErrors(compilation);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("inherited", StringComparison.OrdinalIgnoreCase) &&
            d.GetMessage().Contains("caller", StringComparison.OrdinalIgnoreCase));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("ICombined.", StringComparison.Ordinal));
    }

    private static void AssertNoInputCompilerErrors(Compilation compilation)
    {
        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }
}
