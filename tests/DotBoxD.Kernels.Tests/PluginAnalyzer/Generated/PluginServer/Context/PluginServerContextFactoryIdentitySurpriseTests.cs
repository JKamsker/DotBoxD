using System.Collections.Immutable;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerContextFactoryIdentitySurpriseTests
{
    [Fact]
    public void Context_factory_with_aliased_lookalike_hook_context_reports_signature_diagnostic()
    {
        var lookalikeHookContext = PluginServerGenerationTestDriver.CompileReference(
            """
            namespace DotBoxD.Abstractions;

            public sealed class HookContext;
            """,
            "LookalikeHookContext")
            .WithAliases(ImmutableArray.Create("Lookalike"));

        var (_, outputCompilation, generatorDiagnostics) = PluginServerGenerationTestDriver.RunWithDiagnostics(
            """
            extern alias Lookalike;

            [global::DotBoxD.Abstractions.GeneratePluginServer(
                Context = typeof(GameContext),
                ContextFactory = nameof(GameContext.Create))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext
            {
                public static GameContext Create(Lookalike::DotBoxD.Abstractions.HookContext raw) => new();
            }

            namespace Sample.Game
            {
                [global::DotBoxD.Services.Attributes.RpcService]
                public interface IGameWorld;
            }

            namespace Sample.Game.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : global::DotBoxD.Plugins.IServerExtensionWireClient
                {
                    global::System.Threading.Tasks.ValueTask<string> InstallPluginAsync(
                        string packageJson,
                        global::System.Threading.CancellationToken ct = default);
                    global::System.Threading.Tasks.ValueTask<string> InstallSubscriptionAsync(
                        string packageJson,
                        global::System.Threading.CancellationToken ct = default);
                    global::System.Threading.Tasks.ValueTask<string> InstallServerExtensionAsync(
                        string packageJson,
                        global::System.Threading.CancellationToken ct = default);
                    global::System.Threading.Tasks.ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        global::System.Threading.CancellationToken ct = default);
                    global::System.Threading.Tasks.ValueTask HoldUntilShutdownAsync(
                        global::System.Threading.CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Sample.Game.IGameWorld GetGameWorld(global::DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new global::System.InvalidOperationException("not used");
                }
            }
            """,
            lookalikeHookContext);

        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
        Assert.Contains(
            generatorDiagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("must be static and have signature", StringComparison.Ordinal));
    }
}
