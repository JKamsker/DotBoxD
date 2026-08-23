using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public class ExternAliasInheritedParameterTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void InheritedAssemblyDistinctParameterOverloads_CompileOrRejectWithFocusedDiagnostic()
    {
        var leftPayloadReference = CompileReference("LeftPayload");
        var rightPayloadReference = CompileReference("RightPayload")
            .WithAliases(ImmutableArray.Create("Lookalike"));
        var compilation = CreateCompilation("""
            extern alias Lookalike;

            using System.Threading.Tasks;
            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAliasInheritedParameters;

            public interface ILeft
            {
                Task SendAsync(Contracts.Payload payload);
            }

            public interface IRight
            {
                Task SendAsync(Lookalike::Contracts.Payload payload);
            }

            [RpcService]
            public interface ICombined : ILeft, IRight
            {
            }
            """, leftPayloadReference, rightPayloadReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var final = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        var finalErrors = final.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

        finalErrors.Should().BeEmpty("the generator must not emit invalid source");

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("inherited", StringComparison.Ordinal) &&
            d.GetMessage().Contains("different assemblies", StringComparison.Ordinal));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("ICombined.", StringComparison.Ordinal));
    }

    [Fact]
    public void InheritedAssemblyDistinctFunctionPointerParameters_ReportDistinctAssemblyDiagnostic()
    {
        var leftPayloadReference = CompileReference("LeftPayload");
        var rightPayloadReference = CompileReference("RightPayload")
            .WithAliases(ImmutableArray.Create("Lookalike"));
        var compilation = CreateCompilation("""
            extern alias Lookalike;

            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAlias;

            [RpcService]
            public unsafe interface ILeft
            {
                void Send(delegate*<Contracts.Payload, void> callback);
            }

            [RpcService]
            public unsafe interface IRight
            {
                void Send(delegate*<Lookalike::Contracts.Payload, void> callback);
            }

            [RpcService]
            public interface ICombined : ILeft, IRight
            {
            }
            """, leftPayloadReference, rightPayloadReference, allowUnsafe: true);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("function pointer type"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("different assemblies"));
    }

    private static MetadataReference CompileReference(string assemblyName)
    {
        const string source = """
            namespace Contracts;

            public sealed class Payload
            {
                public int Value { get; set; }
            }
            """;
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

        return MetadataReference.CreateFromImage(assembly.ToArray());
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        MetadataReference leftPayloadReference,
        MetadataReference rightPayloadReference,
        bool allowUnsafe = false) =>
        CSharpCompilation.Create(
            assemblyName: "ExternAliasInheritedParameter_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(leftPayloadReference)
                .Append(rightPayloadReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));
}
