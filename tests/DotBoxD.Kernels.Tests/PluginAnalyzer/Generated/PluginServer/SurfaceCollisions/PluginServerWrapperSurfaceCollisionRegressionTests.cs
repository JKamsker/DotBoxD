using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerWrapperSurfaceCollisionRegressionTests
{
    [Fact]
    public void Control_wrapper_member_named_like_generated_backing_field_reports_dbxk100()
    {
        var (generated, outputCompilation, generatorDiagnostics) =
            PluginServerGenerationTestDriver.RunWithDiagnostics("""
                using System.Threading;
                using System.Threading.Tasks;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using DotBoxD.Services.Attributes;

                namespace WrapperCollision.Game
                {
                    [RpcService]
                    public interface IGameWorldAccess
                    {
                        IMonsterControl Monsters { get; }
                    }

                    [RpcService]
                    public interface IMonsterControl
                    {
                        int _owner { get; }
                        int _inner { get; }
                    }
                }

                namespace WrapperCollision.Game.Ipc
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
                        public static WrapperCollision.Game.IGameWorldAccess GetGameWorldAccess(
                            DotBoxD.Services.Peer.RpcPeer peer)
                            => throw new System.InvalidOperationException("not used");
                    }
                }

                namespace WrapperCollision.Plugin
                {
                    using DotBoxD.Abstractions;
                    using WrapperCollision.Game;

                    [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                    public partial class RemotePluginServer : IGameWorldAccess;

                    public sealed partial class RemotePluginContext;
                }
                """);

        var inputTree = outputCompilation.SyntaxTrees.Single(
            tree => tree.ToString().Contains("namespace WrapperCollision.Game", StringComparison.Ordinal));
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.Location.SourceTree == inputTree);

        var hasCollisionDiagnostic = generatorDiagnostics.Any(
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("collides", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("_owner", StringComparison.Ordinal));
        var hasRawGeneratedCollision = outputCompilation.GetDiagnostics().Any(
            diagnostic => diagnostic.Id == "CS0102" &&
                          diagnostic.GetMessage().Contains("_owner", StringComparison.Ordinal));

        Assert.True(
            hasCollisionDiagnostic && !hasRawGeneratedCollision,
            $"""
            Expected a focused DBXK100 wrapper backing-field collision diagnostic and no raw CS0102.
            Has DBXK100: {hasCollisionDiagnostic}
            Has raw CS0102: {hasRawGeneratedCollision}
            Generated wrapper source contains MonstersPluginControl: {generated.Contains("MonstersPluginControl", StringComparison.Ordinal)}
            Diagnostics:
            {string.Join("\n", generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).Select(d => d.ToString()))}
            """);
    }
}
