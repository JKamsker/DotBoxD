using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ExperimentalAttributeMetadataTests
{
    [Fact]
    public void ExperimentalAttributes_ArePreservedOnGeneratedServiceSurface()
    {
        var (_, runResult) = RunWithPreviewByRefLikeGenerics(ExperimentalServiceSource);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.ExperimentalSurface", "IRoot", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString()
            .Replace("\r\n", "\n");
        proxy.Should().Contain(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP001\", UrlFormat = \"https://example.test/{0}\", Message = \"Method is still settling\")]\n" +
            "        public global::System.Threading.Tasks.Task ExperimentalAsync()");
        proxy.Should().Contain(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP002\")]\n" +
            "        public global::Regress.ExperimentalSurface.IChild Child =>");

        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString()
            .Replace("\r\n", "\n");
        asyncSibling.Should().Contain(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP001\", UrlFormat = \"https://example.test/{0}\", Message = \"Method is still settling\")]\n" +
            "        global::System.Threading.Tasks.Task ExperimentalAsync(");
    }

    [Fact]
    public void DirectCallsToGeneratedExperimentalSurface_ReportConfiguredDiagnostics()
    {
        var (final, _) = RunWithPreviewByRefLikeGenerics(ExperimentalServiceSource);

        var callSite = CSharpSyntaxTree.ParseText(
            """
            namespace Regress.ExperimentalSurface
            {
                public static class GeneratedSurfaceCallSite
                {
                    public static void Call(RootProxy proxy, IRootAsync asyncRoot)
                    {
                        _ = proxy.ExperimentalAsync();
                        _ = asyncRoot.ExperimentalAsync();
                        _ = proxy.Child;
                    }
                }
            }
            """,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "GeneratedSurfaceCallSite.cs");

        var diagnostics = final.AddSyntaxTrees(callSite).GetDiagnostics()
            .Where(d => d.Location.SourceTree == callSite)
            .Select(d => d.Id)
            .ToArray();

        diagnostics.Should().Contain("DBXEXP001");
        diagnostics.Should().Contain("DBXEXP002");
        diagnostics.Count(id => id == "DBXEXP001").Should().Be(2);
    }

    private const string ExperimentalServiceSource = """
        using DotBoxD.Services.Attributes;
        using System.Diagnostics.CodeAnalysis;
        using System.Threading.Tasks;

        namespace Regress.ExperimentalSurface
        {
            [RpcService]
            public interface IChild
            {
                Task PingAsync();
            }

            [RpcService]
            public interface IRoot
            {
                [Experimental("DBXEXP001", UrlFormat = "https://example.test/{0}", Message = "Method is still settling")]
                Task ExperimentalAsync();

                [Experimental("DBXEXP002")]
                IChild Child { get; }
            }
        }
        """;
}
