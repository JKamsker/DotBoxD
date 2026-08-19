using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class NullableFlowAttributeIdentityTests
{
    [Fact]
    public void ForeignMaybeNullAttribute_IsNotPromotedToFrameworkAttribute()
    {
        var foreignAttributes = CompileForeignAttributes();
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableFlowAttributeIdentity
            {
                [RpcService]
                public interface IFlowService
                {
                    Task<string> EchoAsync(
                        [Foreign::System.Diagnostics.CodeAnalysis.MaybeNull] string value);
                }
            }
            """, foreignAttributes);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(compilation)
            .GetRunResult();

        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var generatedSources = runResult.Results.Single().GeneratedSources;
        generatedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.NullableFlowAttributeIdentity", "IFlowService", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText
            .ToString()
            .Should()
            .NotContain("global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute");
        generatedSources
            .Single(g => g.HintName.EndsWith("IFlowService.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText
            .ToString()
            .Should()
            .NotContain("global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute");
    }

    private static MetadataReference CompileForeignAttributes()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ForeignNullableFlowAttributes",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(ForeignAttributeSource) },
            references: Basic.Reference.Assemblies.Net80.References.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        MetadataReference foreignAttributes)
    {
        var references = Basic.Reference.Assemblies.Net80.References.All
            .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
            .Append(foreignAttributes);

        return CSharpCompilation.Create(
            assemblyName: "NullableFlowAttributeIdentity",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private const string ForeignAttributeSource = """
        namespace System.Diagnostics.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class MaybeNullAttribute : System.Attribute;
        }
        """;
}
