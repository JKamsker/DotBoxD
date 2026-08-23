using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public class ExternAliasAsyncEnumerableReturnTypeTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ExternAliasedAsyncEnumerableReturnType_DoesNotUseBclStreamingGeneration()
    {
        var foreignAsyncEnumerableReference = CompileReference("""
            namespace System.Collections.Generic;

            public interface IAsyncEnumerable<out T>
            {
            }
            """, "Foreign.AsyncEnumerable");
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAliasAsyncEnumerableReturnType;

            [RpcService]
            public interface ICounter
            {
                Foreign::System.Collections.Generic.IAsyncEnumerable<int> CountAsync();
            }
            """, foreignAsyncEnumerableReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        var generatedSource = string.Join(
            "\n",
            runResult.Results.Single().GeneratedSources.Select(source => source.SourceText.ToString()));
        generatedSource.Should().Contain("Foreign::System.Collections.Generic.IAsyncEnumerable<int>");
        generatedSource.Should().NotContain("InvokeStreamAsync(\"ICounter\", \"CountAsync\"");

        var final = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        final.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty("the generator must preserve the foreign async-enumerable return type as a non-streaming return");
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
        MetadataReference foreignAsyncEnumerableReference) =>
        CSharpCompilation.Create(
            assemblyName: "ExternAliasAsyncEnumerableReturnType_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(foreignAsyncEnumerableReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
