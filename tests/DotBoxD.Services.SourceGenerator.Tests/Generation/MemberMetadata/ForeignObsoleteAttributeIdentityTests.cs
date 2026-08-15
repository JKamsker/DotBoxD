using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ForeignObsoleteAttributeIdentityTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ForeignObsoleteErrorAttribute_DoesNotRejectRpcService()
    {
        var foreignObsoleteReference = CompileForeignObsoleteReference();
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ForeignObsolete
            {
                [RpcService]
                [Foreign::System.Obsolete("foreign marker", true)]
                public interface IForeignMarkedService
                {
                    Task PingAsync();
                }
            }
            """, foreignObsoleteReference);

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(compilation)
            .GetRunResult();

        using var assertionScope = new FluentAssertions.Execution.AssertionScope();
        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        runResult.Results.Single().GeneratedSources.Should().Contain(g =>
            g.HintName == GeneratorTestHelper.HintName(
                "Regress.ForeignObsolete",
                "IForeignMarkedService",
                GeneratorTestHelper.GeneratedKind.Proxy));
    }

    private static MetadataReference CompileForeignObsoleteReference()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ForeignObsolete_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText("""
                namespace System
                {
                    public sealed class ObsoleteAttribute : Attribute
                    {
                        public ObsoleteAttribute(string message, bool error)
                        {
                        }
                    }
                }
                """, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        return MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
    }

    private static CSharpCompilation CreateCompilation(string source, MetadataReference foreignObsoleteReference) =>
        CSharpCompilation.Create(
            assemblyName: "ForeignObsoleteService_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(foreignObsoleteReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
