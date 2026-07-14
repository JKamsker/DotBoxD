using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiPropertyAccessorAttributeMetadataTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Allows_benign_property_accessor_attribute_metadata()
    {
        var diagnostics = await AnalyzeAsync(Source("string"));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_forbidden_payload_type_in_property_accessor_attribute_metadata()
    {
        var diagnostics = await AnalyzeAsync(Source("System.IO.FileInfo"));

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.Matches(@"System\.IO(\.FileInfo)?", message);
        Assert.DoesNotContain("System.Type", message, StringComparison.Ordinal);
    }

    private static string Source(string metadataType)
        => $$"""
            namespace Sample
            {
                using System;
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [AttributeUsage(AttributeTargets.Method)]
                public sealed class UsesTypeAttribute(Type type) : Attribute
                {
                    public Type Type { get; } = type;
                }

                [Plugin("property-accessor-attribute-metadata")]
                public sealed class PropertyAccessorAttributeKernel : IEventKernel<string>
                {
                    [get: UsesType(typeof({{metadataType}}))]
                    private string Helper => "ok";

                    public bool ShouldHandle(string e, HookContext context) => Helper.Length > 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var compilerErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilerErrors);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerPropertyAccessorAttributeMetadataTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
