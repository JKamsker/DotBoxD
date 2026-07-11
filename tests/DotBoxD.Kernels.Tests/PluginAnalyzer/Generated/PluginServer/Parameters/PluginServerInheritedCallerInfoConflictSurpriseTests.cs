namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerInheritedCallerInfoConflictSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_reports_inherited_caller_info_parameter_conflicts()
    {
        var source = ServerSource();

        var (generated, outputCompilation, generatorDiagnostics) =
            PluginServerGenerationTestDriver.RunWithDiagnostics(source);

        var focusedDiagnostics = generatorDiagnostics
            .Where(diagnostic => diagnostic.Id == "DBXK100")
            .ToArray();
        Assert.Contains(
            focusedDiagnostics,
            diagnostic =>
            {
                var message = diagnostic.GetMessage();
                return message.Contains("inherited", StringComparison.OrdinalIgnoreCase) &&
                       message.Contains("caller", StringComparison.OrdinalIgnoreCase);
            });

        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Id == "CS0229"));
        Assert.DoesNotContain(
            "[global::System.Runtime.CompilerServices.CallerFilePathAttribute] string @member = \"\"",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[global::System.Runtime.CompilerServices.CallerMemberNameAttribute] string @member = \"\"",
            generated,
            StringComparison.Ordinal);
    }

    private static string ServerSource()
        => """
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [RpcService]
                public interface ILeftWorld
                {
                    ValueTask TraceAsync([CallerMemberName] string member = "");
                }

                [RpcService]
                public interface IRightWorld
                {
                    ValueTask TraceAsync([CallerFilePath] string member = "");
                }

                [RpcService]
                public interface IGameWorldAccess : ILeftWorld, IRightWorld;
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
