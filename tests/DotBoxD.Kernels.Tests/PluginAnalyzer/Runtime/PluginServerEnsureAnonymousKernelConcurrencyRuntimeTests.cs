using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

// EnsureAnonymousKernelAsync memoizes one install per plugin id. Two concurrent callers must each be able to
// cancel only their own wait: the shared install must not run under the first caller's token, or a second
// (uncancelled) caller observes a cancellation it never requested.
public sealed class PluginServerEnsureAnonymousKernelConcurrencyRuntimeTests
{
    [Fact]
    public async Task A_callers_cancellation_does_not_cancel_a_concurrent_uncancelled_caller()
    {
        var assembly = Compile(Source);
        var stubType = assembly.GetType("Sample.BlockingControlService", throwOnError: true)!;
        var stub = Activator.CreateInstance(stubType)!;
        var serverType = assembly.GetType("Concurrency.Plugin.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [stub, null])!;
        var ensure = serverType.GetMethod("EnsureAnonymousKernelAsync", BindingFlags.Public | BindingFlags.Instance)!;

        var package = BuildPackage("anon-1");
        Func<PluginPackage> factory = () => package;

        using var cancelledCaller = new CancellationTokenSource();
        var taskA = (Task<string>)ensure.Invoke(server, ["anon-1", factory, cancelledCaller.Token])!;

        // The first caller materializes the shared install; wait until it has reached the (blocked) control call.
        await (Task)stubType.GetProperty("EnteredTask")!.GetValue(stub)!;

        var taskB = (Task<string>)ensure.Invoke(server, ["anon-1", factory, CancellationToken.None])!;

        cancelledCaller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => taskA);

        // Release the shared install; the uncancelled caller must complete successfully.
        stubType.GetMethod("Complete")!.Invoke(stub, ["anon-1"]);
        var installedId = await taskB;

        Assert.Equal("anon-1", installedId);
    }

    private static PluginPackage BuildPackage(string id)
    {
        var module = new SandboxModule(
            "module",
            DotBoxD.Kernels.Model.SemVersion.One,
            DotBoxD.Kernels.Model.SemVersion.One,
            [],
            [],
            new Dictionary<string, string>());
        var manifest = new PluginManifest(
            id,
            "contract",
            ExecutionMode.Auto,
            [],
            [],
            []);
        return PluginPackage.Create(manifest, module);
    }

    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Concurrency.Game
        {
            [DotBoxDService]
            public interface IGameWorldAccess;
        }

        namespace Concurrency.Game.Ipc
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
                public static Concurrency.Game.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }

        namespace Concurrency.Plugin
        {
            using DotBoxD.Abstractions;
            using Concurrency.Game;

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;
        }

        namespace Sample
        {
            using Concurrency.Game.Ipc;

            public sealed class BlockingControlService : IGamePluginControlService
            {
                private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                private readonly TaskCompletionSource<string> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

                public Task EnteredTask => _entered.Task;
                public void Complete(string id) => _release.TrySetResult(id);

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                    => new ValueTask<string>(WaitForReleaseAsync(ct));

                private async Task<string> WaitForReleaseAsync(CancellationToken ct)
                {
                    _entered.TrySetResult(true);
                    using var registration = ct.Register(() => _release.TrySetCanceled(ct));
                    return await _release.Task.ConfigureAwait(false);
                }

                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default) => default;
                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default) => default;
                public ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default) => default;
                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => default;
                public ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, CancellationToken cancellationToken = default) => default;
            }
        }
        """;

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerEnsureAnonymousConcurrencyTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GeneratePluginServerAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));
}
