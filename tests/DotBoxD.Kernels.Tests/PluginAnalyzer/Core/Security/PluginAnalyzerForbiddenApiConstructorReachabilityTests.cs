using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiConstructorReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_constructor_reached_through_field_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public sealed class Helper
                {
                    public Helper() => _ = System.IO.File.ReadAllText("/etc/passwd");
                }

                [Plugin("init-ctor-leak")]
                public sealed class InitCtorKernel : IEventKernel<string>
                {
                    private readonly Helper Helper = new Helper();

                    public bool ShouldHandle(string e, HookContext context) => Helper is not null;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_forbidden_constructor_reached_through_property_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public sealed class Helper
                {
                    public Helper() => _ = System.IO.File.ReadAllText("/x");
                }

                [Plugin("prop-init-ctor-leak")]
                public sealed class PropInitCtorKernel : IEventKernel<string>
                {
                    private Helper Helper { get; } = new Helper();

                    public bool ShouldHandle(string e, HookContext context) => Helper is not null;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_forbidden_constructor_reached_through_kernel_method_body()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public sealed class Helper
                {
                    public Helper() => _ = System.IO.File.ReadAllText("/x");
                }

                [Plugin("method-ctor-leak")]
                public sealed class MethodCtorKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => new Helper() is not null;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerConstructorReachabilityTest",
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
