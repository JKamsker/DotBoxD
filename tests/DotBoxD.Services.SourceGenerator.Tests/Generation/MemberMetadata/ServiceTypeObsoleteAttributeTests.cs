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
        var asyncSibling = generated.Single(g =>
            g.HintName == "Regress_ServiceTypeObsolete_ILegacy.DotBoxDRpcAsync.g.cs");

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        var obsoleteDiagnostics = finalCompilation.GetDiagnostics()
            .Where(d => d.Id == "CS0618" &&
                d.Location.SourceTree is not null &&
                runResult.GeneratedTrees.Contains(d.Location.SourceTree))
            .Select(d => d.ToString())
            .ToArray();

        using (new AssertionScope())
        {
            proxy.SourceText.ToString().Should().Contain(
                """
                    [global::System.ObsoleteAttribute("Use INew")]
                    public sealed class LegacyProxy : global::Regress.ServiceTypeObsolete.ILegacy, global::Regress.ServiceTypeObsolete.ILegacyAsync
                """);
            asyncSibling.SourceText.ToString().Should().Contain(
                """
                    [global::System.ObsoleteAttribute("Use INew")]
                    public interface ILegacyAsync
                """);
            obsoleteDiagnostics.Should().BeEmpty();
        }
    }
}
