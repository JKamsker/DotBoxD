using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerInheritedTupleParameterConflictSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_rejects_inherited_tuple_parameter_name_conflicts()
    {
        var (_, outputCompilation, generatorDiagnostics) = PluginServerGenerationTestDriver.RunWithDiagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Regression.Game
            {
                [RpcService]
                public interface ILeftWorld
                {
                    void Move((int x, int y) point);
                }

                [RpcService]
                public interface IRightWorld
                {
                    void Move((int row, int column) point);
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

        var focusedDiagnostics = generatorDiagnostics
            .Where(diagnostic => diagnostic.Id == "DBXK100")
            .Select(diagnostic => diagnostic.GetMessage())
            .ToArray();
        var outputErrors = outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        var rawTupleErrors = outputErrors
            .Where(diagnostic => diagnostic.Id is "CS0111" or "CS8141")
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")
            .ToArray();

        Assert.True(
            focusedDiagnostics.Any(
                message => message.Contains("inherited", StringComparison.OrdinalIgnoreCase) &&
                           message.Contains("tuple", StringComparison.OrdinalIgnoreCase)),
            $"""
            Expected a focused DBXK100 inherited tuple-parameter metadata diagnostic.

            DBXK100 diagnostics:
            {string.Join("\n", focusedDiagnostics)}

            Output errors:
            {string.Join("\n", outputErrors.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}"))}
            """);
        Assert.Empty(rawTupleErrors);
    }
}
