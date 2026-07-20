using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Fact]
    public async Task Generated_plugin_server_concurrent_disposal_observes_in_flight_cleanup_failure()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System;
            using System.Buffers;
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
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => new Regression.Plugin.RecordingWorld();
                }
            }

            namespace Regression.Plugin
            {
                using DotBoxD.Codecs.MessagePack;
                using DotBoxD.Services.Buffers;
                using DotBoxD.Services.Generated;
                using DotBoxD.Services.Peer;
                using DotBoxD.Services.Serialization;
                using DotBoxD.Services.Server;
                using DotBoxD.Services.Transport;
                using Regression.Game;
                using Regression.Game.Ipc;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;

                public sealed class RecordingWorld : IGameWorldAccess;

                public sealed record DisposeConcurrencyProbeResult(
                    bool SecondStillWaitingBeforeRelease,
                    bool FirstObservedCleanupFailure,
                    bool SecondObservedCleanupFailure);

                public static class DisposeConcurrencyProbe
                {
                    public static async Task<DisposeConcurrencyProbeResult> RunAsync()
                    {
                        RegisterControlService();

                        var transport = new GatedFailingDisposeTransport();
                        var server = new RemotePluginServer((_, _) =>
                            new ValueTask<RpcPeerSession>(ConnectAsync(transport)));

                        await server.StartAsync().ConfigureAwait(false);

                        var first = server.DisposeAsync().AsTask();
                        await transport.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                        var second = server.DisposeAsync().AsTask();
                        await Task.Delay(100).ConfigureAwait(false);
                        var secondStillWaitingBeforeRelease = !second.IsCompleted;

                        transport.Release(new InvalidOperationException("transport cleanup failed"));

                        var firstObservedCleanupFailure = await ObservedCleanupFailureAsync(first).ConfigureAwait(false);
                        var secondObservedCleanupFailure = await ObservedCleanupFailureAsync(second).ConfigureAwait(false);

                        return new DisposeConcurrencyProbeResult(
                            secondStillWaitingBeforeRelease,
                            firstObservedCleanupFailure,
                            secondObservedCleanupFailure);
                    }

                    private static void RegisterControlService()
                    {
                        GeneratedServiceRegistry.Register<IGamePluginControlService>(
                            _ => new ControlServiceProxy(),
                            _ => new NoopDispatcher());
                    }

                    private static Task<RpcPeerSession> ConnectAsync(GatedFailingDisposeTransport transport)
                        => RpcPeerSession.ConnectAsync(
                            transport,
                            new MessagePackRpcSerializer(),
                            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) },
                            CancellationToken.None);

                    private static async Task<bool> ObservedCleanupFailureAsync(Task task)
                    {
                        try
                        {
                            await task.ConfigureAwait(false);
                            return false;
                        }
                        catch (InvalidOperationException ex) when (
                            ex.Message.Contains("transport cleanup failed", StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }

                public sealed class ControlServiceProxy : IGamePluginControlService
                {
                    public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default) =>
                        throw new NotSupportedException();

                    public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default) =>
                        throw new NotSupportedException();

                    public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default) =>
                        throw new NotSupportedException();

                    public ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default) =>
                        throw new NotSupportedException();

                    public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) =>
                        throw new NotSupportedException();

                    public ValueTask<byte[]> InvokeServerExtensionAsync(
                        string pluginId,
                        byte[] arguments,
                        CancellationToken cancellationToken = default) =>
                        throw new NotSupportedException();
                }

                public sealed class NoopDispatcher : IServiceDispatcher
                {
                    public string ServiceName => "GamePluginControl";

                    public Task DispatchAsync(
                        string method,
                        ReadOnlyMemory<byte> payload,
                        ISerializer serializer,
                        IInstanceRegistry registry,
                        IBufferWriter<byte> output,
                        CancellationToken ct = default) =>
                        throw new NotSupportedException();
                }

                public sealed class GatedFailingDisposeTransport : ITransport
                {
                    private readonly GatedChannel _channel = new();
                    private readonly TaskCompletionSource _disposeEntered =
                        new(TaskCreationOptions.RunContinuationsAsynchronously);
                    private readonly TaskCompletionSource<Exception?> _release =
                        new(TaskCreationOptions.RunContinuationsAsynchronously);
                    private int _connected;

                    public Task DisposeEntered => _disposeEntered.Task;

                    public IRpcChannel? Connection => Volatile.Read(ref _connected) == 0 ? null : _channel;

                    public bool IsConnected => Volatile.Read(ref _connected) != 0 && _channel.IsConnected;

                    public Task ConnectAsync(CancellationToken ct = default)
                    {
                        ct.ThrowIfCancellationRequested();
                        Volatile.Write(ref _connected, 1);
                        return Task.CompletedTask;
                    }

                    public void Release(Exception exception)
                        => _release.TrySetResult(exception);

                    public async ValueTask DisposeAsync()
                    {
                        Volatile.Write(ref _connected, 0);
                        _disposeEntered.TrySetResult();
                        var exception = await _release.Task.ConfigureAwait(false);
                        if (exception is not null)
                        {
                            throw exception;
                        }
                    }
                }

                public sealed class GatedChannel : IRpcChannel
                {
                    private readonly TaskCompletionSource _disposed =
                        new(TaskCreationOptions.RunContinuationsAsynchronously);
                    private int _disposedFlag;

                    public bool IsConnected => Volatile.Read(ref _disposedFlag) == 0;

                    public string RemoteEndpoint => "test://generated-dispose-concurrency";

                    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
                    {
                        ct.ThrowIfCancellationRequested();
                        return Task.CompletedTask;
                    }

                    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
                    {
                        await _disposed.Task.WaitAsync(ct).ConfigureAwait(false);
                        return Payload.Empty;
                    }

                    public ValueTask DisposeAsync()
                    {
                        Volatile.Write(ref _disposedFlag, 1);
                        _disposed.TrySetResult();
                        return default;
                    }
                }
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        var assembly = Emit(outputCompilation);
        var probe = assembly.GetType("Regression.Plugin.DisposeConcurrencyProbe", throwOnError: true)!;
        var method = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var resultTask = Assert.IsAssignableFrom<Task>(method.Invoke(null, null));
        await resultTask;
        var resultValue = resultTask.GetType().GetProperty("Result")!.GetValue(resultTask)!;

        Assert.True(ReadBool(resultValue, "SecondStillWaitingBeforeRelease"));
        Assert.True(ReadBool(resultValue, "FirstObservedCleanupFailure"));
        Assert.True(ReadBool(resultValue, "SecondObservedCleanupFailure"));
    }
}
