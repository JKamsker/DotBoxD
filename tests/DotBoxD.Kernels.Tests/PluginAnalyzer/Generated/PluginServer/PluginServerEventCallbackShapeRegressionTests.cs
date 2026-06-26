using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

// A reverse event-callback contract whose OnEventAsync returns a value (ValueTask<T>) was previously accepted
// and silently stubbed (`await DispatchAsync(...); return default!;`). The event delegate must be the non-generic
// ValueTask (results flow through OnResultAsync), so the misshaped contract must be rejected with DBXK100.
public sealed class PluginServerEventCallbackShapeRegressionTests
{
    [Fact]
    public void Value_returning_on_event_async_contract_reports_dbxk100()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace ValueEvent.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace ValueEvent.Game.Ipc
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

                [DotBoxDService]
                public interface IPluginEventCallback
                {
                    ValueTask<int> OnEventAsync(string subscriptionId, System.ReadOnlyMemory<byte> projectedValue, CancellationToken ct = default);
                    ValueTask<byte[]> OnResultAsync(string subscriptionId, System.ReadOnlyMemory<byte> contextValue, CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static ValueEvent.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");

                    public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                        DotBoxD.Services.Peer.RpcPeer peer,
                        ValueEvent.Game.Ipc.IPluginEventCallback implementation)
                        => peer;
                }
            }

            namespace ValueEvent.Plugin
            {
                using DotBoxD.Abstractions;
                using ValueEvent.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains("OnEventAsync", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("ValueTask", StringComparison.Ordinal));
    }
}
