using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts.PluginServer;

public sealed class GeneratePluginServerAttributeContractTests
{
    [Fact]
    public void ContextFactory_allows_omitted_metadata()
    {
        var generated = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(MinimalServer("""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext;
            """));
        var source = string.Join("\n", generated);

        Assert.Contains(
            "FromHookContext(global::DotBoxD.Abstractions.HookContext raw) => new(raw);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Create(raw)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("\"\\t\"")]
    public void ContextFactory_rejects_blank_method_names(string contextFactoryExpression)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer($$"""
            [GeneratePluginServer(Context = typeof(GameContext), ContextFactory = {{contextFactoryExpression}})]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext;
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains(
                     "ContextFactory must name a static context factory method.",
                     StringComparison.Ordinal));
    }

    private static string MinimalServer(string pluginSource)
        => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Sample.Game
            {
                [RpcService]
                public interface IGameWorld;
            }

            namespace Sample.Game.Ipc
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
                    public static Sample.Game.IGameWorld GetGameWorld(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Sample.Plugin
            {
                {{pluginSource}}
            }
            """;
}
