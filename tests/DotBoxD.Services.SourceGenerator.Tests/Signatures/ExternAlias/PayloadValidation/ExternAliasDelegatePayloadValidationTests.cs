using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class ExternAliasDelegatePayloadValidationTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ExternAliasedDelegateDto_IsValidRpcPayload()
    {
        var foreignDelegateReference = CompileReference("""
            namespace System;

            public sealed class Delegate
            {
                public Delegate(int value) => Value = value;

                public int Value { get; }
            }
            """, "Foreign.DelegateDto");
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAliasDelegatePayload;

            [RpcService]
            public interface IDelegatePayloadService
            {
                global::System.Threading.Tasks.Task<int> SendAsync(Foreign::System.Delegate payload);
            }
            """, foreignDelegateReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Id == "DBXS002").Should().BeEmpty(
            "an assembly-distinct System.Delegate DTO is an ordinary reconstructible payload");

        runResult.Results.Single().GeneratedSources
            .Single(source => source.HintName.EndsWith("IDelegatePayloadService.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString()
            .Should()
            .Contain("case \"SendAsync\":");

        var final = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        final.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty("generated code must preserve extern-aliased DTO identities");
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
        MetadataReference foreignDelegateReference) =>
        CSharpCompilation.Create(
            assemblyName: "ExternAliasDelegatePayload_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(foreignDelegateReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
