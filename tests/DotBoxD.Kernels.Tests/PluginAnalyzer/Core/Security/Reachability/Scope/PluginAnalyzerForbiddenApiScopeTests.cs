using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiScopeTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Does_not_report_native_transport_code_in_hook_contract_assembly()
    {
        var source = Source(includeKernel: false);

        var generated = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(source);
        Assert.Contains(
            generated,
            text => text.Contains("partial record struct ExampleResult", StringComparison.Ordinal));

        var diagnostics = await AnalyzeAsync(source);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_kernel_that_reaches_same_native_transport_helper()
    {
        var diagnostics = await AnalyzeAsync(Source(includeKernel: true));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerForbiddenScopeTest",
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

    private static string Source(bool includeKernel)
        => $$"""
            #nullable enable
            namespace Microsoft.Extensions.Hosting
            {
                using System.Threading;
                using System.Threading.Tasks;

                public interface IHostedService
                {
                    Task StartAsync(CancellationToken cancellationToken);

                    Task StopAsync(CancellationToken cancellationToken);
                }
            }

            namespace Sample
            {
                using System.IO;
                using System.IO.Pipes;
                using System.Net.Sockets;
                using System.Threading;
                using System.Threading.Tasks;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Hosting;

                [Hook("example.changed", typeof(ExampleResult))]
                public readonly record struct ExampleContext(int Value);

                [HookResult]
                public readonly partial record struct ExampleResult(bool Success, string? Reason);

                public sealed class NativeConnectionWorker : IHostedService
                {
                    public async Task StartAsync(CancellationToken cancellationToken)
                    {
                        await using Stream stream = await NativeTransport.OpenAsync(cancellationToken);
                        _ = stream.CanRead;
                    }

                    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                }

                public static class NativeTransport
                {
                    public static Task<Stream> OpenAsync(CancellationToken cancellationToken)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var pipe = new NamedPipeServerStream("scope-test");
                        using var client = new TcpClient();
                        return Task.FromResult<Stream>(new MemoryStream());
                    }
                }

                {{(includeKernel ? KernelSource : string.Empty)}}
            }
            """;

    private const string KernelSource = """
        [Plugin("scope-kernel")]
        public sealed class ScopeKernel : IEventKernel<ExampleContext>
        {
            public bool ShouldHandle(ExampleContext e, HookContext context) => true;

            public async void Handle(ExampleContext e, HookContext context)
            {
                await using Stream stream = await NativeTransport.OpenAsync(context.CancellationToken);
            }
        }
        """;
}
