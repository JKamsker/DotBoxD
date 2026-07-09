using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsNullArgumentSurpriseTests
{
    [Theory]
    [InlineData("SetNullMember", "member")]
    [InlineData("SetNullValuesAsync", "set")]
    public async Task Generated_live_settings_handle_rejects_null_public_arguments(
        string methodName,
        string expectedParamName)
    {
        var assembly = Emit(Source);
        var control = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var serverType = assembly.GetType("Sample.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [control, null])!;
        var method = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;

        var exception = await CaptureGeneratedExceptionAsync(method, server);

        var argumentNull = Assert.IsType<ArgumentNullException>(exception);
        Assert.Equal(expectedParamName, argumentNull.ParamName);
    }

    private static Assembly Emit(string source)
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        using var stream = new MemoryStream();
        var emit = outputCompilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static async Task<Exception?> CaptureGeneratedExceptionAsync(MethodInfo method, object server)
    {
        try
        {
            var result = method.Invoke(null, [server]);
            if (result is not null)
            {
                await AwaitValueTask(result);
            }

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
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample
        {
            [RpcService]
            public interface IGameWorldAccess;

            public sealed record DamageEvent(string TargetId, int Amount);

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

            [GeneratePluginServer(
                Context = typeof(RemotePluginContext),
                ControlService = typeof(IGamePluginControlService))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            [Plugin("fire-damage")]
            public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int MinDamage { get; set; } = 1;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => e.Amount >= MinDamage;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "hit");
            }

            public static class Usage
            {
                public static void SetNullMember(RemotePluginServer server)
                    => server.Get<FireDamageKernel>().Set<int>(null!, 1);

                public static ValueTask SetNullValuesAsync(RemotePluginServer server)
                    => server.Get<FireDamageKernel>().SetValuesAsync(null!, atomic: false);
            }

            public sealed class RecordingControlService : IGamePluginControlService
            {
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
                    => ValueTask.CompletedTask;

                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                    => ValueTask.CompletedTask;

                public ValueTask<byte[]> InvokeServerExtensionAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(Array.Empty<byte>());
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sample.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new InvalidOperationException("not used");
            }
        }
        """;
}
