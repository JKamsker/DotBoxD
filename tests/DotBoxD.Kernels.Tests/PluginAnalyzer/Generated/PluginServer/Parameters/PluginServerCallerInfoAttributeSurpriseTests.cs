namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerCallerInfoAttributeSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_preserves_caller_info_attributes_on_forwarded_methods()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    ValueTask<string> TraceAsync(
                        int value,
                        [CallerMemberName] string member = "",
                        [CallerFilePath] string file = "",
                        [CallerLineNumber] int line = 0,
                        [CallerArgumentExpression(nameof(value))] string expression = "");

                    IMonsterControl Monsters { get; }
                }

                [DotBoxDService]
                public interface IMonsterControl
                {
                    ValueTask<string> InspectAsync(
                        int value,
                        [CallerMemberName] string member = "",
                        [CallerArgumentExpression(nameof(value))] string expression = "");
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
        Assert.Contains(
            "TraceAsync(int @value, " +
            "[global::System.Runtime.CompilerServices.CallerMemberNameAttribute] string @member = \"\", " +
            "[global::System.Runtime.CompilerServices.CallerFilePathAttribute] string @file = \"\", " +
            "[global::System.Runtime.CompilerServices.CallerLineNumberAttribute] int @line = 0, " +
            "[global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute(\"value\")] " +
            "string @expression = \"\")",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "InspectAsync(int @value, " +
            "[global::System.Runtime.CompilerServices.CallerMemberNameAttribute] string @member = \"\", " +
            "[global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute(\"value\")] " +
            "string @expression = \"\")",
            generated,
            StringComparison.Ordinal);
    }
}
