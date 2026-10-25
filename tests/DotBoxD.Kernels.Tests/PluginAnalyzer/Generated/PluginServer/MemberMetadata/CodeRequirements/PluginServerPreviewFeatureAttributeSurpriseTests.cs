namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerPreviewFeatureAttributeSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_preserves_preview_feature_requirements_on_root_and_nested_methods()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var normalized = generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "    [global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute(\"root preview requirement\", Url = \"https://example.invalid/root-preview\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> PreviewRootAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "        [global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute(\"nested preview requirement\", Url = \"https://example.invalid/nested-preview\")]\n" +
            "        public global::System.Threading.Tasks.ValueTask<int> PreviewNestedAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RequiresPreviewFeaturesAttribute(\"root preview requirement\", Url = \"https://example.invalid/root-preview\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> StableRootAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RequiresPreviewFeaturesAttribute(\"nested preview requirement\", Url = \"https://example.invalid/nested-preview\")]\n" +
            "        public global::System.Threading.Tasks.ValueTask<int> StableNestedAsync(",
            normalized,
            StringComparison.Ordinal);
    }

    private const string ServerSource = """
        #nullable enable
        using System.Runtime.Versioning;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Regression.Game
        {
            [RpcService]
            public interface IGameWorldAccess
            {
                [RequiresPreviewFeatures("root preview requirement", Url = "https://example.invalid/root-preview")]
                ValueTask<int> PreviewRootAsync();

                ValueTask<int> StableRootAsync();

                IWorkshopAccess Workshop { get; }
            }

            [RpcService]
            public interface IWorkshopAccess
            {
                [RequiresPreviewFeatures("nested preview requirement", Url = "https://example.invalid/nested-preview")]
                ValueTask<int> PreviewNestedAsync();

                ValueTask<int> StableNestedAsync();
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
        """;
}
