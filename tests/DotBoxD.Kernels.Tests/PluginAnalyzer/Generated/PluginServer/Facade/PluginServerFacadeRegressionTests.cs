namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerFacadeRegressionTests
{
    [Fact]
    public void Generated_plugin_server_rejects_multiple_direct_world_interfaces()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [RpcService]
                public interface IAlphaWorld;

                [RpcService]
                public interface IBetaWorld;
            }

            namespace Regression.Plugin
            {
                using DotBoxD.Abstractions;
                using Regression.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IAlphaWorld, IBetaWorld;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains(
            "must directly implement one [RpcService] world interface",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_includes_inherited_controls_and_wraps_async_handles()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [RpcService]
                public interface IGameWorldBase
                {
                    IMonsterControl Monsters { get; }
                }

                [RpcService]
                public interface IGameWorldAccess : IGameWorldBase;

                [RpcService]
                public interface IMonsterControl
                {
                    ValueTask<IMonster> GetAsync(string entityId);
                }

                [RpcService]
                public interface IMonster
                {
                    string Id { get; }
                    ValueTask<int> GetHealthAsync();
                }
            }

            namespace Regression.Game.Ipc
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
                    public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Regression.Plugin
            {
                using DotBoxD.Abstractions;
                using Regression.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("public partial class RemotePluginContext", generated, StringComparison.Ordinal);
        Assert.Contains("RemotePluginHookRegistry Hooks { get; }", generated, StringComparison.Ordinal);
        Assert.Contains("RemotePluginSubscriptionRegistry Subscriptions { get; }", generated, StringComparison.Ordinal);
        Assert.Contains("public global::Regression.Game.IMonsterControl Monsters", generated, StringComparison.Ordinal);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask<global::Regression.Game.IMonster> GetAsync", generated, StringComparison.Ordinal);
        Assert.Contains(
            "new MonsterPluginService(_owner, await ((global::Regression.Game.IMonsterControl)_inner).GetAsync",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("FromPipeNameWithKernelDebugging", generated, StringComparison.Ordinal);
        Assert.Contains(
            "FromPipeName(string pipeName, global::DotBoxD.Services.Peer.RpcPeerOptions? options)",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "FromPipeNameWithKernelDebugging(string pipeName, global::DotBoxD.Pushdown.Services.PluginDebugBridge debugBridge, global::DotBoxD.Services.Peer.RpcPeerOptions? options)",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "FromPipeName(string pipeName, global::DotBoxD.Pushdown.Services.NamedPipeTransportOptions transportOptions, global::DotBoxD.Services.Peer.RpcPeerOptions? options = null)",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("namedPipeOptions: transportOptions, options: options", generated, StringComparison.Ordinal);
        Assert.Contains("ProvidePluginDebugEvents(peer, debugBridge)", generated, StringComparison.Ordinal);
        Assert.Contains("_debugBridge?.AttachControl", generated, StringComparison.Ordinal);
        Assert.Contains("await PrepareDebugPackageAsync(package, cancellationToken)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_accepts_explicit_control_service_and_infers_update_type()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Explicit.Game
            {
                [RpcService]
                public interface IGameWorldAccess;
            }

            namespace Explicit.Control
            {
                public readonly record struct PluginSettingPatch(string Name, string Value);

                public interface IPluginControl : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        PluginSettingPatch[] updates,
                        bool atomic = false,
                        CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Explicit.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Explicit.Plugin
            {
                using DotBoxD.Abstractions;
                using Explicit.Game;

                [GeneratePluginServer(
                    Context = typeof(RemotePluginContext),
                    ControlService = typeof(Explicit.Control.IPluginControl))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("global::Explicit.Control.IPluginControl", generated, StringComparison.Ordinal);
        Assert.Contains("global::Explicit.Control.PluginSettingPatch", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IGamePluginControlService", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_rejects_file_local_world_interface_without_raw_compiler_errors()
    {
        var (_, outputCompilation, generatorDiagnostics) = PluginServerGenerationTestDriver.RunWithDiagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [RpcService]
                file interface IGameWorld;
            }

            namespace Regression.Game.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                internal interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
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

            namespace Regression.Plugin
            {
                using DotBoxD.Abstractions;
                using Regression.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                internal partial class RemotePluginServer : IGameWorld;

                internal sealed partial class RemotePluginContext;
            }
            """);

        var outputDiagnostics = outputCompilation.GetDiagnostics();
        var reportsFileLocalWorld = generatorDiagnostics.Any(
            diagnostic =>
            {
                var message = diagnostic.GetMessage();
                return diagnostic.Id == "DBXK100" &&
                       message.Contains("world", StringComparison.OrdinalIgnoreCase) &&
                       (message.Contains("file-local", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("inaccessible", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("cannot be named", StringComparison.OrdinalIgnoreCase));
            });
        var leaksGeneratedCompilerError = outputDiagnostics.Any(diagnostic => diagnostic.Id == "CS0234");

        Assert.True(
            reportsFileLocalWorld && !leaksGeneratedCompilerError,
            $"""
            Expected DBXK100 to reject the file-local world interface without leaking CS0234.

            Generator diagnostics:
            {string.Join("\n", generatorDiagnostics.Select(diagnostic => diagnostic.ToString()))}

            Output diagnostics:
            {string.Join("\n", outputDiagnostics.Select(diagnostic => diagnostic.ToString()))}
            """);
    }
}
