using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiGenericInitializerConstructionTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_generic_new_constraint_reached_through_field_initializer()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class GenericContainer<T> where T : new()
                {
                    private readonly T value = new T();

                    public bool Ok => value is not null;
                }

                public sealed class DangerousConstructed
                {
                    public DangerousConstructed()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                    }
                }

                [Plugin("generic-field-initializer-reachability")]
                public sealed class GenericInitializerKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return new GenericContainer<DangerousConstructed>().Ok;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        AssertSingleForbiddenDiagnosticAt(
            source,
            diagnostics,
            "return new GenericContainer<DangerousConstructed>().Ok;");
    }

    [Fact]
    public async Task Reports_transitive_generic_construction_reached_through_property_initializer()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class GenericContainer<T> where T : new()
                {
                    private GenericWrapper<T> Value { get; } = new GenericWrapper<T>();

                    public bool Ok => Value.Ok;
                }

                public sealed class GenericWrapper<T> where T : new()
                {
                    private readonly T value = new T();

                    public bool Ok => value is not null;
                }

                public sealed class DangerousConstructed
                {
                    public DangerousConstructed()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                    }
                }

                [Plugin("generic-property-initializer-reachability")]
                public sealed class GenericInitializerKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return new GenericContainer<DangerousConstructed>().Ok;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        AssertSingleForbiddenDiagnosticAt(
            source,
            diagnostics,
            "return new GenericContainer<DangerousConstructed>().Ok;");
    }

    private static void AssertSingleForbiddenDiagnosticAt(
        string source,
        ImmutableArray<Diagnostic> diagnostics,
        string expectedLine)
    {
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);

        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        var actualLine = diagnostic.Location.SourceTree!.GetText().Lines[position.Line].ToString().Trim();
        Assert.Equal(expectedLine, actualLine);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        return diagnostics;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerForbiddenGenericInitializerConstructionTest",
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
