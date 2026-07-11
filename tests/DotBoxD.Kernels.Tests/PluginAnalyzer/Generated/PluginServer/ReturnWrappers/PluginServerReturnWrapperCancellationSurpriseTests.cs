using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerReturnWrapperCancellationSurpriseTests
{
    [Fact]
    public async Task Generated_service_wrapper_observes_cancellation_before_materializing_return_wrapper()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var run = assembly.GetType("Sample.ReturnWrapperCancellationProbe", throwOnError: true)!
            .GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var result = await AwaitValueTaskResult(run.Invoke(null, null)!);

        Assert.True(GetBool(result, "TokenWasCanceled"));
        Assert.Equal(1, GetInt(result, "OpenCalls"));
        Assert.False(GetBool(result, "ReturnedWrapper"));
        Assert.True(GetBool(result, "ThrewOperationCanceled"), GetString(result, "ReturnedTypeName"));
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

    private static string? GetString(object instance, string propertyName)
        => (string?)instance.GetType().GetProperty(propertyName)!.GetValue(instance);

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
                ValueTask<IInventory> OpenInventoryAsync(CancellationToken ct = default);
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

            public sealed record ReturnWrapperCancellationProbeResult(
                bool ThrewOperationCanceled,
                bool ReturnedWrapper,
                bool TokenWasCanceled,
                int OpenCalls,
                string? ReturnedTypeName);

            public static class ReturnWrapperCancellationProbe
            {
                public static async ValueTask<ReturnWrapperCancellationProbeResult> RunAsync()
                {
                    using var cts = new CancellationTokenSource();
                    var world = new RecordingWorld(cts);
                    await using var server = new RemotePluginServer(new RecordingControlService(), world);

                    IInventory? returned = null;
                    var threwOperationCanceled = false;
                    try
                    {
                        returned = await server.OpenInventoryAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        threwOperationCanceled = true;
                    }

                    return new ReturnWrapperCancellationProbeResult(
                        threwOperationCanceled,
                        returned is not null,
                        cts.IsCancellationRequested,
                        world.OpenCalls,
                        returned?.GetType().FullName);
                }
            }

            public sealed class RecordingWorld : IGameWorldAccess
            {
                private readonly CancellationTokenSource _cts;

                public RecordingWorld(CancellationTokenSource cts)
                {
                    _cts = cts;
                }

                public int OpenCalls { get; private set; }

                public ValueTask<IInventory> OpenInventoryAsync(CancellationToken ct = default)
                {
                    OpenCalls++;
                    if (!ct.CanBeCanceled)
                    {
                        throw new InvalidOperationException("Expected caller cancellation token.");
                    }

                    _cts.Cancel();
                    return ValueTask.FromResult<IInventory>(new RecordingInventory());
                }
            }

            public sealed class RecordingInventory : IInventory
            {
                public ValueTask<int> CountAsync(CancellationToken ct = default)
                    => ValueTask.FromResult(7);
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
