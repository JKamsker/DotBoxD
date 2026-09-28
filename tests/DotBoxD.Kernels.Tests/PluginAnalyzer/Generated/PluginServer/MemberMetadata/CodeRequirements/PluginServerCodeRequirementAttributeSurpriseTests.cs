namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerCodeRequirementAttributeSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_preserves_code_requirement_attributes_on_root_and_nested_methods()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var normalized = generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            RootAttributes +
            "    public global::System.Threading.Tasks.ValueTask<int> RootRequirementAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            NestedAttributes +
            "        public global::System.Threading.Tasks.ValueTask<int> NestedRequirementAsync(",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_keeps_unannotated_forwarded_methods_clean()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var normalized = generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RequiresUnreferencedCodeAttribute(\"root trimming requirement\", Url = \"https://example.invalid/root-trimming\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> CleanRootAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RequiresDynamicCodeAttribute(\"nested dynamic-code requirement\", Url = \"https://example.invalid/nested-dynamic\")]\n" +
            "        public global::System.Threading.Tasks.ValueTask<int> CleanNestedAsync(",
            normalized,
            StringComparison.Ordinal);
    }

    private const string RootAttributes =
        "    [global::System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute(\"root assembly-files requirement\", Url = \"https://example.invalid/root-assembly-files\")]\n" +
        "    [global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute(\"root dynamic-code requirement\", Url = \"https://example.invalid/root-dynamic\")]\n" +
        "    [global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute(\"root trimming requirement\", Url = \"https://example.invalid/root-trimming\")]\n";

    private const string NestedAttributes =
        "        [global::System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute(\"nested assembly-files requirement\", Url = \"https://example.invalid/nested-assembly-files\")]\n" +
        "        [global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute(\"nested dynamic-code requirement\", Url = \"https://example.invalid/nested-dynamic\")]\n" +
        "        [global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute(\"nested trimming requirement\", Url = \"https://example.invalid/nested-trimming\")]\n";

    private const string ServerSource = """
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
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
                [RequiresUnreferencedCode("root trimming requirement", Url = "https://example.invalid/root-trimming")]
                [RequiresDynamicCode("root dynamic-code requirement", Url = "https://example.invalid/root-dynamic")]
                [RequiresAssemblyFiles("root assembly-files requirement", Url = "https://example.invalid/root-assembly-files")]
                ValueTask<int> RootRequirementAsync();

                ValueTask<int> CleanRootAsync();

                IWorkshopAccess Workshop { get; }
            }

            [RpcService]
            public interface IWorkshopAccess
            {
                [RequiresUnreferencedCode("nested trimming requirement", Url = "https://example.invalid/nested-trimming")]
                [RequiresDynamicCode("nested dynamic-code requirement", Url = "https://example.invalid/nested-dynamic")]
                [RequiresAssemblyFiles("nested assembly-files requirement", Url = "https://example.invalid/nested-assembly-files")]
                ValueTask<int> NestedRequirementAsync();

                ValueTask<int> CleanNestedAsync();
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
