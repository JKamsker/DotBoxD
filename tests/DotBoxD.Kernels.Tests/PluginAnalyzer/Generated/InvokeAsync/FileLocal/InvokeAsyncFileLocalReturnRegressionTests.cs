using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncFileLocalReturnRegressionTests
{
    [Fact]
    public void File_local_return_dto_reports_InvokeAsync_diagnostic_without_generated_compiler_leak()
    {
        var diagnostics = RunGeneratorAndGetErrorDiagnostics("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Kernels.Game.Plugin.Client;
            using DotBoxD.Kernels.Game.Server.Abstractions;

            namespace DotBoxD.Kernels.Game.Server.Abstractions
            {
                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    int GetHealth(string entityId);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
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

            namespace DotBoxD.Kernels.Game.Plugin.Client
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }

            namespace Sample
            {
                file sealed record Secret(int Health);

                public static class Usage
                {
                    public static async ValueTask<int> Run(RemotePluginServer kernels)
                    {
                        var value = await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                        {
                            return new Secret(world.GetHealth("monster-1"));
                        });
                        return value.Health;
                    }
                }
            }
            """);

        var hasFocusedDiagnostic = diagnostics.Any(diagnostic =>
            diagnostic.Id == "DBXK100" &&
            diagnostic.GetMessage().Contains("Secret", StringComparison.Ordinal) &&
            (diagnostic.GetMessage().Contains("file-local", StringComparison.OrdinalIgnoreCase) ||
             diagnostic.GetMessage().Contains("accessible from generated code", StringComparison.Ordinal)));
        var compilerLeaks = diagnostics
            .Where(diagnostic => diagnostic.Id is "CS0234" or "CS9051" &&
                diagnostic.GetMessage().Contains("Secret", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            hasFocusedDiagnostic && compilerLeaks.Length == 0,
            "Expected a focused DBXK100 diagnostic for the file-local InvokeAsync return DTO " +
            $"without raw generated compiler diagnostics, but saw:{Environment.NewLine}{Format(diagnostics)}");
    }

    private static string Format(IEnumerable<Diagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
}
