using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientExperimentalAttributeSurpriseTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Service_backed_generated_client_preserves_experimental_attributes()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedSource);
        var client = assembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true)!;
        var method = client.GetMethod(
            "ExperimentalEchoAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(int), typeof(System.Threading.CancellationToken)])!;

        var attribute = Assert.Single(method.GetCustomAttributes<ExperimentalAttribute>());

        Assert.Equal("DBXEXP001", attribute.DiagnosticId);
    }

    [Fact]
    public void Service_backed_receiver_method_extension_preserves_experimental_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ServiceBackedSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP001\")]");
        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "ExperimentalEchoValue(int @value, global::System.Threading.CancellationToken @cancellationToken = default)");
    }

    [Fact]
    public void Direct_receiver_method_extension_preserves_experimental_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(DirectExtensionSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelDirectServerExtensionClientExtensions",
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP001\")]");
    }

    [Fact]
    public void Direct_generated_surface_calls_report_experimental_diagnostics()
    {
        var (compilation, generatorDiagnostics) = CompileWithConsumer(ServiceBackedSource, ConsumerSource);
        var diagnostics = compilation.GetDiagnostics();

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.Id != "DBXEXP001");
        AssertExperimentalDiagnosticOnLine(diagnostics, "client.ExperimentalEchoAsync");
        AssertExperimentalDiagnosticOnLine(diagnostics, "control.ExperimentalEchoValue");
    }

    private static void AssertGeneratedSourceContains(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string expectedSource)
        => Assert.Contains(
            generatedSources,
            source => source.Contains(generatedTypeName, StringComparison.Ordinal) &&
                      source.Contains(expectedSource, StringComparison.Ordinal));

    private static void AssertExperimentalDiagnosticOnLine(
        IReadOnlyList<Diagnostic> diagnostics,
        string expectedCall)
    {
        var matchingDiagnostic = diagnostics.Any(diagnostic =>
            diagnostic.Id == "DBXEXP001" &&
            diagnostic.Location.SourceTree is not null &&
            DiagnosticLine(diagnostic).Contains(expectedCall, StringComparison.Ordinal));

        Assert.True(
            matchingDiagnostic,
            "Expected DBXEXP001 on generated-surface call '" +
            expectedCall +
            "'. Actual diagnostics:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    private static string DiagnosticLine(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var text = diagnostic.Location.SourceTree!.GetText();
        return text.Lines[span.StartLinePosition.Line].ToString();
    }

    private static (CSharpCompilation Compilation, IReadOnlyList<Diagnostic> GeneratorDiagnostics) CompileWithConsumer(
        string source,
        string consumer)
    {
        var runResult = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(source, ParseOptions),
                CSharpSyntaxTree.ParseText(consumer, ParseOptions),
            }
            .Concat(runResult.GeneratedTrees);
        var compilation = CSharpCompilation.Create(
            "DotBoxDGeneratedPackageExperimentalConsumerTest",
            syntaxTrees,
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (compilation, runResult.Diagnostics);
    }

    private static IEnumerable<MetadataReference> References()
    {
        foreach (var reference in TrustedPlatformReferences())
        {
            yield return reference;
        }

        yield return MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(DotBoxD.Kernels.SandboxModule).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private const string ConsumerSource = """
        #nullable enable
        using System.Threading;
        using System.Threading.Tasks;

        namespace Sample;

        public static class GeneratedSurfaceConsumer
        {
            public static ValueTask<int> ViaClient(
                EchoKernelServerExtensionClient client,
                CancellationToken cancellationToken)
                => client.ExperimentalEchoAsync(42, cancellationToken);

            public static ValueTask<int> ViaReceiver(
                RemoteControl control,
                CancellationToken cancellationToken)
                => control.ExperimentalEchoValue(42, cancellationToken);
        }
        """;

    private const string ServiceBackedSource = """
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public interface IEchoService
        {
            [Experimental("DBXEXP001")]
            ValueTask<int> ExperimentalEchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "ExperimentalEchoValue")]
            public int ExperimentalEcho(int value, HookContext ctx) => value;
        }
        """;

    private const string DirectExtensionSource = """
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(IRemoteControl), "direct-echo")]
        public sealed partial class EchoKernel
        {
            [Experimental("DBXEXP001")]
            [ServerExtensionMethod(typeof(IRemoteControl), "ExperimentalEcho")]
            public int ExperimentalEcho(int value, HookContext ctx) => value;
        }
        """;
}
