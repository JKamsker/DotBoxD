using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class ExternAliasDistinctMetadataParameterTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void AssemblyDistinctGenericArguments_PreserveTheirOwnAliasesInMetadata()
    {
        var leftReference = CompilePayloadReference("Left.Payload", "Left");
        var rightReference = CompilePayloadReference("Right.Payload", "Right");
        var compilation = CSharpCompilation.Create(
            assemblyName: "ExternAliasDistinctMetadata_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText("""
                    extern alias Left;
                    extern alias Right;

                    using DotBoxD.Services.Attributes;

                    namespace Regress.ExternAliasDistinctMetadata;

                    public sealed class Pair<TLeft, TRight>
                    {
                        public Pair(TLeft left, TRight right)
                        {
                            Left = left;
                            Right = right;
                        }

                        public TLeft Left { get; }

                        public TRight Right { get; }
                    }

                    [RpcService]
                    public interface IDistinctMetadataService
                    {
                        global::System.Threading.Tasks.Task SendAsync(
                            Pair<Left::Contracts.Payload, Right::Contracts.Payload> payload);
                    }
                    """, s_parseOptions),
            },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(leftReference)
                .Append(rightReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        var generated = runResult.Results.Single().GeneratedSources
            .Single(source => source.HintName == "DotBoxDGenerated.g.cs")
            .SourceText
            .ToString();

        generated.Should().Contain(
            "typeof(global::Regress.ExternAliasDistinctMetadata.Pair<Left::Contracts.Payload, Right::Contracts.Payload>)");

        compilation.AddSyntaxTrees(runResult.GeneratedTrees).GetDiagnostics()
            .Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    private static MetadataReference CompilePayloadReference(string assemblyName, string alias)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[]
            {
                CSharpSyntaxTree.ParseText("""
                    namespace Contracts;

                    public sealed class Payload
                    {
                        public int Value { get; set; }
                    }
                    """, s_parseOptions),
            },
            Basic.Reference.Assemblies.Net80.References.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        return MetadataReference.CreateFromImage(assembly.ToArray())
            .WithAliases(ImmutableArray.Create(alias));
    }
}
