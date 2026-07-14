using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiDeclarationTypeReferenceTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_host_type_used_only_as_plugin_base_class()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [Plugin("base-list-type-leak")]
                public sealed class BaseListTypeKernel : System.IO.FileSystemInfo, IEventKernel<string>
                {
                    public override bool Exists => false;

                    public override string Name => "base-list-type-leak";

                    public override void Delete() { }

                    public bool ShouldHandle(string e, HookContext context) => e.Length >= 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        AssertSingleForbiddenDiagnostic(diagnostics);
    }

    [Fact]
    public async Task Reports_forbidden_host_type_used_only_as_plugin_type_parameter_constraint()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [Plugin("constraint-type-leak")]
                public sealed class ConstraintTypeKernel<T> : IEventKernel<string>
                    where T : System.IO.FileSystemInfo
                {
                    public bool ShouldHandle(string e, HookContext context) => e.Length >= 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        AssertSingleForbiddenDiagnostic(diagnostics);
    }

    [Fact]
    public async Task Does_not_report_safe_base_list_or_constraint_declarations()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public abstract class SafePluginBase
                {
                    protected static bool IsReady(string value) => value.Length >= 0;
                }

                public interface ISafeConstraint
                {
                }

                [Plugin("safe-declaration-shape")]
                public sealed class SafeDeclarationKernel<T> : SafePluginBase, IEventKernel<string>
                    where T : ISafeConstraint
                {
                    public bool ShouldHandle(string e, HookContext context) => IsReady(e);

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.Empty(diagnostics.Where(d => d.Id == "DBXK001"));
    }

    private static void AssertSingleForbiddenDiagnostic(ImmutableArray<Diagnostic> diagnostics)
    {
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.FileSystemInfo", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

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
            "DotBoxDPluginAnalyzerForbiddenDeclarationTypeReferenceTest",
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
