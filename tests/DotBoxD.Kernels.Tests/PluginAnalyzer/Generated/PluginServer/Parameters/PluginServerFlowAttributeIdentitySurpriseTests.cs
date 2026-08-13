using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerFlowAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_plugin_server_ignores_aliased_lookalike_flow_attributes()
    {
        var foreignAllowNull = PluginServerGenerationTestDriver.CompileReference(
            """
            namespace System.Diagnostics.CodeAnalysis
            {
                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class AllowNullAttribute : System.Attribute;
            }
            """,
            "ForeignFlowAttributes");
        var (generated, outputCompilation, diagnostics) = PluginServerGenerationTestDriver.RunWithDiagnostics(
            """
            extern alias ForeignFlowAttributes;

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
                    ValueTask<string> ForeignAsync(
                        [ForeignFlowAttributes::System.Diagnostics.CodeAnalysis.AllowNull] string? value);

                    ValueTask<string> BclAsync([AllowNull] string? value);
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
                using Regression.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """,
            foreignAllowNull.WithProperties(
                MetadataReferenceProperties.Assembly.WithAliases(["ForeignFlowAttributes"])));

        Assert.Empty(diagnostics);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.DoesNotContain(
            "ForeignAsync([global::System.Diagnostics.CodeAnalysis.AllowNullAttribute]",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "BclAsync([global::System.Diagnostics.CodeAnalysis.AllowNullAttribute]",
            generated,
            StringComparison.Ordinal);
    }
}
