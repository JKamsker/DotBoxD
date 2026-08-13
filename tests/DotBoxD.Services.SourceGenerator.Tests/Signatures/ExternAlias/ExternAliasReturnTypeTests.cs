using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public class ExternAliasReturnTypeTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ExternAliasedTaskReturnType_PreservesSymbolIdentityInGeneratedCode()
    {
        var lookalikeTaskReference = CompileReference("""
            namespace System.Threading.Tasks;

            public sealed class Task<TResult>
            {
            }
            """, "System.Runtime");
        var compilation = CreateCompilation("""
            extern alias Lookalike;

            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAliasReturnType;

            [RpcService]
            public interface ICounter
            {
                Lookalike::System.Threading.Tasks.Task<int> CountAsync();
            }
            """, lookalikeTaskReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var final = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        final.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty("the generator must preserve the extern-aliased Task<TResult> identity or reject it with a focused diagnostic");
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
            .WithAliases(ImmutableArray.Create("Lookalike"));
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        MetadataReference lookalikeTaskReference) =>
        CSharpCompilation.Create(
            assemblyName: "ExternAliasReturnType_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(lookalikeTaskReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
