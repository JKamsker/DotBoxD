using FluentAssertions;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class InheritedDefaultValueConflictTests
{
    [Fact]
    public void DuplicateInheritedMethodsWithDifferentOptionalDefaults_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedDefaultValues
            {
                public interface ILeft
                {
                    int Count(int value = 1);
                }

                public interface IRight
                {
                    int Count(int value = 2);
                }

                [RpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(compilation)
            .GetRunResult();

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "DBXS003" &&
            d.GetMessage().Contains("inherited", StringComparison.OrdinalIgnoreCase) &&
            d.GetMessage().Contains("optional", StringComparison.OrdinalIgnoreCase) &&
            d.GetMessage().Contains("default", StringComparison.OrdinalIgnoreCase));

        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("ICombined.", StringComparison.Ordinal));
    }
}
