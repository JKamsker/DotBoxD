using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientTypeExperimentalAttributeSurpriseTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Service_backed_generated_client_preserves_experimental_service_attribute()
    {
        var result = RunGenerator(ServiceBackedSource);
        if (AssertFocusedFailClosedDiagnostic(result.GeneratorDiagnostics))
        {
            return;
        }

        AssertNoGeneratedExperimentalDiagnostics(result.OutputCompilation, result.GeneratedTrees);
        AssertGeneratedSourceContains(
            result.GeneratedSources,
            "EchoKernelServerExtensionClient",
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP_TYPE\")]");
        AssertDirectGeneratedClientUseReportsExperimentalDiagnostic(result.GeneratedTrees);
    }

    private static bool AssertFocusedFailClosedDiagnostic(IReadOnlyList<Diagnostic> diagnostics)
    {
        var failClosedDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal))
            .ToArray();
        if (failClosedDiagnostics.Length == 0)
        {
            Assert.DoesNotContain(
                diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            return false;
        }

        Assert.All(
            failClosedDiagnostics,
            diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        return true;
    }

    private static void AssertNoGeneratedExperimentalDiagnostics(
        Compilation compilation,
        IReadOnlySet<SyntaxTree> generatedTrees)
    {
        var generatedDiagnostics = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id == "DBXEXP_TYPE" &&
                                 diagnostic.Location.SourceTree is { } tree &&
                                 generatedTrees.Contains(tree))
            .ToArray();

        Assert.Empty(generatedDiagnostics);
    }

    private static void AssertGeneratedSourceContains(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string expectedSource)
        => Assert.Contains(
            generatedSources,
            source => source.Contains(generatedTypeName, StringComparison.Ordinal) &&
                      source.Contains(expectedSource, StringComparison.Ordinal));

    private static void AssertDirectGeneratedClientUseReportsExperimentalDiagnostic(
        IReadOnlySet<SyntaxTree> generatedTrees)
    {
        var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(ServiceBackedSource, ParseOptions),
                CSharpSyntaxTree.ParseText(ConsumerSource, ParseOptions),
            }
            .Concat(generatedTrees);
        var compilation = CSharpCompilation.Create(
            "DotBoxDGeneratedPackageExperimentalTypeConsumerTest",
            syntaxTrees,
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostics = compilation.GetDiagnostics();

        AssertExperimentalDiagnosticOnLine(diagnostics, "EchoKernelServerExtensionClient client");
    }

    private static void AssertExperimentalDiagnosticOnLine(
        IReadOnlyList<Diagnostic> diagnostics,
        string expectedLine)
    {
        var matchingDiagnostic = diagnostics.Any(diagnostic =>
            diagnostic.Id == "DBXEXP_TYPE" &&
            diagnostic.Location.SourceTree is not null &&
            DiagnosticLine(diagnostic).Contains(expectedLine, StringComparison.Ordinal));

        Assert.True(
            matchingDiagnostic,
            "Expected DBXEXP_TYPE on generated-client use line '" +
            expectedLine +
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

    private static (
        IReadOnlyList<string> GeneratedSources,
        Compilation OutputCompilation,
        IReadOnlySet<SyntaxTree> GeneratedTrees,
        IReadOnlyList<Diagnostic> GeneratorDiagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDGeneratedPackageExperimentalTypeTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var generatedTrees = driver.GetRunResult().GeneratedTrees.ToHashSet();
        var generatedSources = generatedTrees
            .Select(tree => tree.GetText().ToString())
            .ToArray();
        return (generatedSources, outputCompilation, generatedTrees, generatorDiagnostics);
    }

    private static IEnumerable<MetadataReference> References()
    {
        foreach (var reference in TrustedPlatformReferences())
        {
            yield return reference;
        }

        yield return MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location);
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

        public static class GeneratedClientConsumer
        {
            public static ValueTask<int> ViaClient(
                EchoKernelServerExtensionClient client,
                CancellationToken cancellationToken)
                => client.EchoAsync(42, cancellationToken);
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

        [Experimental("DBXEXP_TYPE")]
        public interface IEchoService
        {
            ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
            public int Echo(int value, HookContext ctx) => value;
        }
        """;
}
