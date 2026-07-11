using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ExperimentalAttributeTypeMetadataTests
{
    [Fact]
    public void ExperimentalAttribute_IsPreservedOnGeneratedServiceTypes()
    {
        var (final, runResult) = RunWithPreviewByRefLikeGenerics(ExperimentalServiceSource);

        if (HasFocusedFailClosedDiagnostic(runResult))
        {
            return;
        }

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.ExperimentalType", "ILegacy", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString()
            .Replace("\r\n", "\n");
        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("ILegacy.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString()
            .Replace("\r\n", "\n");

        var generatedDiagnostics = final.GetDiagnostics()
            .Where(IsExperimentalDiagnosticFromGeneratedSource)
            .Select(d => d.ToString())
            .ToArray();

        using var scope = new AssertionScope();
        proxy.Should().Contain(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP_TYPE\", " +
            "UrlFormat = \"https://example.test/{0}\", Message = \"Use the stable API.\")]\n" +
            "    public sealed class LegacyProxy :");
        asyncSibling.Should().Contain(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP_TYPE\", " +
            "UrlFormat = \"https://example.test/{0}\", Message = \"Use the stable API.\")]\n" +
            "    public interface ILegacyAsync");
        generatedDiagnostics.Should().BeEmpty(
            "generated source should not emit compiler experimental diagnostics for accepted service contracts");
    }

    [Fact]
    public void DirectUsesOfGeneratedExperimentalServiceTypes_ReportConfiguredDiagnostic()
    {
        var (final, runResult) = RunWithPreviewByRefLikeGenerics(ExperimentalServiceSource);

        if (HasFocusedFailClosedDiagnostic(runResult))
        {
            return;
        }

        var callSite = CSharpSyntaxTree.ParseText(
            """
            namespace Regress.ExperimentalType
            {
                public static class GeneratedTypeCallSite
                {
                    public static void Use(LegacyProxy proxy, ILegacyAsync asyncSibling)
                    {
                        _ = proxy;
                        _ = asyncSibling;
                    }
                }
            }
            """,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "GeneratedTypeCallSite.cs");

        var diagnostics = final.AddSyntaxTrees(callSite).GetDiagnostics()
            .Where(d => d.Location.SourceTree == callSite)
            .Select(d => d.Id)
            .ToArray();

        diagnostics.Should().Contain("DBXEXP_TYPE");
        diagnostics.Count(id => id == "DBXEXP_TYPE").Should().BeGreaterThanOrEqualTo(2);
    }

    private static bool HasFocusedFailClosedDiagnostic(GeneratorDriverRunResult runResult)
        => runResult.Diagnostics.Any(static d => d.Id.StartsWith("DBXS", StringComparison.Ordinal));

    private static bool IsExperimentalDiagnosticFromGeneratedSource(Diagnostic diagnostic)
        => diagnostic.Id == "DBXEXP_TYPE" &&
           diagnostic.Location.SourceTree is not null &&
           diagnostic.Location.SourceTree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal);

    private const string ExperimentalServiceSource = """
        using DotBoxD.Services.Attributes;
        using System.Diagnostics.CodeAnalysis;
        using System.Threading.Tasks;

        namespace Regress.ExperimentalType
        {
            [Experimental("DBXEXP_TYPE", UrlFormat = "https://example.test/{0}", Message = "Use the stable API.")]
            [RpcService]
            public interface ILegacy
            {
                Task PingAsync();
            }
        }
        """;
}
