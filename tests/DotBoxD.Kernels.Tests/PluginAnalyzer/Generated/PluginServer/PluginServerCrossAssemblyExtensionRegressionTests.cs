using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

// When the [DotBoxDService] world interfaces live in a referenced assembly, the in-source collision count
// sees zero same-short-name services and falls back to Get{ShortName} (GetAccess), while the referenced
// assembly's own services generator emitted the disambiguated GetCombat_Access. The facade must resolve the
// Get extension that actually exists (mirroring the Provide{suffix} symmetry), not a dangling GetAccess.
public sealed class PluginServerCrossAssemblyExtensionRegressionTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private const string ReferencedWorldAndExtensions = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Cross.Combat
        {
            [DotBoxDService]
            public interface IAccess;
        }

        namespace Cross.Economy
        {
            [DotBoxDService]
            public interface IAccess;
        }

        namespace Cross.Combat.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Cross.Combat.IAccess GetCombat_Access(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");

                public static Cross.Economy.IAccess GetEconomy_Access(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }
        """;

    private const string ConsumerPlugin = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;

        namespace Cross.Plugin
        {
            using Cross.Combat;

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IAccess;

            public sealed partial class RemotePluginContext;
        }
        """;

    [Fact]
    public void Cross_assembly_world_resolves_the_generated_get_extension_that_exists()
    {
        var reference = CompileReference(ReferencedWorldAndExtensions, "Cross.Referenced");
        var (generated, outputCompilation) = RunGenerator(ConsumerPlugin, reference);

        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(
            "DotBoxDGeneratedExtensions.GetCombat_Access(_session.Peer)",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DotBoxDGeneratedExtensions.GetAccess(", generated, StringComparison.Ordinal);
    }

    private static MetadataReference CompileReference(string source, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            FacadeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static (string Generated, Compilation OutputCompilation) RunGenerator(
        string source,
        MetadataReference reference)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerCrossAssemblyTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            FacadeReferences().Append(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString()));
        return (generated, outputCompilation);
    }

    private static IEnumerable<MetadataReference> FacadeReferences()
        => TrustedPlatformReferences()
            .Append(MetadataReference.CreateFromFile(typeof(GeneratePluginServerAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        // Test and example assemblies in the host's trusted-platform set each carry their own generated
        // DotBoxD.Services.Generated.DotBoxDGeneratedExtensions. Excluding them keeps the referenced assembly's
        // extensions the sole definition the consumer sees, so the cross-assembly suffix resolution exercises the
        // real scenario instead of a CS0433 ambiguity.
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Where(reference => !IsServiceConsumerAssembly(Path.GetFileNameWithoutExtension(reference)))
            .Select(reference => MetadataReference.CreateFromFile(reference));

    private static bool IsServiceConsumerAssembly(string name)
        => name.Contains("Tests", StringComparison.Ordinal) ||
           name.StartsWith("Examples.", StringComparison.Ordinal) ||
           name.StartsWith("Sample", StringComparison.Ordinal);
}
