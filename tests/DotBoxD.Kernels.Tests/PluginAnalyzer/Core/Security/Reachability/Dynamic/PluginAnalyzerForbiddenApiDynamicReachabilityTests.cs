using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiDynamicReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_helper_method_reached_through_dynamic_dispatch()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public bool Danger()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return true;
                    }
                }

                [Plugin("dynamic-dispatch-leak")]
                public sealed class DynamicDispatchKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        dynamic helper = new Helper();
                        return helper.Danger();
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "return helper.Danger();");
    }

    [Fact]
    public async Task Reports_forbidden_helper_property_reached_through_dynamic_member_access()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public bool Danger
                    {
                        get
                        {
                            _ = System.IO.File.ReadAllText("/x");
                            return true;
                        }
                    }
                }

                [Plugin("dynamic-property-leak")]
                public sealed class DynamicPropertyKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        dynamic helper = new Helper();
                        return helper.Danger;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "return helper.Danger;");
    }

    [Fact]
    public async Task Reports_forbidden_helper_indexer_reached_through_dynamic_indexer_access()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public bool this[int index]
                    {
                        get
                        {
                            _ = System.IO.File.ReadAllText("/x");
                            return index == 0;
                        }
                    }
                }

                [Plugin("dynamic-indexer-leak")]
                public sealed class DynamicIndexerKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        dynamic helper = new Helper();
                        return helper[0];
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "return helper[0];");
    }

    [Fact]
    public async Task Reports_direct_forbidden_call_control()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("direct-forbidden-call")]
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

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "_ = System.IO.File.ReadAllText(\"/x\");");
    }

    private static void AssertSingleForbiddenDiagnosticAt(
        string source,
        ImmutableArray<Diagnostic> diagnostics,
        string expectedLine)
    {
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);

        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        var actualLine = source.Split('\n')[position.Line].Trim();
        Assert.Equal(expectedLine, actualLine);
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
            "DotBoxDPluginAnalyzerDynamicReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location))
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
