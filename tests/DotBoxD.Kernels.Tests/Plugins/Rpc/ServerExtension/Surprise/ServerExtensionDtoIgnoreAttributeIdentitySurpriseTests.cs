using System.Collections.Immutable;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoIgnoreAttributeIdentitySurpriseTests
{
    [Fact]
    public void Extern_aliased_lookalike_json_ignore_attribute_does_not_remove_dto_field()
    {
        var foreignJsonIgnore = CompileForeignJsonIgnoreReference();

        var package = PluginAnalyzerGeneratedPackageFactory.CreateWithReferences(
            """
            extern alias Lookalike;

            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed class Request
            {
                public int Included;

                [Lookalike::System.Text.Json.Serialization.JsonIgnore]
                public int ForeignMarked;
            }

            [ServerExtension("lookalike-ignore")]
            public sealed partial class LookalikeIgnoreKernel
            {
                public int Read(Request request, HookContext ctx) => request.Included;
            }
            """,
            "Sample.LookalikeIgnorePluginPackage",
            foreignJsonIgnore);

        var parameter = Assert.Single(Assert.Single(package.Module.Functions).Parameters);
        Assert.Equal(SandboxType.Record([SandboxType.I32, SandboxType.I32]), parameter.Type);
    }

    private static MetadataReference CompileForeignJsonIgnoreReference()
    {
        var compilation = CSharpCompilation.Create(
            "LookalikeJsonIgnore_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(
                """
                namespace System.Text.Json.Serialization;

                public sealed class JsonIgnoreAttribute : System.Attribute
                {
                }
                """)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        return MetadataReference.CreateFromImage(assembly.ToArray())
            .WithAliases(ImmutableArray.Create("Lookalike"));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
