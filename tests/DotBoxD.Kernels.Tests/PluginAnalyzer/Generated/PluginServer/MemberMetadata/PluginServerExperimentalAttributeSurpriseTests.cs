using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerExperimentalAttributeSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_preserves_experimental_attributes_on_forwarded_members()
    {
        var (generated, _) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    [Experimental("DBXEXP001")]
                    ValueTask<int> ExperimentalPingAsync();

                    [Experimental("DBXEXP002", Message = "Use StablePingAsync.")]
                    ValueTask<int> ExperimentalPingWithMessageAsync();

                    [Experimental("DBXEXP001")]
                    IMonsterControl ExperimentalMonsters { get; }
            """, """

                [RpcService]
                public interface IMonsterControl
                {
                    [Experimental("DBXEXP001")]
                    string ExperimentalRename(string value);

                    [Experimental("DBXEXP001")]
                    IInventory ExperimentalInventory { get; }
                }

                [RpcService]
                public interface IInventory
                {
                    [Experimental("DBXEXP001")]
                    int ExperimentalCount();
                }
            """));

        var normalized = NormalizeLineEndings(generated);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP001\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> ExperimentalPingAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP002\", Message = \"Use StablePingAsync.\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> ExperimentalPingWithMessageAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP001\")]\n" +
            "    public global::Regression.Game.IMonsterControl ExperimentalMonsters",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP001\")]\n" +
            "        public string ExperimentalRename(",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP001\")]\n" +
            "        public global::Regression.Game.IInventory ExperimentalInventory",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP001\")]\n" +
            "            public int ExperimentalCount(",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_surface_reports_experimental_diagnostics_to_consumers()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    [Experimental("DBXEXP001")]
                    ValueTask<int> ExperimentalPingAsync();

                    [Experimental("DBXEXP001")]
                    IMonsterControl ExperimentalMonsters { get; }
            """, """

                [RpcService]
                public interface IMonsterControl
                {
                    ValueTask<int> CountAsync();
                }
            """));

        var consumerTree = CSharpSyntaxTree.ParseText(
            """
            #nullable enable
            using System.Threading.Tasks;
            using Regression.Plugin;

            namespace Regression.Consumer;

            public static class GeneratedPluginServerConsumer
            {
                public static async ValueTask<int> UseAsync(RemotePluginServer server)
                {
                    _ = server.ExperimentalMonsters;
                    return await server.ExperimentalPingAsync();
                }
            }
            """,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var diagnostics = outputCompilation.AddSyntaxTrees(consumerTree)
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Location.SourceTree == consumerTree)
            .ToArray();

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXEXP001");
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "DBXEXP001"));
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ServerSource(string worldMembers, string extraGameTypes)
        => $$"""
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
            {{worldMembers}}
                }
            {{extraGameTypes}}
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
