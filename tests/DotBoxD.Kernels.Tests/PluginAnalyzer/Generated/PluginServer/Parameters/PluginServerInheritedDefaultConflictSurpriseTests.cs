using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerInheritedDefaultConflictSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_rejects_inherited_method_optional_default_conflicts()
    {
        var (_, outputCompilation, generatorDiagnostics) =
            PluginServerGenerationTestDriver.RunWithDiagnostics("""
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
                        ValueTask<int> CountAsync(int value = 1, CancellationToken ct = default);
                    }

                    [RpcService]
                    public interface IRightWorld
                    {
                        ValueTask<int> CountAsync(int value = 2, CancellationToken ct = default);
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
                """);

        var outputDiagnostics = outputCompilation.GetDiagnostics();
        var reportsOptionalDefaultConflict = generatorDiagnostics.Any(
            diagnostic =>
            {
                var message = diagnostic.GetMessage();
                return diagnostic.Id == "DBXK100" &&
                       diagnostic.Severity == DiagnosticSeverity.Error &&
                       message.Contains("CountAsync", StringComparison.Ordinal) &&
                       message.Contains("inherited", StringComparison.OrdinalIgnoreCase) &&
                       (message.Contains("optional", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("default", StringComparison.OrdinalIgnoreCase));
            });
        var leaksRawAmbiguity = outputDiagnostics.Any(
            diagnostic =>
                diagnostic.Id == "CS0229" &&
                diagnostic.GetMessage().Contains("CountAsync", StringComparison.Ordinal));

        Assert.True(
            reportsOptionalDefaultConflict && !leaksRawAmbiguity,
            $"""
            Expected DBXK100 to reject inherited optional/default conflicts without leaking raw CS0229.

            Generator diagnostics:
            {string.Join("\n", generatorDiagnostics.Select(diagnostic => diagnostic.ToString()))}

            Output diagnostics:
            {string.Join("\n", outputDiagnostics.Select(diagnostic => diagnostic.ToString()))}
            """);
    }
}
