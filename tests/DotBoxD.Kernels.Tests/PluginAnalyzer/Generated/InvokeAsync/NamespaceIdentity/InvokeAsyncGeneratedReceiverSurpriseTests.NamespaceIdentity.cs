using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class InvokeAsyncGeneratedReceiverSurpriseTests
{
    [Fact]
    public void Qualified_same_named_builder_locals_preserve_facade_namespace_identity()
    {
        var result = RunGeneratorAndAssertCompiles(TwoNamespaceBuilderSource);
        var packages = result.GeneratedTrees
            .Select(tree => tree.ToString())
            .Where(source => source.Contains(
                "\\\"contract\\\":\\\"AnonymousInvokeAsync\\\"",
                StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Equal(2, packages.Length);
        Assert.Contains(packages, source => source.Contains("host.alpha.read", StringComparison.Ordinal));
        Assert.Contains(packages, source => source.Contains("host.beta.read", StringComparison.Ordinal));
    }

    private const string TwoNamespaceBuilderSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Shared
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IPluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
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

        namespace Alpha
        {
            [RpcService]
            public interface IAlphaWorldAccess
            {
                [HostBinding("host.alpha.read", "alpha.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Read(string id);
            }

            [GeneratePluginServer(
                Context = typeof(RemotePluginContext),
                ControlService = typeof(Shared.IPluginControlService))]
            public partial class RemotePluginServer : IAlphaWorldAccess;

            public sealed partial class RemotePluginContext;
        }

        namespace Beta
        {
            [RpcService]
            public interface IBetaWorldAccess
            {
                [HostBinding("host.beta.read", "beta.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Read(string id);
            }

            [GeneratePluginServer(
                Context = typeof(RemotePluginContext),
                ControlService = typeof(Shared.IPluginControlService))]
            public partial class RemotePluginServer : IBetaWorldAccess;

            public sealed partial class RemotePluginContext;
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Alpha.IAlphaWorldAccess GetAlphaWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new InvalidOperationException("not used");

                public static Beta.IBetaWorldAccess GetBetaWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new InvalidOperationException("not used");
            }
        }

        namespace Sample
        {
            public static class Usage
            {
                public static ValueTask<int> RunAlpha(Shared.IPluginControlService control)
                {
                    var server = Alpha.RemotePluginServerBuilder.FromConnection(control).Build();
                    return server.InvokeAsync(async (Alpha.IAlphaWorldAccess world) =>
                    {
                        return world.Read("alpha");
                    });
                }

                public static ValueTask<int> RunBeta(Shared.IPluginControlService control)
                {
                    var server = Beta.RemotePluginServerBuilder.FromConnection(control).Build();
                    return server.InvokeAsync(async (Beta.IBetaWorldAccess world) =>
                    {
                        return world.Read("beta");
                    });
                }
            }
        }
        """;
}
