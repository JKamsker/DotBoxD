using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerAnonymousKernelInstallMismatchTests
{
    [Fact]
    public async Task EnsureAnonymousKernelAsync_rejects_manifest_id_mismatch_before_installing()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var probe = assembly.GetType("Sample.Plugin.AnonymousMismatchProbe", throwOnError: true)!;
        var runAsync = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var result = await Assert.IsAssignableFrom<Task<object>>(runAsync.Invoke(null, null));

        Assert.Equal(typeof(InvalidOperationException).FullName, GetString(result, "FirstExceptionType"));
        Assert.Contains("requested", GetString(result, "FirstExceptionMessage"), StringComparison.Ordinal);
        Assert.Contains("actual", GetString(result, "FirstExceptionMessage"), StringComparison.Ordinal);
        Assert.Equal(typeof(InvalidOperationException).FullName, GetString(result, "SecondExceptionType"));
        Assert.Contains("requested", GetString(result, "SecondExceptionMessage"), StringComparison.Ordinal);
        Assert.Contains("actual", GetString(result, "SecondExceptionMessage"), StringComparison.Ordinal);
        Assert.Equal(0, GetInt(result, "InstallCalls"));
    }

    private static Assembly Load(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static string? GetString(object result, string propertyName)
        => (string?)result.GetType().GetProperty(propertyName)!.GetValue(result);

    private static int GetInt(object result, string propertyName)
        => (int)result.GetType().GetProperty(propertyName)!.GetValue(result)!;

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample.Game
        {
            [RpcService]
            public interface IGameWorldAccess;
        }

        namespace Sample.Game.Ipc
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
                public static Sample.Game.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new InvalidOperationException("not used");
            }
        }

        namespace Sample.Plugin
        {
            using Sample.Game;
            using Sample.Game.Ipc;

            public sealed record TickEvent(int Value);

            [Plugin("actual")]
            public sealed partial class ActualKernel : IEventKernel<TickEvent>
            {
                public bool ShouldHandle(TickEvent e, HookContext ctx) => true;

                public void Handle(TickEvent e, HookContext ctx)
                    => ctx.Messages.Send("target", "ok");
            }

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            public sealed class RecordingControl : IGamePluginControlService
            {
                public int InstallCalls { get; private set; }

                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => throw new InvalidOperationException("not used");

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => throw new InvalidOperationException("not used");

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                {
                    InstallCalls++;
                    return ValueTask.FromResult("actual");
                }

                public ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default)
                    => throw new InvalidOperationException("not used");

                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                    => ValueTask.CompletedTask;

                public ValueTask<byte[]> InvokeServerExtensionAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(Array.Empty<byte>());
            }

            public sealed record ProbeResult(
                string? FirstExceptionType,
                string? FirstExceptionMessage,
                string? SecondExceptionType,
                string? SecondExceptionMessage,
                int InstallCalls);

            public static class AnonymousMismatchProbe
            {
                public static async Task<object> RunAsync()
                {
                    var control = new RecordingControl();
                    var server = new RemotePluginServer(control, null);

                    var first = await CaptureAsync(() =>
                        server.EnsureAnonymousKernelAsync("requested", ActualPluginPackage.Create));
                    var second = await CaptureAsync(() =>
                        server.EnsureAnonymousKernelAsync("requested", ActualPluginPackage.Create));

                    return new ProbeResult(
                        first?.GetType().FullName,
                        first?.Message,
                        second?.GetType().FullName,
                        second?.Message,
                        control.InstallCalls);
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
            }
        }
        """;
}
