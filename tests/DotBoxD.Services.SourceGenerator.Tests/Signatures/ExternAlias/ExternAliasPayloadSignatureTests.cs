using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class ExternAliasPayloadSignatureTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Theory]
    [InlineData("""
        extern alias Lib;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Services.Attributes;

        namespace Regress.ExternAliasPayloads;

        [RpcService]
        public interface IPayloads
        {
            Task<Lib::Contracts.Payload> GetAsync();

            Lib::Contracts.Payload Read();

            Task SendAsync(List<Lib::Contracts.Payload> items);

            ValueTask<Dictionary<string, Lib::Contracts.Payload[]>> MapAsync(Lib::Contracts.Payload? maybe);
        }
        """)]
    [InlineData("""
        extern alias Lib;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Services.Attributes;
        using Payload = Lib::Contracts.Payload;

        namespace Regress.ExternAliasPayloads;

        [RpcService]
        public interface IPayloads
        {
            Task<Payload> GetAsync();

            Payload Read();

            Task SendAsync(List<Payload> items);
        }
        """)]
    public void ExternAliasedPayloadTypes_GenerateCompilableSignatures(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ExternAliasPayloads_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(CompilePayloadReference()),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        runResult.Diagnostics.Should().BeEmpty();

        var final = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        final.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Should()
            .BeEmpty();

        var proxy = runResult.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("IPayloads.DotBoxDRpcProxy.g.cs", StringComparison.Ordinal))
            .ToString();
        proxy.Should().Contain("extern alias Lib;");
        proxy.Should().Contain("global::System.Threading.Tasks.Task<Lib::Contracts.Payload> GetAsync()");
        proxy.Should().Contain(
            "global::System.Threading.Tasks.Task SendAsync(global::System.Collections.Generic.List<Lib::Contracts.Payload> items");
    }

    private static MetadataReference CompilePayloadReference()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ExternAliasPayloadLib",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText(
                    "namespace Contracts { public sealed class Payload { public int Value { get; set; } } }",
                    s_parseOptions),
            },
            references: Basic.Reference.Assemblies.Net80.References.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        emit.Success.Should().BeTrue();
        return MetadataReference.CreateFromImage(assembly.ToArray())
            .WithAliases(ImmutableArray.Create("Lib"));
    }
}
