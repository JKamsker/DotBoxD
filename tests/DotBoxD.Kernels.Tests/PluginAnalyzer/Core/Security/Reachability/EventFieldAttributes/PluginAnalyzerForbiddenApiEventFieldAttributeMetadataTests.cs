using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEventFieldAttributeMetadataTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_type_reference_in_field_target_event_attribute_metadata()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using System;
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [AttributeUsage(AttributeTargets.All)]
                public sealed class UsesTypeAttribute(Type type) : Attribute
                {
                    public Type Type { get; } = type;
                }

                [Plugin("event-field-attribute-leak")]
                public sealed class EventFieldAttributeKernel : IEventKernel<string>
                {
                    [field: UsesType(typeof(System.IO.FileInfo))]
                    private event Action? Changed;

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Changed?.Invoke();
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Matches(@"System\.IO(\.FileInfo)?", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Does_not_report_benign_type_reference_in_field_target_event_attribute_metadata()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using System;
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [AttributeUsage(AttributeTargets.All)]
                public sealed class UsesTypeAttribute(Type type) : Attribute
                {
                    public Type Type { get; } = type;
                }

                [Plugin("event-field-attribute-clean")]
                public sealed class EventFieldAttributeKernel : IEventKernel<string>
                {
                    [field: UsesType(typeof(string))]
                    private event Action? Changed;

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Changed?.Invoke();
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerEventFieldAttributeMetadataTest",
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
