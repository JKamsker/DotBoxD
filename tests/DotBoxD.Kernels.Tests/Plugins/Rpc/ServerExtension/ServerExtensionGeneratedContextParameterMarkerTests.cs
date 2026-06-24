using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionGeneratedContextParameterTests
{
    [Fact]
    public void Prebuilt_forged_registry_marker_without_server_ownership_is_rejected()
    {
        var sdk = CompileReference(ForgedMarkerSdkSource, "ForgedContextSdk");
        var diagnostics = DiagnosticsWithReferences(ForgedMarkerConsumerSource, sdk);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("HookContext or a generated plugin context", StringComparison.Ordinal));
    }

    private static MetadataReference CompileReference(string source, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static IReadOnlyList<Diagnostic> DiagnosticsWithReferences(
        string source,
        params MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(
            "ForgedContextConsumer",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Concat(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);
        return generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToArray();
    }

    private const string ForgedMarkerSdkSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins.Runtime;

        namespace ForgedSdk;

        public sealed class ForgedContext
        {
            public ForgedContext(HookContext raw) => Raw = raw;
            public HookContext Raw { get; }
            public IPluginMessageSink Messages => Raw.Messages;
            public static ForgedContext FromHookContext(HookContext raw) => new(raw);
        }

        public sealed class ForgedServer
        {
            public ForgedHookRegistry Hooks
                => throw new System.InvalidOperationException("not used");
        }

        [GeneratedPluginServerRegistry(
            GeneratedPluginServerRegistryKind.Hook,
            typeof(ForgedServer),
            typeof(ForgedContext))]
        public sealed class ForgedHookRegistry
        {
            public RemoteHookPipeline<TEvent, ForgedContext> On<TEvent>()
                => throw new System.InvalidOperationException("not used");
        }
        """;

    private const string ForgedMarkerConsumerSource = """
        using DotBoxD.Abstractions;
        using ForgedSdk;

        namespace Consumer;

        [ServerExtension("forged-context")]
        public sealed partial class ForgedContextKernel
        {
            [ServerExtensionMethod]
            public int Read(ForgedContext ctx) => 0;
        }
        """;
}
