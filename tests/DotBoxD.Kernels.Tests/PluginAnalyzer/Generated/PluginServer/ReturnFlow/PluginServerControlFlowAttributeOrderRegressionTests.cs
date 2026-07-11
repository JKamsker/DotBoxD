namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerControlFlowAttributeOrderRegressionTests
{
    [Fact]
    public void Inherited_control_properties_with_equivalent_flow_attributes_ignore_source_order()
    {
        var (_, outputCompilation, generatorDiagnostics) =
            PluginServerGenerationTestDriver.RunWithDiagnostics("""
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
                    public interface IBaseWorld
                    {
                        [MaybeNull]
                        [NotNull]
                        IMonsterControl Monsters { get; }
                    }

                    [RpcService]
                    public interface IGameWorldAccess : IBaseWorld
                    {
                        [NotNull]
                        [MaybeNull]
                        new IMonsterControl Monsters { get; }
                    }

                    [RpcService]
                    public interface IMonsterControl
                    {
                        ValueTask<int> CountAsync();
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
        Assert.DoesNotContain(
            generatorDiagnostics,
            diagnostic =>
                diagnostic.Id == "DBXK100" &&
                diagnostic.GetMessage().Contains("different flow attributes", StringComparison.Ordinal));
    }
}
