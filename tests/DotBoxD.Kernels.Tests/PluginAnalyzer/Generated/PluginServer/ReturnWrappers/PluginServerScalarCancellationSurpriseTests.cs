using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerScalarCancellationSurpriseTests
{
    [Fact]
    public async Task Generated_plugin_server_forwarders_observe_cancellation_before_returning_scalar_results()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var run = assembly.GetType("Sample.ScalarCancellationProbe", throwOnError: true)!
            .GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var result = await AwaitValueTaskResult(run.Invoke(null, null)!);

        Assert.True(GetBool(result, "WorldTokenWasCanceled"));
        Assert.True(GetBool(result, "WrapperTokenWasCanceled"));
        Assert.Equal(1, GetInt(result, "WorldCalls"));
        Assert.Equal(1, GetInt(result, "WrapperCalls"));

        var worldValue = GetNullableInt(result, "WorldValue");
        var wrapperValue = GetNullableInt(result, "WrapperValue");
        var worldThrewOperationCanceled = GetBool(result, "WorldThrewOperationCanceled");
        var wrapperThrewOperationCanceled = GetBool(result, "WrapperThrewOperationCanceled");
        Assert.True(
            worldThrewOperationCanceled &&
            wrapperThrewOperationCanceled &&
            worldValue is null &&
            wrapperValue is null,
            "Expected cancellation before scalar results. " +
            $"WorldThrew={worldThrewOperationCanceled}, " +
            $"WrapperThrew={wrapperThrewOperationCanceled}, " +
            $"WorldValue={worldValue?.ToString() ?? "<null>"}, " +
            $"WrapperValue={wrapperValue?.ToString() ?? "<null>"}.");
    }

    private static Assembly Emit(Microsoft.CodeAnalysis.Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static async Task<object> AwaitValueTaskResult(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static bool GetBool(object instance, string propertyName)
        => (bool)instance.GetType().GetProperty(propertyName)!.GetValue(instance)!;

    private static int GetInt(object instance, string propertyName)
        => (int)instance.GetType().GetProperty(propertyName)!.GetValue(instance)!;

    private static int? GetNullableInt(object instance, string propertyName)
        => (int?)instance.GetType().GetProperty(propertyName)!.GetValue(instance);

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
            public interface IInventory
            {
                ValueTask<int> CountAsync(CancellationToken ct = default);
            }

            [RpcService]
            public interface IGameWorldAccess
            {
                IInventory Inventory { get; }

                ValueTask<int> CountAsync(CancellationToken ct = default);
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

            public sealed record ScalarCancellationProbeResult(
                bool WorldThrewOperationCanceled,
                bool WrapperThrewOperationCanceled,
                int? WorldValue,
                int? WrapperValue,
                bool WorldTokenWasCanceled,
                bool WrapperTokenWasCanceled,
                int WorldCalls,
                int WrapperCalls);

            public static class ScalarCancellationProbe
            {
                public static async ValueTask<ScalarCancellationProbeResult> RunAsync()
                {
                    using var worldCts = new CancellationTokenSource();
                    using var wrapperCts = new CancellationTokenSource();
                    var inventory = new RecordingInventory(wrapperCts);
                    var world = new RecordingWorld(worldCts, inventory);
                    await using var server = new RemotePluginServer(new RecordingControlService(), world);

                    int? worldValue = null;
                    var worldThrewOperationCanceled = false;
                    try
                    {
                        worldValue = await server.CountAsync(worldCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        worldThrewOperationCanceled = true;
                    }

                    int? wrapperValue = null;
                    var wrapperThrewOperationCanceled = false;
                    try
                    {
                        wrapperValue = await server.Inventory.CountAsync(wrapperCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        wrapperThrewOperationCanceled = true;
                    }

                    return new ScalarCancellationProbeResult(
                        worldThrewOperationCanceled,
                        wrapperThrewOperationCanceled,
                        worldValue,
                        wrapperValue,
                        worldCts.IsCancellationRequested,
                        wrapperCts.IsCancellationRequested,
                        world.CountCalls,
                        inventory.CountCalls);
                }
            }

            public sealed class RecordingWorld : IGameWorldAccess
            {
                private readonly CancellationTokenSource _cts;

                public RecordingWorld(CancellationTokenSource cts, IInventory inventory)
                {
                    _cts = cts;
                    Inventory = inventory;
                }

                public IInventory Inventory { get; }

                public int CountCalls { get; private set; }

                public ValueTask<int> CountAsync(CancellationToken ct = default)
                {
                    CountCalls++;
                    if (!ct.CanBeCanceled)
                    {
                        throw new InvalidOperationException("Expected caller cancellation token.");
                    }

                    _cts.Cancel();
                    return ValueTask.FromResult(7);
                }
            }

            public sealed class RecordingInventory : IInventory
            {
                private readonly CancellationTokenSource _cts;

                public RecordingInventory(CancellationTokenSource cts)
                {
                    _cts = cts;
                }

                public int CountCalls { get; private set; }

                public ValueTask<int> CountAsync(CancellationToken ct = default)
                {
                    CountCalls++;
                    if (!ct.CanBeCanceled)
                    {
                        throw new InvalidOperationException("Expected caller cancellation token.");
                    }

                    _cts.Cancel();
                    return ValueTask.FromResult(11);
                }
            }

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
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sample.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new global::System.InvalidOperationException("not used");
            }
        }
        """;
}
