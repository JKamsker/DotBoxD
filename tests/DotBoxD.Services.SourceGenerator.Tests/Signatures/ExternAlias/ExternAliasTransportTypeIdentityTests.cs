using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public class ExternAliasTransportTypeIdentityTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ExternAliasedStreamAndPipeTypes_DoNotUseBclTransportGeneration()
    {
        var foreignTransportReference = CompileReference("""
            namespace System.IO
            {
                public sealed class Stream
                {
                }
            }

            namespace System.IO.Pipelines
            {
                public sealed class Pipe
                {
                }
            }
            """, "Foreign.TransportTypes");
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAliasTransportTypeIdentity;

            [RpcService]
            public interface ITransport
            {
                Foreign::System.IO.Stream Download();

                void Upload(Foreign::System.IO.Pipelines.Pipe payload);
            }
            """, foreignTransportReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        var generatedSource = string.Join(
            "\n",
            runResult.Results.Single().GeneratedSources.Select(source => source.SourceText.ToString()));
        generatedSource.Should().Contain("Foreign::System.IO.Stream");
        generatedSource.Should().Contain("Foreign::System.IO.Pipelines.Pipe");
        generatedSource.Should().NotContain("InvokeStreamAsync(\"ITransport\", \"Download\"");

        var final = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        final.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty("foreign Stream and Pipe types are ordinary RPC payloads, not BCL transport shapes");
    }

    private static MetadataReference CompileReference(string source, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return MetadataReference.CreateFromImage(assembly.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        MetadataReference foreignTransportReference) =>
        CSharpCompilation.Create(
            assemblyName: "ExternAliasTransportTypeIdentity_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(foreignTransportReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
