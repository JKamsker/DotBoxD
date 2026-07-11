using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ServiceTypeObsoleteAttributeTests
{
    [Fact]
    public void ObsoleteServiceInterface_PreservesAttributeOnGeneratedPublicTypes()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace Regress.ServiceTypeObsolete
            {
                [RpcService]
                [Obsolete("Use INew")]
                public interface ILegacy
                {
                    Task PingAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var generated = runResult.Results.Single().GeneratedSources;

        var proxy = generated.Single(g => g.HintName == GeneratorTestHelper.HintName(
            "Regress.ServiceTypeObsolete",
            "ILegacy",
            GeneratorTestHelper.GeneratedKind.Proxy));
        var asyncSibling = generated.Single(g => g.HintName == GeneratorTestHelper.HintName(
            "Regress.ServiceTypeObsolete",
            "ILegacy",
            GeneratorTestHelper.GeneratedKind.Async));

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        var obsoleteDiagnostics = finalCompilation.GetDiagnostics()
            .Where(d => d.Id == "CS0618" &&
                d.Location.SourceTree is not null &&
                runResult.GeneratedTrees.Contains(d.Location.SourceTree))
            .Select(d => d.ToString())
            .ToArray();

        using (new AssertionScope())
        {
            AssertTypeHasObsoleteAttribute(
                proxy.SourceText.ToString(),
                "public sealed class LegacyProxy : global::Regress.ServiceTypeObsolete.ILegacy, global::Regress.ServiceTypeObsolete.ILegacyAsync",
                "Use INew");
            AssertTypeHasObsoleteAttribute(
                asyncSibling.SourceText.ToString(),
                "public interface ILegacyAsync",
                "Use INew");
            obsoleteDiagnostics.Should().BeEmpty();
        }
    }

    [Fact]
    public void ObsoleteErrorServiceInterface_IsRejectedBeforeGeneratedReferences()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace Regress.ServiceTypeObsolete
            {
                [RpcService]
                [Obsolete("Use INew", true)]
                public interface ILegacy
                {
                    Task PingAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var diagnostic = runResult.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS003").Subject;
        diagnostic.GetMessage().Should().Contain("[Obsolete(..., true)]");
        runResult.Results.Single().GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void ObsoleteServiceInterface_PreservesDiagnosticMetadataOnGeneratedPublicTypes()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace Regress.ServiceTypeObsolete
            {
                [RpcService]
                [Obsolete("Use INew", DiagnosticId = "DBXS999", UrlFormat = "https://example.test/obsolete")]
                public interface ILegacy
                {
                    Task PingAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;

        var proxy = generated.Single(g => g.HintName == GeneratorTestHelper.HintName(
            "Regress.ServiceTypeObsolete",
            "ILegacy",
            GeneratorTestHelper.GeneratedKind.Proxy));
        var asyncSibling = generated.Single(g => g.HintName == GeneratorTestHelper.HintName(
            "Regress.ServiceTypeObsolete",
            "ILegacy",
            GeneratorTestHelper.GeneratedKind.Async));
        const string expected =
            "[global::System.ObsoleteAttribute(\"Use INew\", DiagnosticId = \"DBXS999\", UrlFormat = \"https://example.test/obsolete\")]";

        using (new AssertionScope())
        {
            AssertTypeHasAttribute(
                proxy.SourceText.ToString(),
                "public sealed class LegacyProxy : global::Regress.ServiceTypeObsolete.ILegacy, global::Regress.ServiceTypeObsolete.ILegacyAsync",
                expected);
            AssertTypeHasAttribute(
                asyncSibling.SourceText.ToString(),
                "public interface ILegacyAsync",
                expected);
        }
    }

    private static void AssertTypeHasObsoleteAttribute(string source, string declaration, string message)
    {
        var declarationIndex = source.IndexOf(declaration, StringComparison.Ordinal);
        declarationIndex.Should().BeGreaterThanOrEqualTo(0);

        var declarationLineStart = FindLineStart(source, declarationIndex);
        var previousLine = GetPreviousLine(source, declarationLineStart);
        previousLine.Should().Be($"[global::System.ObsoleteAttribute(\"{message}\")]");
    }

    private static void AssertTypeHasAttribute(string source, string declaration, string expected)
    {
        var declarationIndex = source.IndexOf(declaration, StringComparison.Ordinal);
        declarationIndex.Should().BeGreaterThanOrEqualTo(0);

        var declarationLineStart = FindLineStart(source, declarationIndex);
        var previousLine = GetPreviousLine(source, declarationLineStart);
        previousLine.Should().Be(expected);
    }

    private static int FindLineStart(string source, int index)
    {
        var previousNewLine = source.LastIndexOf('\n', index);
        return previousNewLine < 0 ? 0 : previousNewLine + 1;
    }

    private static string GetPreviousLine(string source, int lineStart)
    {
        var previousLineEnd = lineStart;
        while (previousLineEnd > 0 && IsLineBreak(source[previousLineEnd - 1]))
        {
            previousLineEnd--;
        }

        if (previousLineEnd == 0)
        {
            return string.Empty;
        }

        var previousLineStart = source.LastIndexOf('\n', previousLineEnd - 1);
        previousLineStart = previousLineStart < 0 ? 0 : previousLineStart + 1;
        return source.Substring(previousLineStart, previousLineEnd - previousLineStart).Trim();
    }

    private static bool IsLineBreak(char value) =>
        value is '\r' or '\n';
}
