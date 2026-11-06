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

    [Fact]
    public void ExternAliasedEnumDefaultsAndSubServices_GenerateCompilableCode()
    {
        const string source = """
            extern alias Lib;
            using System.Runtime.InteropServices;
            using System.Threading.Tasks;
            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAliasMembers;

            [RpcService]
            public interface IAliasedMembers
            {
                Lib::Contracts.ISub Sub { get; }

                Task PaintAsync(Lib::Contracts.Color color = Lib::Contracts.Color.Red);

                Task PaintMaybeAsync(Lib::Contracts.Color? color = Lib::Contracts.Color.Green);

                Task PaintMetadataAsync([Optional, DefaultParameterValue(Lib::Contracts.Color.Red)] Lib::Contracts.Color color);

                Task PaintBeforeAsync([Optional, DefaultParameterValue(Lib::Contracts.Color.Green)] Lib::Contracts.Color color, int last);

                Task<Lib::Contracts.ISub> GetSubAsync();

                Lib::Contracts.ISub ReadSub();
            }
            """;
        var compilation = CSharpCompilation.Create(
            assemblyName: "ExternAliasMembers_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: GeneratorTestHelper.CreateCompilation().References
                .Append(CompileGeneratedContractsReference()),
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

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().NotContain("global::Contracts.");
        var proxy = runResult.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("IAliasedMembers.DotBoxDRpcProxy.g.cs", StringComparison.Ordinal))
            .ToString();
        proxy.Should().Contain("Lib::Contracts.Color color = (Lib::Contracts.Color)1");
        proxy.Should().Contain("new Lib::Contracts.SubProxy(");
    }

    private static MetadataReference CompileGeneratedContractsReference()
    {
        var compilation = GeneratorTestHelper.CreateCompilation("""
            using System.Threading.Tasks;
            using DotBoxD.Services.Attributes;

            namespace Contracts
            {
                public enum Color
                {
                    Red = 1,
                    Green = 2,
                }

                [RpcService]
                public interface ISub
                {
                    Task PingAsync();
                }
            }
            """);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        var contracts = compilation.AddSyntaxTrees(runResult.GeneratedTrees);

        using var assembly = new MemoryStream();
        var emit = contracts.Emit(assembly);
        emit.Success.Should().BeTrue(string.Join(
            Environment.NewLine,
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(assembly.ToArray())
            .WithAliases(ImmutableArray.Create("Lib"));
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
