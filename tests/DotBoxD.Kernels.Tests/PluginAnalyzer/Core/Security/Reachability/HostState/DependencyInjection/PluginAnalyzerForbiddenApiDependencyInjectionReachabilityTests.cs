using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiDependencyInjectionReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "dependency injection provider",
        """
        private static readonly object Provider = BuildProvider();

        private static object BuildProvider()
        {
            var services = new ServiceCollection();
            for (var i = 0; i < 100_000; i++)
            {
                services.AddSingleton(new Retained(i));
            }

            return services.BuildServiceProvider();
        }
        """,
        "Microsoft.Extensions.DependencyInjection")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin-service-provider.json\");",
        "System.IO.File")]
    public async Task Reports_dependency_injection_provider_reached_from_static_initializer(
        string testCase,
        string staticMember,
        string expectedForbiddenApi)
    {
        var source = Source(staticMember);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedForbiddenApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    private static string Source(string staticMember)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.DependencyInjection;

                [Plugin("dependency-injection-host-api")]
                public sealed class DependencyInjectionKernel : IEventKernel<string>
                {
                    {{staticMember}}

                    public bool ShouldHandle(string e, HookContext context) => e.Length >= 0;

                    public void Handle(string e, HookContext context) { }

                    private sealed class Retained
                    {
                        public Retained(int value) => Value = value;

                        public int Value { get; }
                    }
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
            "DotBoxDPluginAnalyzerDependencyInjectionReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Concat(DependencyInjectionReferences())
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> DependencyInjectionReferences()
    {
        yield return MetadataReference.CreateFromFile(AspNetCoreTestReferences.FindAssembly(
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll"));
        yield return MetadataReference.CreateFromFile(AspNetCoreTestReferences.FindAssembly(
            "Microsoft.Extensions.DependencyInjection.dll"));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
