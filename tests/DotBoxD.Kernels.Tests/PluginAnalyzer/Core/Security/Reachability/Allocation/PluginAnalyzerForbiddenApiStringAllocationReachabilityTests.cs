using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiStringAllocationReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "string.Create allocation",
        "var value = string.Create(int.MaxValue, 0, static (span, state) => { });",
        "System.String.Create")]
    [InlineData(
        "direct System.IO control",
        "var value = System.IO.File.ReadAllText(\"/x\");",
        "System.IO.File")]
    public async Task Reports_unbounded_allocation_api_reached_from_event_kernel(
        string testCase,
        string statement,
        string expectedApi)
    {
        var source = Source(statement);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    [Theory]
    [InlineData(
        "string.Create",
        "string.Create(16, 0, static (span, state) => { })",
        "System.String.Create")]
    [InlineData(
        "RSA.Create",
        "System.Security.Cryptography.RSA.Create(2048)",
        "System.Security.Cryptography.RSA.Create")]
    public async Task Reports_exact_forbidden_members_in_event_kernel_initializers(
        string testCase,
        string initializer,
        string expectedApi)
    {
        var source = $$"""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("initializer-exact-member")]
                public sealed class InitializerKernel : IEventKernel<string>
                {
                    private readonly object _value = {{initializer}};

                    public bool ShouldHandle(string e, HookContext context) => _value is not null;
                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.True(
            diagnostics.Any(diagnostic => diagnostic.Id == "DBXK001" &&
                diagnostic.GetMessage().Contains(expectedApi, StringComparison.Ordinal)),
            $"{testCase}: expected an initializer diagnostic for {expectedApi}.");
    }

    private static string Source(string statement)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("string-allocation-leak")]
                public sealed class StringAllocationKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{statement}}
                        return value.Length > e.Length;
                    }

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
            "DotBoxDPluginAnalyzerStringAllocationReachabilityTest",
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
