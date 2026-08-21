using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsInstallationSurpriseTests
{
    [Fact]
    public async Task Generated_live_settings_handle_rejects_never_installed_kernel_before_invoking_callback()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run("""
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

                public sealed class RecordingControl : IGamePluginControlService
                {
                    public int UpdateCount { get; private set; }

                    public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                        => throw new InvalidOperationException("not used");

                    public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                        => throw new InvalidOperationException("not used");

                    public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                        => throw new InvalidOperationException("not used");

                    public ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default)
                    {
                        UpdateCount++;
                        return ValueTask.CompletedTask;
                    }

                    public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                        => throw new InvalidOperationException("not used");

                    public ValueTask<byte[]> InvokeServerExtensionAsync(
                        string pluginId,
                        byte[] arguments,
                        CancellationToken cancellationToken = default)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace Regression.Plugin
            {
                using Regression.Game;
                using Regression.Game.Ipc;

                public sealed record DamageEvent(string TargetId);

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;

                [Plugin("guardian")]
                public sealed partial class GuardianKernel : IEventKernel<DamageEvent>
                {
                    [LiveSetting]
                    public int AggroRange { get; set; } = 5;

                    public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                    public void Handle(DamageEvent e, HookContext ctx)
                        => ctx.Messages.Send(e.TargetId, "ok");
                }

                public static class LiveSettingsProbe
                {
                    public static async Task<object?[]> RunAsync()
                    {
                        var control = new RecordingControl();
                        var server = new RemotePluginServer(control, world: null);
                        var callbackCount = 0;
                        Exception? caught = null;
                        try
                        {
                            await server.Get<GuardianKernel>().SetValuesAsync(kernel =>
                            {
                                callbackCount++;
                                kernel.AggroRange = 6;
                            });
                        }
                        catch (Exception exception)
                        {
                            caught = exception;
                        }

                        return new object?[] { caught?.GetType().FullName, callbackCount, control.UpdateCount };
                    }
                }
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        var assembly = Load(outputCompilation);
        var probe = assembly.GetType("Regression.Plugin.LiveSettingsProbe", throwOnError: true)!;
        var runAsync = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var result = await InvokeAsync(runAsync);

        Assert.Equal(typeof(InvalidOperationException).FullName, result[0]);
        Assert.Equal(0, result[1]);
        Assert.Equal(0, result[2]);
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

    private static async Task<object?[]> InvokeAsync(MethodInfo method)
    {
        var result = method.Invoke(null, null);
        return await Assert.IsType<Task<object?[]>>(result);
    }
}
