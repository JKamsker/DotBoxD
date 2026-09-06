using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionForeignListCollectionIdentitySurpriseTests
{
    [Fact]
    public void Framework_list_remains_a_supported_collection_shape()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(
            """
            using System.Collections.Generic;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;

            namespace Sample;

            [ServerExtension("framework-list")]
            public sealed partial class FrameworkListKernel
            {
                public int Read(List<int> values, HookContext ctx)
                    => values.Count;
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    [Fact]
    public void Extern_aliased_lookalike_list_reports_a_focused_unsupported_shape_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.DiagnosticsWithReferences(
            """
            extern alias Foreign;

            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;

            namespace Sample;

            [ServerExtension("foreign-list")]
            public sealed partial class ForeignListKernel
            {
                public int Read(Foreign::System.Collections.Generic.List<int> values, HookContext ctx)
                    => 0;
            }
            """,
            CompileForeignListReference());

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "System.Collections.Generic.List",
                              StringComparison.Ordinal));
    }

    private static MetadataReference CompileForeignListReference()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignCollections",
            [CSharpSyntaxTree.ParseText(
                """
                namespace System.Collections.Generic;

                public sealed class List<T>
                {
                }
                """)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        Assert.True(
            emit.Success,
            string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        return MetadataReference.CreateFromImage(assembly.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
