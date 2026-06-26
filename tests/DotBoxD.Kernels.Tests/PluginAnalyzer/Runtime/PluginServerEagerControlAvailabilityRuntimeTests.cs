using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

// A server built with the eager constructor and no world proxy is fully started (_started = true) but its
// control fields are null. Accessing a control must report that the server has no world proxy, not the
// misleading "Call StartAsync()" message (StartAsync is a no-op once started and cannot help).
public sealed class PluginServerEagerControlAvailabilityRuntimeTests
{
    [Fact]
    public void Accessing_a_control_without_a_world_proxy_reports_an_accurate_message()
    {
        var assembly = Compile(Source);
        var stub = Activator.CreateInstance(
            assembly.GetType("Sample.StubControlService", throwOnError: true)!)!;
        var serverType = assembly.GetType("EagerControl.Plugin.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [stub, null])!;

        var monsters = serverType.GetProperty("Monsters", BindingFlags.Public | BindingFlags.Instance)!;
        var error = Assert.Throws<TargetInvocationException>(() => monsters.GetValue(server));
        var inner = Assert.IsType<InvalidOperationException>(error.InnerException);

        Assert.Contains("without a world proxy", inner.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Call StartAsync()", inner.Message, StringComparison.Ordinal);
    }

    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace EagerControl.Game
        {
            [DotBoxDService]
            public interface IGameWorldAccess
            {
                IMonsterControl Monsters { get; }
            }

            [DotBoxDService]
            public interface IMonsterControl;
        }

        namespace EagerControl.Game.Ipc
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
                public static EagerControl.Game.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }

        namespace EagerControl.Plugin
        {
            using DotBoxD.Abstractions;
            using EagerControl.Game;

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;
        }

        namespace Sample
        {
            using EagerControl.Game;
            using EagerControl.Game.Ipc;

            public sealed class StubControlService : IGamePluginControlService
            {
                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default) => default;
                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default) => default;
                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default) => default;
                public ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default) => default;
                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => default;
                public ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, CancellationToken cancellationToken = default) => default;
            }
        }
        """;

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerEagerControlRuntimeTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GeneratePluginServerAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));
}
