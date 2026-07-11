using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Fact]
    public async Task Generated_plugin_server_mid_connect_cancellation_does_not_publish_started_facade()
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
                public partial class RemotePluginServer : IGameWorldAccess
                {
                    private int _configuredCalls;

                    public int ConfiguredCalls => Volatile.Read(ref _configuredCalls);

                    partial void OnConfigured()
                        => Interlocked.Increment(ref _configuredCalls);
                }

                public sealed partial class RemotePluginContext;

                public sealed class RecordingWorld : IGameWorldAccess;

                public sealed record StartCancellationProbeResult(
                    bool ThrewOperationCanceled,
                    bool SessionDisposedBeforeFacadeDispose,
                    int ConfiguredCalls,
                    bool HooksRejectedAsNotStarted);

                public static class StartCancellationProbe
                {
                    public static async Task<StartCancellationProbeResult> RunAsync()
                    {
                        RegisterControlService();

                        using var cts = new CancellationTokenSource();
                        var transport = new TrackingTransport();
                        var server = new RemotePluginServer((_, _) =>
                            new ValueTask<RpcPeerSession>(ConnectAndCancelAsync(transport, cts)));

                        var threwOperationCanceled = false;
                        try
                        {
                            await server.StartAsync(cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            threwOperationCanceled = true;
                        }

                        var sessionDisposedBeforeFacadeDispose = transport.DisposeCalls > 0;
                        var hooksRejectedAsNotStarted = ThrowsNotStarted(() => _ = server.Hooks);

                        try
                        {
                            return new StartCancellationProbeResult(
                                threwOperationCanceled,
                                sessionDisposedBeforeFacadeDispose,
                                server.ConfiguredCalls,
                                hooksRejectedAsNotStarted);
                        }
                        finally
                        {
                            await server.DisposeAsync().ConfigureAwait(false);
                        }
                    }

                    private static void RegisterControlService()
                    {
                        GeneratedServiceRegistry.Register<IGamePluginControlService>(
                            _ => new ControlServiceProxy(),
                            _ => new NoopDispatcher());
                    }

                    private static async Task<RpcPeerSession> ConnectAndCancelAsync(
                        TrackingTransport transport,
                        CancellationTokenSource cts)
                    {
                        var session = await RpcPeerSession.ConnectAsync(
                            transport,
                            new MessagePackRpcSerializer(),
                            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) },
                            CancellationToken.None).ConfigureAwait(false);
                        cts.Cancel();
                        return session;
                    }

                    private static bool ThrowsNotStarted(Action action)
                    {
                        try
                        {
                            action();
                            return false;
                        }
                        catch (InvalidOperationException ex) when (
                            ex.Message.Contains("Call StartAsync()", StringComparison.Ordinal))
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

                public sealed class TrackingTransport : ITransport
                {
                    private readonly TrackingChannel _channel = new();
                    private int _connected;
                    private int _disposeCalls;

                    public int DisposeCalls => Volatile.Read(ref _disposeCalls);

                    public IRpcChannel? Connection => Volatile.Read(ref _connected) == 0 ? null : _channel;

                    public bool IsConnected => Volatile.Read(ref _connected) != 0 && _channel.IsConnected;

                    public Task ConnectAsync(CancellationToken ct = default)
                    {
                        ct.ThrowIfCancellationRequested();
                        Volatile.Write(ref _connected, 1);
                        return Task.CompletedTask;
                    }

                    public async ValueTask DisposeAsync()
                    {
                        Interlocked.Increment(ref _disposeCalls);
                        Volatile.Write(ref _connected, 0);
                        await _channel.DisposeAsync().ConfigureAwait(false);
                    }
                }

                public sealed class TrackingChannel : IRpcChannel
                {
                    private readonly TaskCompletionSource _disposed =
                        new(TaskCreationOptions.RunContinuationsAsynchronously);
                    private int _disposedFlag;

                    public bool IsConnected => Volatile.Read(ref _disposedFlag) == 0;

                    public string RemoteEndpoint => "test://generated-start-cancel";

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
        var probe = assembly.GetType("Regression.Plugin.StartCancellationProbe", throwOnError: true)!;
        var method = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var resultTask = Assert.IsAssignableFrom<Task>(method.Invoke(null, null));
        await resultTask;
        var resultValue = resultTask.GetType().GetProperty("Result")!.GetValue(resultTask)!;

        Assert.True(ReadBool(resultValue, "ThrewOperationCanceled"));
        Assert.True(ReadBool(resultValue, "SessionDisposedBeforeFacadeDispose"));
        Assert.Equal(0, ReadInt(resultValue, "ConfiguredCalls"));
        Assert.True(ReadBool(resultValue, "HooksRejectedAsNotStarted"));
    }

    private static bool ReadBool(object source, string propertyName) =>
        (bool)source.GetType().GetProperty(propertyName)!.GetValue(source)!;

    private static int ReadInt(object source, string propertyName) =>
        (int)source.GetType().GetProperty(propertyName)!.GetValue(source)!;
}
