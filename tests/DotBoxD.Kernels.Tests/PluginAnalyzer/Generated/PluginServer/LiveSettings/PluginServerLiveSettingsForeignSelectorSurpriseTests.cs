using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsForeignSelectorSurpriseTests
{
    [Fact]
    public async Task Generated_live_settings_Set_rejects_foreign_live_setting_selectors()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var probe = assembly.GetType("Sample.Plugin.LiveSettingsProbe", throwOnError: true)!;
        var runAsync = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var result = await Assert.IsAssignableFrom<Task<object>>(runAsync.Invoke(null, null));
        var exceptionType = GetString(result, "ExceptionType");
        var exceptionMessage = GetString(result, "ExceptionMessage") ?? string.Empty;

        Assert.True(
            exceptionType is "System.ArgumentException" or "System.InvalidOperationException",
            $"Expected selector rejection, but captured '{exceptionType ?? "<null>"}' with update " +
            $"'{GetString(result, "UpdateSummary") ?? "<none>"}'.");
        Assert.True(
            exceptionMessage.Contains("member", StringComparison.OrdinalIgnoreCase) ||
            exceptionMessage.Contains("selector", StringComparison.OrdinalIgnoreCase) ||
            exceptionMessage.Contains("FireDamageKernel", StringComparison.Ordinal),
            $"Expected the rejection to name the member selector or target kernel, but got '{exceptionMessage}'.");
        Assert.Equal(0, GetInt(result, "UpdateCallCount"));
        Assert.Null(GetString(result, "UpdatedPluginId"));
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

            [Plugin("fire-damage")]
            public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int MinDamage { get; set; } = 1;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "hit");
            }

            public static class ForeignSettings
            {
                [LiveSetting]
                public static int MinDamage { get; set; }
            }

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            public sealed class RecordingControl : IGamePluginControlService
            {
                public int UpdateCallCount { get; private set; }

                public string? UpdatedPluginId { get; private set; }

                public string? UpdateSummary { get; private set; }

                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("fire-damage");

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("fire-damage");

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("fire-damage");

                public ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default)
                {
                    UpdateCallCount++;
                    UpdatedPluginId = pluginId;
                    UpdateSummary = string.Join(",", updates.Select(update => update.Name + "=" + update.Value));
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

            public sealed record ProbeResult(
                string? ExceptionType,
                string? ExceptionMessage,
                int UpdateCallCount,
                string? UpdatedPluginId,
                string? UpdateSummary);

            public static class LiveSettingsProbe
            {
                public static async Task<object> RunAsync()
                {
                    var control = new RecordingControl();
                    var server = new RemotePluginServer(
                        control,
                        null,
                        setup => setup.Replace<IEventKernel<DamageEvent>, FireDamageKernel>());

                    await server.StartAsync().AsTask().ConfigureAwait(false);

                    var exception = await CaptureAsync(() =>
                        server.Get<FireDamageKernel>()
                            .Set(_ => ForeignSettings.MinDamage, 99)
                            .ApplyAsync()
                            .AsTask()).ConfigureAwait(false);

                    return new ProbeResult(
                        exception?.GetType().FullName,
                        exception?.Message,
                        control.UpdateCallCount,
                        control.UpdatedPluginId,
                        control.UpdateSummary);
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
