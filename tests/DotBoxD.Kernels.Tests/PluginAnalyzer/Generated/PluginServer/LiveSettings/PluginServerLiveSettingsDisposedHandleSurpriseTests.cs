using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsDisposedHandleSurpriseTests
{
    [Fact]
    public async Task Retained_live_settings_handle_rejects_SetValuesAsync_after_owner_disposal_before_callback()
    {
        var result = await RunProbeAsync();

        Assert.Equal(typeof(ObjectDisposedException).FullName, result[0]);
        Assert.False((bool)result[1]!);
        Assert.Equal(0, (int)result[2]!);
    }

    private static async Task<object?[]> RunProbeAsync()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var probe = assembly.GetType("Sample.Plugin.LiveSettingsDisposedHandleProbe", throwOnError: true)!;
        var runAsync = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var task = Assert.IsAssignableFrom<Task<object?[]>>(runAsync.Invoke(null, null));
        return await task.ConfigureAwait(false);
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

    private const string Source = """
        using System;
        using System.Collections.Generic;
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

            public sealed record DamageEvent(string TargetId);

            [Plugin("guardian")]
            public sealed partial class GuardianKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int CalmStrength { get; set; } = 10;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "hit");
            }

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            public sealed class RecordingControl : IGamePluginControlService
            {
                public List<LiveSettingUpdate[]> Batches { get; } = new();

                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("guardian");

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("guardian");

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("guardian");

                public ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default)
                {
                    Batches.Add(updates);
                    return ValueTask.CompletedTask;
                }

                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                    => ValueTask.CompletedTask;

                public ValueTask<byte[]> InvokeServerExtensionAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(Array.Empty<byte>());
            }

            public static class LiveSettingsDisposedHandleProbe
            {
                public static async Task<object?[]> RunAsync()
                {
                    var control = new RecordingControl();
                    var server = new RemotePluginServer(
                        control,
                        null,
                        setup => setup.Replace<IEventKernel<DamageEvent>, GuardianKernel>());

                    await server.StartAsync();
                    var handle = server.Get<GuardianKernel>();
                    await server.DisposeAsync();

                    var invoked = false;
                    Exception? caught = null;
                    try
                    {
                        await handle.SetValuesAsync(kernel =>
                        {
                            invoked = true;
                            kernel.CalmStrength = 11;
                        });
                    }
                    catch (Exception ex)
                    {
                        caught = ex;
                    }

                    return new object?[] { caught?.GetType().FullName, invoked, control.Batches.Count };
                }
            }
        }
        """;
}
