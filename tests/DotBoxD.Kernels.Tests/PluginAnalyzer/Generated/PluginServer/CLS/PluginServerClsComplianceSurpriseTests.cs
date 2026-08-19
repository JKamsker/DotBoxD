using System.Collections.Immutable;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerClsComplianceSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_facade_does_not_emit_cls_warnings()
    {
        var (generated, outputCompilation, generatorDiagnostics) = PluginServerGenerationTestDriver.RunWithDiagnostics("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            [assembly: CLSCompliant(true)]

            namespace Cls.Game
            {
                [RpcService]
                public interface IGameWorldAccess
                {
                    ValueTask<int> RollAsync(int sides, CancellationToken ct = default);
                }
            }

            namespace Cls.Game.Ipc
            {
                [CLSCompliant(false)]
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                [CLSCompliant(false)]
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

                [CLSCompliant(false)]
                [RpcService]
                public interface IPluginEventCallback
                {
                    ValueTask OnEventAsync(
                        string subscriptionId,
                        ReadOnlyMemory<byte> projectedValue,
                        CancellationToken ct = default);

                    ValueTask<byte[]> OnResultAsync(
                        string subscriptionId,
                        ReadOnlyMemory<byte> contextValue,
                        CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                [CLSCompliant(false)]
                public static class DotBoxDGeneratedExtensions
                {
                    public static Cls.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");

                    public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                        DotBoxD.Services.Peer.RpcPeer peer,
                        Cls.Game.Ipc.IPluginEventCallback implementation)
                        => peer;
                }
            }

            namespace Cls.Plugin
            {
                using DotBoxD.Abstractions;
                using Cls.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Empty(generatorDiagnostics);

        var clsDiagnostics = outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id is "CS3001" or "CS3002" or "CS3003")
            .ToArray();

        Assert.Empty(clsDiagnostics);
        Assert.Contains("[global::System.CLSCompliant(false)]", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_ignores_extern_aliased_lookalike_cls_compliance_attribute()
    {
        var foreignClsCompliant = PluginServerGenerationTestDriver.CompileReference(
            """
            namespace System;

            [AttributeUsage(AttributeTargets.Assembly)]
            public sealed class CLSCompliantAttribute : Attribute
            {
                public CLSCompliantAttribute(bool isCompliant)
                {
                }
            }
            """,
            "ForeignClsCompliant")
            .WithAliases(ImmutableArray.Create("Foreign"));

        var (generated, outputCompilation, generatorDiagnostics) = PluginServerGenerationTestDriver.RunWithDiagnostics(
            """
            extern alias Foreign;

            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            [assembly: Foreign::System.CLSCompliant(true)]

            namespace Cls.Game
            {
                [RpcService]
                public interface IGameWorldAccess
                {
                    ValueTask<int> RollAsync(int sides, CancellationToken ct = default);
                }
            }

            namespace Cls.Game.Ipc
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
                    public static Cls.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace Cls.Plugin
            {
                using Cls.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """,
            foreignClsCompliant);

        Assert.Empty(generatorDiagnostics);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.DoesNotContain("[global::System.CLSCompliant(false)]", generated, StringComparison.Ordinal);
    }
}
