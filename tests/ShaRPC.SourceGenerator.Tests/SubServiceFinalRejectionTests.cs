using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class SubServiceFinalRejectionTests
{
    [Fact]
    public void SubServiceRejectedByAsyncSiblingCollision_DoesNotStaleRejectRestoredParentService()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.FinalRejectedSubService
            {
                public interface ISubAsync
                {
                }

                public interface IRootAsync
                {
                }

                [ShaRpcService]
                public interface ISub
                {
                    int Count();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<ISub> OpenAsync();
                }

                [ShaRpcService]
                public interface IParent
                {
                    Task<IRoot> GetRootAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("generated async sibling interface 'ISubAsync'"));
        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("generated async sibling interface 'IRootAsync'"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("IRoot.OpenAsync"));
        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("IParent.GetRootAsync"));

        var generated = runResult.Results.Single().GeneratedSources;
        var rootProxy = generated
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        rootProxy.Should().Contain("throw new global::System.NotSupportedException");
        rootProxy.Should().NotContain("new global::Regress.FinalRejectedSubService.SubProxy");

        var rootDispatcher = generated
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        rootDispatcher.Should().NotContain("case \"OpenAsync\":");

        var parentProxy = generated
            .Single(g => g.HintName.EndsWith("IParent.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        parentProxy.Should().Contain("new global::Regress.FinalRejectedSubService.RootProxy");
        parentProxy.Should().NotContain("ShaRPC cannot marshal 'GetRootAsync'");
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
