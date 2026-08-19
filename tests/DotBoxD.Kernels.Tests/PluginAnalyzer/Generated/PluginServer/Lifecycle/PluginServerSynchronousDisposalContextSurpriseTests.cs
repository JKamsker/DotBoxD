using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerSynchronousDisposalContextSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_synchronous_disposal_does_not_block_caller_context()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains(
            "public void Dispose() => global::System.Threading.Tasks.Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_plugin_server_synchronous_disposal_after_async_disposal_completes_without_pumping_caller_context()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var serverType = assembly.GetType("Regression.Plugin.RemotePluginServer", throwOnError: true)!;
        var control = Activator.CreateInstance(assembly.GetType("Regression.Game.Ipc.NoopControlService", throwOnError: true)!)!;
        var server = Activator.CreateInstance(serverType, [control, null])!;

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalThread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());

            try
            {
                DisposeAsync(server).GetAwaiter().GetResult();
                serverType.GetMethod("Dispose", Type.EmptyTypes)!.Invoke(server, null);
                completed.SetResult();
            }
            catch (Exception exception)
            {
                completed.SetException(exception);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        });

        disposalThread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(disposalThread.Join(TimeSpan.FromSeconds(5)));
    }

    private static Assembly Emit(Microsoft.CodeAnalysis.Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static async Task DisposeAsync(object server)
    {
        var valueTask = server.GetType().GetMethod("DisposeAsync", Type.EmptyTypes)!.Invoke(server, null)!;
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!.Invoke(valueTask, null)!;
        await ((Task)asTask).ConfigureAwait(false);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
        }
    }

    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Regression.Game
        {
            [RpcService]
            public interface IGameWorldAccess;
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

            public sealed class NoopControlService : IGamePluginControlService
            {
                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default) => ValueTask.FromResult(string.Empty);
                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default) => ValueTask.FromResult(string.Empty);
                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default) => ValueTask.FromResult(string.Empty);
                public ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default) => ValueTask.CompletedTask;
                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
                public ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, CancellationToken ct = default) => ValueTask.FromResult(global::System.Array.Empty<byte>());
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                    DotBoxD.Services.Peer.RpcPeer peer) => throw new System.NotSupportedException();
            }
        }

        namespace Regression.Plugin
        {
            using Regression.Game;

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;
        }
        """;
}
