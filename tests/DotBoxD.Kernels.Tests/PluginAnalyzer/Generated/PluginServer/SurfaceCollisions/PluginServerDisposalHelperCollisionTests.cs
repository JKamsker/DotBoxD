using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerDisposalHelperCollisionTests
{
    [Fact]
    public void World_method_named_like_generated_disposal_helper_reports_dbxk100()
    {
        var (generated, outputCompilation, generatorDiagnostics) =
            PluginServerGenerationTestDriver.RunWithDiagnostics("""
                using System.Threading;
                using System.Threading.Tasks;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using DotBoxD.Services.Attributes;

                namespace DisposalHelperCollision.Game
                {
                    [RpcService]
                    public interface IGameWorldAccess
                    {
                        Task GetOrStartDisposeAsync();
                    }
                }

                namespace DisposalHelperCollision.Game.Ipc
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
                        public static DisposalHelperCollision.Game.IGameWorldAccess GetGameWorldAccess(
                            DotBoxD.Services.Peer.RpcPeer peer)
                            => throw new System.InvalidOperationException("not used");
                    }
                }

                namespace DisposalHelperCollision.Plugin
                {
                    using DotBoxD.Abstractions;
                    using DisposalHelperCollision.Game;

                    [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                    public partial class RemotePluginServer : IGameWorldAccess;

                    public sealed partial class RemotePluginContext;
                }
                """);

        var inputTree = outputCompilation.SyntaxTrees.Single(
            tree => tree.ToString().Contains("namespace DisposalHelperCollision.Game", StringComparison.Ordinal));
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.Location.SourceTree == inputTree);

        var hasCollisionDiagnostic = generatorDiagnostics.Any(
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("collides with the generated facade surface", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("GetOrStartDisposeAsync", StringComparison.Ordinal));
        var rawGeneratedCollisions = outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id == "CS0111" &&
                                 diagnostic.GetMessage().Contains("GetOrStartDisposeAsync", StringComparison.Ordinal) &&
                                 diagnostic.Location.SourceTree != inputTree)
            .ToArray();

        Assert.True(
            hasCollisionDiagnostic && rawGeneratedCollisions.Length == 0,
            $"""
            Expected a focused DBXK100 GetOrStartDisposeAsync collision diagnostic and no raw generated CS0111.
            Has DBXK100: {hasCollisionDiagnostic}
            Raw generated collisions: {rawGeneratedCollisions.Length}
            Generated source contains private GetOrStartDisposeAsync helper: {generated.Contains("private global::System.Threading.Tasks.Task GetOrStartDisposeAsync()", StringComparison.Ordinal)}
            Diagnostics:
            {string.Join("\n", generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).Select(diagnostic => diagnostic.ToString()))}
            """);
    }
}
