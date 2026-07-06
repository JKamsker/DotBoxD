namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerReturnFlowAttributeSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_preserves_return_flow_attributes_on_world_facade_members()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    [MaybeNull]
                    string CurrentTarget { get; }

                    [return: NotNullIfNotNull(nameof(value))]
                    string Echo(string value);
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        var normalized = NormalizeLineEndings(generated);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute]\n    public string CurrentTarget",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute(\"value\")]\n    public string Echo",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_preserves_return_flow_attributes_on_control_and_service_wrappers()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    [NotNull]
                    IMonsterControl Monsters { get; }
            """, """

                [RpcService]
                public interface IMonsterControl
                {
                    [MaybeNull]
                    string CurrentTarget { get; }

                    [return: NotNullIfNotNull(nameof(value))]
                    string Echo(string value);

                    IInventory Inventory { get; }
                }

                [RpcService]
                public interface IInventory
                {
                    [MaybeNull]
                    string Label { get; }

                    [return: MaybeNull]
                    string Find(string id);
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        var normalized = NormalizeLineEndings(generated);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.NotNullAttribute]\n    public global::Regression.Game.IMonsterControl Monsters",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute]\n        public string CurrentTarget",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute(\"value\")]\n        public string Echo",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute]\n            public string Label",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[return: global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute]\n            public string Find",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Inherited_control_properties_with_conflicting_flow_attributes_report_generator_diagnostic()
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
                    public interface ILeftWorld
                    {
                        [MaybeNull]
                        IMonsterControl Monsters { get; }
                    }

                    [RpcService]
                    public interface IRightWorld
                    {
                        [NotNull]
                        IMonsterControl Monsters { get; }
                    }

                    [RpcService]
                    public interface IGameWorldAccess : ILeftWorld, IRightWorld;

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

        var outputDiagnostics = outputCompilation.GetDiagnostics();
        var reportsControlFlowConflict = generatorDiagnostics.Any(
            diagnostic =>
            {
                var message = diagnostic.GetMessage();
                return diagnostic.Id == "DBXK100" &&
                       message.Contains("Monsters", StringComparison.Ordinal) &&
                       message.Contains("inherited", StringComparison.OrdinalIgnoreCase) &&
                       message.Contains("flow", StringComparison.OrdinalIgnoreCase);
            });
        var leaksAmbiguousGeneratedAccess = outputDiagnostics.Any(diagnostic => diagnostic.Id == "CS0229");

        Assert.True(
            reportsControlFlowConflict && !leaksAmbiguousGeneratedAccess,
            $"""
            Expected DBXK100 to reject inherited control property flow conflicts without leaking CS0229.

            Generator diagnostics:
            {string.Join("\n", generatorDiagnostics.Select(diagnostic => diagnostic.ToString()))}

            Output diagnostics:
            {string.Join("\n", outputDiagnostics.Select(diagnostic => diagnostic.ToString()))}
            """);
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ServerSource(string worldMembers, string extraGameTypes = "")
        => $$"""
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
                public interface IGameWorldAccess
                {
            {{worldMembers}}
                }
            {{extraGameTypes}}
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
            """;
}
