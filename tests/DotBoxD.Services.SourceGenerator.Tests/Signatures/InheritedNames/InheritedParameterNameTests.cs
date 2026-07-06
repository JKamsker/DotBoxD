using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public class InheritedParameterNameTests
{
    [Fact]
    public void DuplicateInheritedMethodsWithDifferentParameterNames_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedParameterNames
            {
                public interface ILeft
                {
                    int CountAsync(int left);
                }

                public interface IRight
                {
                    int CountAsync(int right);
                }

                [RpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("parameter name"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("ICombined."));
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
