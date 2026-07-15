using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Fact]
    public async Task Generated_plugin_server_callbacks_that_dispose_facade_do_not_publish_state()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var probe = assembly.GetType("Regression.Plugin.ReentrantDisposalProbe", throwOnError: true)!;
        var runAsync = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var resultTask = Assert.IsAssignableFrom<Task>(runAsync.Invoke(null, null));
        await resultTask;
        var result = resultTask.GetType().GetProperty("Result")!.GetValue(resultTask)!;

        Assert.Equal(typeof(ObjectDisposedException).FullName, ReadString(result, "StartExceptionType"));
        Assert.True(ReadBool(result, "StartSessionDisposed"));
        Assert.Equal(0, ReadInt(result, "StartConfiguredCalls"));
        Assert.False(ReadBool(result, "StartPublishedFacade"));

        Assert.Equal(typeof(ObjectDisposedException).FullName, ReadString(result, "InstallExceptionType"));
        Assert.Equal(0, ReadInt(result, "InstallConfiguredCalls"));
        Assert.False(ReadBool(result, "InstallMarkedPackage"));
    }

    private static string? ReadString(object source, string propertyName) =>
        (string?)source.GetType().GetProperty(propertyName)!.GetValue(source);

    private const string Source = """
        using System;
        using System.Buffers;
        using System.Linq;
        using System.Reflection;
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
                ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Regression.Game.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
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

            public sealed record DamageEvent(string TargetId);

            [Plugin("guardian")]
            public sealed partial class GuardianKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;
                public void Handle(DamageEvent e, HookContext ctx) => ctx.Messages.Send(e.TargetId, "ok");
            }

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess
            {
                private static int configuredCalls;
                public static int ConfiguredCalls => Volatile.Read(ref configuredCalls);
                public static void ResetConfiguredCalls() => Volatile.Write(ref configuredCalls, 0);
                partial void OnConfigured() => Interlocked.Increment(ref configuredCalls);
            }

            public sealed partial class RemotePluginContext;

            public sealed class RecordingWorld : IGameWorldAccess;

            public sealed record ReentrantDisposalResult(string? StartExceptionType, bool StartSessionDisposed, int StartConfiguredCalls, bool StartPublishedFacade, string? InstallExceptionType, int InstallConfiguredCalls, bool InstallMarkedPackage);

            public static class ReentrantDisposalProbe
            {
                public static async Task<ReentrantDisposalResult> RunAsync()
                {
                    RegisterControlService();

                    var start = await RunStartDisposalAsync().ConfigureAwait(false);
                    var install = await RunInstallDisposalAsync().ConfigureAwait(false);

                    return new ReentrantDisposalResult(
                        start.ExceptionType,
                        start.SessionDisposed,
                        start.ConfiguredCalls,
                        start.PublishedFacade,
                        install.ExceptionType,
                        install.ConfiguredCalls,
                        install.MarkedPackage);
                }

                private static async Task<(string? ExceptionType, bool SessionDisposed, int ConfiguredCalls, bool PublishedFacade)> RunStartDisposalAsync()
                {
                    var transport = new TrackingTransport();
                    RemotePluginServer? server = null;
                    RemotePluginServer.ResetConfiguredCalls();
                    server = new RemotePluginServer((_, _) => new ValueTask<RpcPeerSession>(
                        ConnectAndDisposeAsync(transport, server!)));

                    var exception = await CaptureAsync(() => server.StartAsync().AsTask()).ConfigureAwait(false);
                    var publishedFacade = !Throws<ObjectDisposedException>(() => _ = server.Hooks);

                    return (exception?.GetType().FullName, transport.DisposeCalls > 0, RemotePluginServer.ConfiguredCalls, publishedFacade);
                }

                private static async Task<(string? ExceptionType, int ConfiguredCalls, bool MarkedPackage)> RunInstallDisposalAsync()
                {
                    var control = new RecordingControl();
                    RemotePluginServer.ResetConfiguredCalls();
                    var server = new RemotePluginServer(
                        control,
                        null,
                        setup => setup.Replace<IEventKernel<DamageEvent>, GuardianKernel>());
                    control.Server = server;

                    var exception = await CaptureAsync(() => server.StartAsync().AsTask()).ConfigureAwait(false);

                    return (exception?.GetType().FullName, RemotePluginServer.ConfiguredCalls, HasInstalledPackage(server, "guardian"));
                }

                private static async Task<RpcPeerSession> ConnectAndDisposeAsync(TrackingTransport transport, RemotePluginServer server)
                {
                    var session = await RpcPeerSession.ConnectAsync(
                        transport,
                        new MessagePackRpcSerializer(),
                        new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) },
                        CancellationToken.None).ConfigureAwait(false);
                    await server.DisposeAsync().ConfigureAwait(false);
                    return session;
                }

                private static bool HasInstalledPackage(RemotePluginServer server, string pluginId)
                {
                    var field = typeof(RemotePluginServer).GetField("_installedPluginIds", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    return ((System.Collections.Generic.IEnumerable<string>)field.GetValue(server)!).Contains(pluginId, StringComparer.Ordinal);
                }

                private static void RegisterControlService()
                {
                    GeneratedServiceRegistry.Register<IGamePluginControlService>(_ => new ControlServiceProxy(), _ => new NoopDispatcher());
                }

                private static async Task<Exception?> CaptureAsync(Func<Task> action)
                {
                    try
                    {
                        await action().ConfigureAwait(false);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                private static bool Throws<TException>(Action action)
                    where TException : Exception
                {
                    try
                    {
                        action();
                        return false;
                    }
                    catch (TException)
                    {
                        return true;
                    }
                }
            }

            public sealed class RecordingControl : IGamePluginControlService
            {
                public RemotePluginServer? Server { get; set; }

                public async ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                {
                    await Server!.DisposeAsync().ConfigureAwait(false);
                    return "guardian";
                }

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default) => throw new InvalidOperationException("not used");
                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default) => throw new InvalidOperationException("not used");
                public ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default) => ValueTask.CompletedTask;
                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
                public ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, CancellationToken cancellationToken = default) => ValueTask.FromResult(Array.Empty<byte>());
            }

            public sealed class ControlServiceProxy : IGamePluginControlService
            {
                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default) => throw new NotSupportedException();
                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default) => throw new NotSupportedException();
                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default) => throw new NotSupportedException();
                public ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default) => throw new NotSupportedException();
                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => throw new NotSupportedException();
                public ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            }

            public sealed class NoopDispatcher : IServiceDispatcher
            {
                public string ServiceName => "GamePluginControl";

                public Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer, IInstanceRegistry registry, IBufferWriter<byte> output, CancellationToken ct = default)
                    => throw new NotSupportedException();
            }

            public sealed class TrackingTransport : ITransport
            {
                private readonly TrackingChannel channel = new();
                private int connected;
                private int disposeCalls;

                public int DisposeCalls => Volatile.Read(ref disposeCalls);
                public IRpcChannel? Connection => Volatile.Read(ref connected) == 0 ? null : channel;
                public bool IsConnected => Volatile.Read(ref connected) != 0 && channel.IsConnected;

                public Task ConnectAsync(CancellationToken ct = default)
                {
                    ct.ThrowIfCancellationRequested();
                    Volatile.Write(ref connected, 1);
                    return Task.CompletedTask;
                }

                public async ValueTask DisposeAsync()
                {
                    Interlocked.Increment(ref disposeCalls);
                    Volatile.Write(ref connected, 0);
                    await channel.DisposeAsync().ConfigureAwait(false);
                }
            }

            public sealed class TrackingChannel : IRpcChannel
            {
                private readonly TaskCompletionSource disposed =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);
                private int disposedFlag;

                public bool IsConnected => Volatile.Read(ref disposedFlag) == 0;
                public string RemoteEndpoint => "test://generated-reentrant-dispose";

                public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }

                public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
                {
                    await disposed.Task.WaitAsync(ct).ConfigureAwait(false);
                    return Payload.Empty;
                }

                public ValueTask DisposeAsync()
                {
                    Volatile.Write(ref disposedFlag, 1);
                    disposed.TrySetResult();
                    return default;
                }
            }
        }
        """;
}
