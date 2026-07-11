using System.Reflection;
using System.Runtime.ExceptionServices;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerRetainedWrapperDisposalSurpriseTests
{
    [Fact]
    public async Task Retained_service_wrapper_rejects_calls_after_server_disposal()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var control = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var world = Activator.CreateInstance(assembly.GetType("Sample.RecordingWorld", throwOnError: true)!)!;
        var serverType = assembly.GetType("Sample.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [control, world])!;
        var inventory = serverType.GetProperty("Inventory", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(server)!;

        await DisposeAsync(server);

        var count = inventory.GetType().GetMethod("CountAsync", BindingFlags.Public | BindingFlags.Instance)!;
        var exception = await Record.ExceptionAsync(() =>
            InvokeValueTaskResult<int>(count, inventory, CancellationToken.None));

        Assert.Equal(0, ReadInventoryCallCount(world));
        Assert.IsType<ObjectDisposedException>(exception);
    }

    private static Assembly Emit(Microsoft.CodeAnalysis.Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static async Task DisposeAsync(object server)
    {
        var valueTask = server.GetType().GetMethod("DisposeAsync", Type.EmptyTypes)!.Invoke(server, null)!;
        await AwaitValueTask(valueTask);
    }

    private static async Task AwaitValueTask(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        await ((Task)asTask.Invoke(valueTask, null)!).ConfigureAwait(false);
    }

    private static async Task<T> AwaitValueTaskResult<T>(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        return await ((Task<T>)asTask.Invoke(valueTask, null)!).ConfigureAwait(false);
    }

    private static async Task<T> InvokeValueTaskResult<T>(MethodInfo method, object target, params object?[] args)
    {
        try
        {
            return await AwaitValueTaskResult<T>(method.Invoke(target, args)!).ConfigureAwait(false);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static int ReadInventoryCallCount(object world)
    {
        var inventory = world.GetType().GetProperty("InventoryService", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(world)!;
        return (int)inventory.GetType().GetProperty("CountCalls", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(inventory)!;
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample
        {
            [RpcService]
            public interface IInventoryControl
            {
                ValueTask<int> CountAsync(CancellationToken ct = default);
            }

            [RpcService]
            public interface IGameWorldAccess
            {
                IInventoryControl Inventory { get; }
            }

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

            [GeneratePluginServer(
                Context = typeof(RemotePluginContext),
                ControlService = typeof(IGamePluginControlService))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            public sealed class RecordingControlService : IGamePluginControlService
            {
                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("plugin-id");

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("plugin-id");

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("extension-id");

                public ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default)
                    => ValueTask.CompletedTask;

                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                    => ValueTask.CompletedTask;

                public ValueTask<byte[]> InvokeServerExtensionAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(Array.Empty<byte>());
            }

            public sealed class RecordingWorld : IGameWorldAccess
            {
                public InventoryControl InventoryService { get; } = new();
                public IInventoryControl Inventory => InventoryService;
            }

            public sealed class InventoryControl : IInventoryControl
            {
                public int CountCalls { get; private set; }

                public ValueTask<int> CountAsync(CancellationToken ct = default)
                {
                    CountCalls++;
                    return ValueTask.FromResult(7);
                }
            }
        }

        namespace Sample.Ipc
        {
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Services.Attributes;

            [RpcService]
            public interface IPluginEventCallback
            {
                ValueTask OnEventAsync(string subscriptionId, ReadOnlyMemory<byte> projectedValue, CancellationToken ct = default);
                ValueTask<byte[]> OnResultAsync(string subscriptionId, ReadOnlyMemory<byte> contextValue, CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sample.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new InvalidOperationException("not used");

                public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                    DotBoxD.Services.Peer.RpcPeer peer,
                    Sample.Ipc.IPluginEventCallback implementation)
                    => peer;
            }
        }
        """;
}
