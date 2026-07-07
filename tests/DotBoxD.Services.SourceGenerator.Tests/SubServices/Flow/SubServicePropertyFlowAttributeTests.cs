using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices.Flow;

public class SubServicePropertyFlowAttributeTests
{
    [Fact]
    public void SubServicePropertyFlowAttributes_ArePreservedOnProxyProperties()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;

            namespace Regress.SubServicePropertyFlow
            {
                [RpcService]
                public interface IChild
                {
                    Task<int> CountAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    [MaybeNull]
                    IChild MaybeChild { get; }

                    [NotNull]
                    IChild FlowNotNullChild { get; }
                }
            }
            """;

        var (final, runResult) = Run(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString()
            .Replace("\r\n", "\n");
        proxy.Should().Contain(
            """
                    [global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute]
                    public global::Regress.SubServicePropertyFlow.IChild MaybeChild =>
            """);
        proxy.Should().Contain(
            """
                    [global::System.Diagnostics.CodeAnalysis.NotNullAttribute]
                    public global::Regress.SubServicePropertyFlow.IChild FlowNotNullChild =>
            """);
    }

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
