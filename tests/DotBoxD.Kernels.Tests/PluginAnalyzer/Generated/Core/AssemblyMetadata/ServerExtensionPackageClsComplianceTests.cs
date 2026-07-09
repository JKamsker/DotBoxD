using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class ServerExtensionPackageClsComplianceTests
{
    private static readonly HashSet<string> s_clsDiagnosticIds = ["CS3001", "CS3002", "CS3003"];
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Cls_compliant_assembly_does_not_report_generated_cls_diagnostics_for_service_backed_server_extension()
    {
        var compilation = CreateCompilation("""
            #nullable enable
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            [assembly: CLSCompliant(true)]

            namespace Sample;

            [RpcService]
            public interface IRemoteControl;

            [CLSCompliant(false)]
            public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
            {
                public RemoteControl(IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
            }

            [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
                [CLSCompliant(false)]
                public int Echo(int value, HookContext ctx) => value;
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: s_parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        if (TryAssertFocusedClsFailClosed(generatorDiagnostics, driver.GetRunResult()))
        {
            return;
        }

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        Assert.NotEmpty(runResult.GeneratedTrees);
        var generatedTrees = runResult.GeneratedTrees.ToHashSet();
        var diagnostics = outputCompilation.GetDiagnostics();
        var userClsDiagnostics = ClsDiagnostics(diagnostics)
            .Where(d => d.Location.SourceTree is null || !generatedTrees.Contains(d.Location.SourceTree))
            .Select(d => d.ToString())
            .ToArray();
        Assert.Empty(userClsDiagnostics);

        var generatedClsDiagnostics = ClsDiagnostics(diagnostics)
            .Where(d => d.Location.SourceTree is not null && generatedTrees.Contains(d.Location.SourceTree))
            .Select(d => d.ToString())
            .ToArray();

        Assert.True(
            generatedClsDiagnostics.Length == 0,
            "Generated package sources should not emit CLS diagnostics:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, generatedClsDiagnostics));
    }

    private static bool TryAssertFocusedClsFailClosed(
        IEnumerable<Diagnostic> generatorDiagnostics,
        GeneratorDriverRunResult runResult)
    {
        var failClosed = generatorDiagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage().Contains("CLS", StringComparison.OrdinalIgnoreCase));
        if (failClosed is null)
        {
            return false;
        }

        Assert.Empty(runResult.GeneratedTrees);
        return true;
    }

    private static IEnumerable<Diagnostic> ClsDiagnostics(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Where(diagnostic => s_clsDiagnosticIds.Contains(diagnostic.Id));

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDServerExtensionClsComplianceTest",
            [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
