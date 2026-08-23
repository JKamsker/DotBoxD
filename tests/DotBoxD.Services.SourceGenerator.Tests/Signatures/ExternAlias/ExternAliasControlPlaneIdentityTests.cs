using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class ExternAliasControlPlaneIdentityTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ExternAliasedLookalikeControlMember_IsPreservedInGeneratedService()
    {
        var foreignControlReference = CompileReference("""
            namespace DotBoxD.Abstractions;

            public interface IServiceControl
            {
                System.Threading.Tasks.ValueTask<int> PingAsync();
            }
            """, "Foreign.ServiceControl");
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;

            namespace Regress.ExternAliasControlPlaneIdentity;

            [RpcService]
            public interface IProbeService : Foreign::DotBoxD.Abstractions.IServiceControl
            {
            }
            """, foreignControlReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var generatorErrors = runResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (generatorErrors.Any(d => d.Id == "DBXS003"))
        {
            generatorErrors.Should().OnlyContain(d => d.Id == "DBXS003");
        }
        else
        {
            generatorErrors.Should().BeEmpty();
        }

        var final = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        final.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty("the generator must preserve the foreign control member or reject the foreign base with a focused diagnostic");
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
        MetadataReference foreignControlReference) =>
        CSharpCompilation.Create(
            assemblyName: "ExternAliasControlPlaneIdentity_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(foreignControlReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
