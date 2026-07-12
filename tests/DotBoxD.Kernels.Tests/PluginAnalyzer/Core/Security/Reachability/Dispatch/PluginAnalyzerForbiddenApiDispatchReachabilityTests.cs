using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiDispatchReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_helper_method_reached_through_interface_dispatch()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public interface IHelper
                {
                    bool Danger();
                }

                public sealed class Helper : IHelper
                {
                    public bool Danger()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return true;
                    }
                }

                [Plugin("interface-dispatch-leak")]
                public sealed class InterfaceDispatchKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        IHelper helper = new Helper();
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
    public async Task Reports_forbidden_helper_method_reached_through_virtual_dispatch()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public abstract class BaseHelper
                {
                    public abstract bool Danger();
                }

                public sealed class Helper : BaseHelper
                {
                    public override bool Danger()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return true;
                    }
                }

                [Plugin("virtual-dispatch-leak")]
                public sealed class VirtualDispatchKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        BaseHelper helper = new Helper();
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
    public async Task Reports_forbidden_helper_method_reached_through_concrete_dispatch_control()
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

                [Plugin("concrete-dispatch-control")]
                public sealed class ConcreteDispatchKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        var helper = new Helper();
                        return helper.Danger();
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "return helper.Danger();");
    }

    private static void AssertSingleForbiddenDiagnosticAt(
        string source,
        ImmutableArray<Diagnostic> diagnostics,
        string expectedLine)
    {
        var compilerErrors = diagnostics.Where(d => d.Id.StartsWith("CS", StringComparison.Ordinal)
            && d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilerErrors);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);

        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        var actualLine = source.Split('\n')[position.Line].Trim();
        Assert.Equal(expectedLine, actualLine);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var diagnostics = compilation.GetDiagnostics().ToImmutableArray();
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return diagnostics;
        }

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerDispatchReachabilityTest",
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
