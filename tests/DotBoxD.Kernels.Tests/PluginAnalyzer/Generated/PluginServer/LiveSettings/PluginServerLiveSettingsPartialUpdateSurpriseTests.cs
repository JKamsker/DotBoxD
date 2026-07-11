using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsPartialUpdateSurpriseTests
{
    [Fact]
    public async Task Generated_SetValuesAsync_sends_only_live_settings_written_by_callback()
    {
        var secondBatch = await RunProbeAsync("RunAsync");

        Assert.Equal(["AggroRange=6"], secondBatch);
    }

    [Fact]
    public async Task Generated_live_settings_handle_reuse_sends_only_current_batch()
    {
        var secondBatch = await RunProbeAsync("RunReusedHandleAsync");

        Assert.Equal(["AggroRange=7"], secondBatch);
    }

    private static async Task<string[]> RunProbeAsync(string methodName)
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var probe = assembly.GetType("Sample.Plugin.LiveSettingsPartialUpdateProbe", throwOnError: true)!;
        var runAsync = probe.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;

        var task = Assert.IsAssignableFrom<Task<string[]>>(runAsync.Invoke(null, null));
        return await task;
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
        using System.Linq;
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

                [LiveSetting]
                public int AggroRange { get; set; } = 5;

                [LiveSetting]
                public string DamageType { get; set; } = "slash";

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, DamageType);
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

            public static class LiveSettingsPartialUpdateProbe
            {
                public static async Task<string[]> RunAsync()
                {
                    var control = new RecordingControl();
                    var server = new RemotePluginServer(
                        control,
                        null,
                        setup => setup.Replace<IEventKernel<DamageEvent>, GuardianKernel>());

                    await server.StartAsync();
                    await server.Get<GuardianKernel>().Set(kernel => kernel.CalmStrength, 35).ApplyAsync();
                    await server.Get<GuardianKernel>().SetValuesAsync(kernel => kernel.AggroRange = 6);

                    return control.Batches[1]
                        .Select(static update => update.Name + "=" + update.Value)
                        .ToArray();
                }

                public static async Task<string[]> RunReusedHandleAsync()
                {
                    var control = new RecordingControl();
                    var server = new RemotePluginServer(
                        control,
                        null,
                        setup => setup.Replace<IEventKernel<DamageEvent>, GuardianKernel>());

                    await server.StartAsync();
                    var handle = server.Get<GuardianKernel>();
                    await handle.Set(kernel => kernel.CalmStrength, 35).ApplyAsync();
                    await handle.Set(kernel => kernel.AggroRange, 7).ApplyAsync();

                    return control.Batches[1]
                        .Select(static update => update.Name + "=" + update.Value)
                        .ToArray();
                }
            }
        }
        """;
}
