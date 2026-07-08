using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsNullValueSurpriseTests
{
    [Fact]
    public async Task Generated_live_settings_Set_rejects_null_required_live_setting_value()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var probe = assembly.GetType("Sample.Plugin.LiveSettingsProbe", throwOnError: true)!;
        var createStartedAsync = probe.GetMethod("CreateStartedAsync", BindingFlags.Public | BindingFlags.Static)!;
        var state = await Assert.IsAssignableFrom<Task<object>>(createStartedAsync.Invoke(null, null));
        var server = state.GetType().GetProperty("Server")!.GetValue(state)!;
        var control = state.GetType().GetProperty("Control")!.GetValue(state)!;
        var serverType = assembly.GetType("Sample.Plugin.RemotePluginServer", throwOnError: true)!;
        var kernelType = assembly.GetType("Sample.Plugin.FireDamageKernel", throwOnError: true)!;

        var handle = serverType.GetMethod("Get")!
            .MakeGenericMethod(kernelType)
            .Invoke(server, null)!;
        var selector = CreateDamageTypeSelector(kernelType);
        var set = handle.GetType().GetMethods()
            .Single(method => method is { Name: "Set", IsGenericMethodDefinition: true })
            .MakeGenericMethod(typeof(string));
        var applyAsync = handle.GetType().GetMethod("ApplyAsync", [typeof(bool)])!;

        var exception = await CaptureExceptionAsync(async () =>
        {
            set.Invoke(handle, [selector, null]);
            await AwaitValueTask(applyAsync.Invoke(handle, [false])!);
        });
        var exceptionType = exception?.GetType().FullName;
        var updateCallCount = GetInt(control, "UpdateCallCount");
        var lastUpdate = Get(control, "LastUpdate");

        Assert.True(
            exceptionType == typeof(ArgumentException).FullName ||
            exceptionType == typeof(InvalidOperationException).FullName,
            "Expected ArgumentException or InvalidOperationException, " +
            $"got '{exceptionType ?? "<null>"}'; updateCallCount={updateCallCount}; " +
            $"lastUpdate='{lastUpdate ?? "<null>"}'.");
        Assert.NotNull(exception);
        Assert.Contains("DamageType", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, updateCallCount);
        Assert.Null(lastUpdate);
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

    private static string? Get(object result, string propertyName)
        => (string?)result.GetType().GetProperty(propertyName)!.GetValue(result);

    private static int GetInt(object result, string propertyName)
        => (int)result.GetType().GetProperty(propertyName)!.GetValue(result)!;

    private static object CreateDamageTypeSelector(Type kernelType)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(kernelType, "kernel");
        var property = System.Linq.Expressions.Expression.Property(parameter, "DamageType");
        var delegateType = typeof(Func<,>).MakeGenericType(kernelType, typeof(string));
        return System.Linq.Expressions.Expression.Lambda(delegateType, property, parameter);
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return ex.InnerException;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task AwaitValueTask(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        await (Task)asTask.Invoke(valueTask, null)!;
    }

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
                public required string DamageType { get; set; }

                [LiveSetting]
                public int MinDamage { get; set; } = 1;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, DamageType);
            }

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            public sealed class RecordingControl : IGamePluginControlService
            {
                public int UpdateCallCount { get; private set; }
                public string? LastUpdate { get; private set; }

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
                    LastUpdate = updates.Length == 0
                        ? null
                        : string.Join(",", updates.Select(update => update.Name + "=" + update.Value));
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

            public sealed record ProbeState(RemotePluginServer Server, RecordingControl Control);

            public static class LiveSettingsProbe
            {
                public static async Task<object> CreateStartedAsync()
                {
                    var control = new RecordingControl();
                    var server = new RemotePluginServer(
                        control,
                        null,
                        setup => setup.Replace<IEventKernel<DamageEvent>, FireDamageKernel>());

                    await server.StartAsync();
                    return new ProbeState(server, control);
                }
            }
        }
        """;
}
