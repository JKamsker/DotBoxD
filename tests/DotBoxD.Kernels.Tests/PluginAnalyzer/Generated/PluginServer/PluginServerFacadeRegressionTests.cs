using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerFacadeRegressionTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_server_includes_inherited_controls_and_wraps_async_handles()
    {
        var (result, outputCompilation) = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [DotBoxDService]
                public interface IGameWorldBase
                {
                    IMonsterControl Monsters { get; }
                }

                [DotBoxDService]
                public interface IGameWorldAccess : IGameWorldBase;

                [DotBoxDService]
                public interface IMonsterControl
                {
                    ValueTask<IMonster> GetAsync(string entityId);
                }

                [DotBoxDService]
                public interface IMonster
                {
                    string Id { get; }
                    ValueTask<int> GetHealthAsync();
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
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Contains("public partial class RemotePluginContext", generated, StringComparison.Ordinal);
        Assert.Contains("RemotePluginHookRegistry Hooks { get; }", generated, StringComparison.Ordinal);
        Assert.Contains("RemotePluginSubscriptionRegistry Subscriptions { get; }", generated, StringComparison.Ordinal);
        Assert.Contains("public global::Regression.Game.IMonsterControl Monsters", generated, StringComparison.Ordinal);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask<global::Regression.Game.IMonster> GetAsync", generated, StringComparison.Ordinal);
        Assert.Contains("new MonsterPluginService(_owner, await _inner.GetAsync", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_disambiguates_same_simple_name_returned_services()
    {
        // Two [DotBoxDService] interfaces named IMonster live in different namespaces and are both returned from
        // one control. Returned-service wrapper classes are named from the SIMPLE type name and emitted as nested
        // siblings, so without disambiguation both would become `class MonsterPluginService` (CS0102). The model
        // dedups by FULL name, so they are two distinct wrappers; their emitted names must differ.
        var (result, outputCompilation) = RunGenerator("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Collision.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    IMonsterControl Monsters { get; }
                }

                [DotBoxDService]
                public interface IMonsterControl
                {
                    ValueTask<Collision.Game.Alpha.IMonster> GetAlphaAsync(string id);
                    ValueTask<Collision.Game.Beta.IMonster> GetBetaAsync(string id);
                }
            }

            namespace Collision.Game.Alpha
            {
                [DotBoxDService]
                public interface IMonster
                {
                    ValueTask<int> GetHealthAsync();
                }
            }

            namespace Collision.Game.Beta
            {
                [DotBoxDService]
                public interface IMonster
                {
                    ValueTask<int> GetLevelAsync();
                }
            }

            namespace Collision.Game.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, System.Threading.CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, System.Threading.CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, System.Threading.CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        System.Threading.CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(System.Threading.CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Collision.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Collision.Plugin
            {
                using DotBoxD.Abstractions;
                using Collision.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        // Match the class declarations precisely (": " follows the wrapper name) so the first assertion cannot be
        // satisfied by the disambiguated "_2" name as a prefix substring.
        Assert.Contains("class MonsterPluginService :", generated, StringComparison.Ordinal);
        Assert.Contains("class MonsterPluginService_2 :", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_uses_disambiguated_world_proxy_suffix()
    {
        var (result, outputCompilation) = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Collision.One
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace Collision.Two
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace Collision.Two.Ipc
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
                    public static Collision.Two.IGameWorldAccess GetCollision_Two_GameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Collision.Plugin
            {
                using DotBoxD.Abstractions;
                using Collision.Two;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Contains("DotBoxDGeneratedExtensions.GetCollision_Two_GameWorldAccess", generated, StringComparison.Ordinal);
    }

    private static (GeneratorDriverRunResult Result, Compilation OutputCompilation) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerRegressionTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GeneratePluginServerAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return (driver.GetRunResult(), outputCompilation);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
