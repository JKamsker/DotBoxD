using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEventAccessorAttributeMetadataTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_host_type_in_custom_event_accessor_attribute_metadata()
    {
        const string source = """
            #nullable enable

            namespace Sample
            {
                using System;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [AttributeUsage(AttributeTargets.Method)]
                public sealed class UsesTypeAttribute : Attribute
                {
                    public UsesTypeAttribute(Type type) { }
                }

                [Plugin("event-accessor-metadata-leak")]
                public sealed class EventAccessorMetadataKernel : IEventKernel<string>
                {
                    [method: UsesType(typeof(System.IO.FileInfo))]
                    private event Action? Changed
                    {
                        add { }
                        remove { }
                    }

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Changed += OnChanged;
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }

                    private static void OnChanged() { }
                }
            }
            """;

        var diagnostics = await AnalyzeCompilerCleanSourceAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.FileInfo", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Direct_forbidden_call_control_still_reports_dbxk001()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("event-accessor-direct-control")]
                public sealed class DirectForbiddenCallKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeCompilerCleanSourceAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Benign_custom_event_accessor_attribute_metadata_stays_clean()
    {
        const string source = """
            #nullable enable

            namespace Sample
            {
                using System;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [AttributeUsage(AttributeTargets.Method)]
                public sealed class UsesTypeAttribute : Attribute
                {
                    public UsesTypeAttribute(Type type) { }
                }

                [Plugin("event-accessor-benign-metadata")]
                public sealed class BenignEventAccessorMetadataKernel : IEventKernel<string>
                {
                    [method: UsesType(typeof(string))]
                    private event Action? Changed
                    {
                        add { }
                        remove { }
                    }

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Changed += OnChanged;
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }

                    private static void OnChanged() { }
                }
            }
            """;

        var diagnostics = await AnalyzeCompilerCleanSourceAsync(source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeCompilerCleanSourceAsync(string source)
    {
        var compilation = CreateCompilation(source);
        AssertNoCompilerErrors(compilation.GetDiagnostics());

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static void AssertNoCompilerErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal) &&
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(errors);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerEventAccessorAttributeMetadataTest",
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
