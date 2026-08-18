using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ExperimentalAttributeIdentityTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ExternAliasedExperimentalAttribute_IsNotPromotedToBclMetadata()
    {
        var foreignExperimentalReference = CompileReference("""
            namespace System.Diagnostics.CodeAnalysis;

            [System.AttributeUsage(System.AttributeTargets.Interface)]
            public sealed class ExperimentalAttribute : System.Attribute
            {
                public ExperimentalAttribute(string diagnosticId)
                {
                }
            }
            """, "Foreign.Experimental");
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ExperimentalIdentity;

            [Foreign::System.Diagnostics.CodeAnalysis.Experimental("FOREIGN_EXP")]
            [RpcService]
            public interface IForeignExperimentalService
            {
                Task PingAsync();
            }
            """, foreignExperimentalReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var generatedSources = runResult.Results.Single().GeneratedSources
            .Select(static source => source.SourceText.ToString())
            .ToArray();

        generatedSources.Should().NotContain(source =>
            source.Contains("global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute", StringComparison.Ordinal));
        generatedSources.Should().NotContain(source =>
            source.Contains("FOREIGN_EXP", StringComparison.Ordinal));
    }

    [Fact]
    public void BclExperimentalAttribute_IsPreservedOnGeneratedServiceTypes()
    {
        var compilation = GeneratorTestHelper.CreateCompilation("""
            using DotBoxD.Services.Attributes;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;

            namespace Regress.ExperimentalIdentity;

            [Experimental("DBXEXP_REAL")]
            [RpcService]
            public interface IRealExperimentalService
            {
                Task PingAsync();
            }
            """);

        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        var generatedSources = runResult.Results.Single().GeneratedSources
            .Select(static source => source.SourceText.ToString())
            .ToArray();

        generatedSources.Should().Contain(source =>
            source.Contains(
                "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP_REAL\")]",
                StringComparison.Ordinal));
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
        MetadataReference foreignExperimentalReference) =>
        CSharpCompilation.Create(
            assemblyName: "ExperimentalAttributeIdentity_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(foreignExperimentalReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
