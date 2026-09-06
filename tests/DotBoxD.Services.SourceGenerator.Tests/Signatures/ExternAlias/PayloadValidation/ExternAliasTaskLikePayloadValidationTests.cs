using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class ExternAliasTaskLikePayloadValidationTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ExternAliasedTaskDto_IsValidRpcPayloadMember()
    {
        var foreignTaskLikeReference = CompileReference("""
            namespace System.Threading.Tasks;

            public sealed class Task<TResult>
            {
                public Task(TResult value) => Value = value;

                public TResult Value { get; }
            }

            """, "Foreign.TaskLikeDtos");
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAliasTaskLikePayload;

            public sealed class Envelope
            {
                public Envelope(Foreign::System.Threading.Tasks.Task<int> work) => Work = work;

                public Foreign::System.Threading.Tasks.Task<int> Work { get; }
            }

            [RpcService]
            public interface IEnvelopePayloadService
            {
                global::System.Threading.Tasks.Task<int> SendAsync(Envelope payload);
            }

            """, foreignTaskLikeReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Id == "DBXS002").Should().BeEmpty(
            "an assembly-distinct Task DTO is an ordinary reconstructible payload");

        var generatedSources = runResult.Results.Single().GeneratedSources;
        generatedSources
            .Single(source => source.HintName.EndsWith("IEnvelopePayloadService.DotBoxDRpcDispatcher.g.cs"))
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

    [Fact]
    public void ExternAliasedValueTaskDto_IsValidNestedGenericRpcPayload()
    {
        var foreignTaskLikeReference = CompileReference("""
            namespace System.Threading.Tasks;

            public sealed class ValueTask<TResult>
            {
                public ValueTask(TResult value) => Value = value;

                public TResult Value { get; }
            }
            """, "Foreign.ValueTaskDto");
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAliasTaskLikePayload;

            public sealed class Box<T>
            {
                public Box(T value) => Value = value;

                public T Value { get; }
            }

            [RpcService]
            public interface IGenericPayloadService
            {
                global::System.Threading.Tasks.Task<int> SendNestedAsync(
                    Box<Foreign::System.Threading.Tasks.ValueTask<int>> payload);
            }
            """, foreignTaskLikeReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Id == "DBXS002").Should().BeEmpty(
            "an assembly-distinct ValueTask DTO nested in a generic payload is reconstructible");

        runResult.Results.Single().GeneratedSources
            .Single(source => source.HintName.EndsWith("IGenericPayloadService.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString()
            .Should()
            .Contain("case \"SendNestedAsync\":");

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
        MetadataReference foreignTaskLikeReference) =>
        CSharpCompilation.Create(
            assemblyName: "ExternAliasTaskLikePayload_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(foreignTaskLikeReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
