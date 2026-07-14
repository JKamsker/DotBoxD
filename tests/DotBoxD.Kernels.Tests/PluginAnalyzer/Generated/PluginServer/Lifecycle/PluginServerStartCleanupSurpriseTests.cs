using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerStartCleanupSurpriseTests
{
    [Fact]
    public async Task StartAsync_preserves_initialization_error_when_session_cleanup_throws()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var probe = assembly.GetType("Regression.Plugin.StartCleanupProbe", throwOnError: true)!;
        var runAsync = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var result = await Assert.IsAssignableFrom<Task<object>>(runAsync.Invoke(null, null));

        Assert.Equal(1, GetInt(result, "DisposeCalls"));
        Assert.Equal(typeof(InvalidOperationException).FullName, GetString(result, "ExceptionType"));
        Assert.Contains(
            "No DotBoxD generated factory is registered",
            GetString(result, "ExceptionMessage"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("transport cleanup failed", GetString(result, "ExceptionMessage"), StringComparison.Ordinal);
    }

    private static Assembly Load(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static string GetString(object source, string propertyName)
        => (string)source.GetType().GetProperty(propertyName)!.GetValue(source)!;

    private static int GetInt(object source, string propertyName)
        => (int)source.GetType().GetProperty(propertyName)!.GetValue(source)!;

    private const string Source = """
        using System;
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
            using DotBoxD.Services.Peer;
            using DotBoxD.Services.Serialization;
            using DotBoxD.Services.Transport;
            using Regression.Game;

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            public sealed class RecordingWorld : IGameWorldAccess;

            public sealed record StartCleanupProbeResult(
                string? ExceptionType,
                string? ExceptionMessage,
                int DisposeCalls);

            public static class StartCleanupProbe
            {
                public static async Task<object> RunAsync()
                {
                    var transport = new ThrowingDisposeTransport();
                    var server = new RemotePluginServer((_, _) =>
                        new ValueTask<RpcPeerSession>(ConnectWithoutControlServiceAsync(transport)));

                    try
                    {
                        await server.StartAsync().ConfigureAwait(false);
                        return new StartCleanupProbeResult(null, null, transport.DisposeCalls);
                    }
                    catch (Exception exception)
                    {
                        return new StartCleanupProbeResult(
                            exception.GetType().FullName,
                            exception.Message,
                            transport.DisposeCalls);
                    }
                    finally
                    {
                        await server.DisposeAsync().ConfigureAwait(false);
                    }
                }

                private static Task<RpcPeerSession> ConnectWithoutControlServiceAsync(
                    ThrowingDisposeTransport transport)
                    => RpcPeerSession.ConnectAsync(
                        transport,
                        new MessagePackRpcSerializer(),
                        new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) },
                        CancellationToken.None);
            }

            public sealed class ThrowingDisposeTransport : ITransport
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
                    throw new InvalidOperationException("transport cleanup failed");
                }
            }

            public sealed class TrackingChannel : IRpcChannel
            {
                private readonly TaskCompletionSource _disposed =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);
                private int _disposedFlag;

                public bool IsConnected => Volatile.Read(ref _disposedFlag) == 0;

                public string RemoteEndpoint => "test://generated-start-cleanup";

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
        """;
}
