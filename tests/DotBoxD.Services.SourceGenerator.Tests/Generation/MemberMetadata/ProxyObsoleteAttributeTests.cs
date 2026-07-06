using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation.MemberMetadata;

public sealed class ProxyObsoleteAttributeTests
{
    [Fact]
    public void ProxyMembers_PreserveObsoleteAttributesFromServiceContracts()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace Regress.ObsoleteProxyMembers
            {
                [RpcService]
                public interface IChild
                {
                    Task<int> NewAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    [Obsolete("Use NewAsync")]
                    Task LegacyAsync();

                    [Obsolete("Use Child2")]
                    IChild Child { get; }

                    IChild Child2 { get; }
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.ObsoleteProxyMembers",
                "IRoot",
                GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();

        proxy.Should().Contain(
            "[global::System.ObsoleteAttribute(\"Use NewAsync\")]\n" +
            "        public global::System.Threading.Tasks.Task LegacyAsync()");
        proxy.Should().Contain(
            "[global::System.ObsoleteAttribute(\"Use NewAsync\")]\n" +
            "        public global::System.Threading.Tasks.Task LegacyAsync(global::System.Threading.CancellationToken ct = default)");
        proxy.Should().Contain(
            "[global::System.ObsoleteAttribute(\"Use Child2\")]\n" +
            "        public global::Regress.ObsoleteProxyMembers.IChild Child =>");
        AssertChild2IsNotObsolete(proxy);

        var asyncSibling = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();

        asyncSibling.Should().Contain(
            "[global::System.ObsoleteAttribute(\"Use NewAsync\")]\n" +
            "        global::System.Threading.Tasks.Task LegacyAsync(global::System.Threading.CancellationToken ct = default);");
    }

    private static void AssertChild2IsNotObsolete(string proxy)
    {
        const string childDeclaration =
            "        public global::Regress.ObsoleteProxyMembers.IChild Child =>";
        const string child2Declaration =
            "        public global::Regress.ObsoleteProxyMembers.IChild Child2 =>";
        var childIndex = proxy.IndexOf(childDeclaration, StringComparison.Ordinal);
        var child2Index = proxy.IndexOf(child2Declaration, StringComparison.Ordinal);
        childIndex.Should().BeGreaterThanOrEqualTo(0);
        child2Index.Should().BeGreaterThanOrEqualTo(0);
        child2Index.Should().BeGreaterThan(childIndex);

        var childDeclarationEnd = proxy.IndexOf('\n', childIndex);
        childDeclarationEnd.Should().BeGreaterThanOrEqualTo(0);

        var child2Prefix = proxy.Substring(
            childDeclarationEnd + 1,
            child2Index - childDeclarationEnd - 1);
        child2Prefix.Should().NotContain("ObsoleteAttribute");
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
