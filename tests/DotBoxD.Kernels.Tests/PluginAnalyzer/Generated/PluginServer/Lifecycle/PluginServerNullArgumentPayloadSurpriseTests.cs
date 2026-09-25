using System.Reflection;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginServerSurpriseRegressionTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Theory]
    [InlineData("RunNoCaptureAsync")]
    [InlineData("RunWithCapturesAsync")]
    public async Task Generated_plugin_server_InvokeAsync_rejects_null_argument_payload_before_wire_invocation(
        string methodName)
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource(extraPluginTypes: """

                public sealed class RecordingWorld : IGameWorldAccess;

                public sealed class CaptureBag
                {
                    public int Value { get; init; } = 42;
                }

                public sealed record NullArgumentPayloadProbeResult(
                    bool Threw,
                    int WireCalls,
                    string? ExceptionType);

                public static class NullArgumentPayloadProbe
                {
                    private const string PluginId = "anonymous.null-arguments";

                    public static async Task<NullArgumentPayloadProbeResult> RunNoCaptureAsync(
                        RemotePluginServer server,
                        RecordingControlService control)
                    {
                        try
                        {
                            await server.InvokeAsync(
                                static _ => new ValueTask<int>(1),
                                IRInvocation<global::System.Func<IGameWorldAccess, ValueTask<int>>, int>.FromGenerated(
                                    PluginId,
                                    CreatePackage,
                                    static _ => null!,
                                    static (_, _) => 42),
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (global::System.Exception ex)
                        {
                            return new NullArgumentPayloadProbeResult(true, control.WireCalls, ex.GetType().Name);
                        }

                        return new NullArgumentPayloadProbeResult(false, control.WireCalls, null);
                    }

                    public static async Task<NullArgumentPayloadProbeResult> RunWithCapturesAsync(
                        RemotePluginServer server,
                        RecordingControlService control)
                    {
                        try
                        {
                            await server.InvokeAsync(
                                new CaptureBag(),
                                static (_, captures) => new ValueTask<int>(captures.Value),
                                IRInvocation<
                                    CaptureBag,
                                    RemoteServerInvocation<IGameWorldAccess, CaptureBag, int>,
                                    int>.FromGenerated(
                                        PluginId,
                                        CreatePackage,
                                        static (_, _) => null!,
                                        static (_, _, _) => 42),
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (global::System.Exception ex)
                        {
                            return new NullArgumentPayloadProbeResult(true, control.WireCalls, ex.GetType().Name);
                        }

                        return new NullArgumentPayloadProbeResult(false, control.WireCalls, null);
                    }

                    private static object CreatePackage()
                    {
                        var span = new DotBoxD.Kernels.Model.SourceSpan(1, 1);
                        var entrypoint = new DotBoxD.Kernels.SandboxFunction(
                            "Run",
                            IsEntrypoint: true,
                            [],
                            DotBoxD.Kernels.Sandbox.SandboxType.I32,
                            [
                                new DotBoxD.Kernels.ReturnStatement(
                                    new DotBoxD.Kernels.LiteralExpression(
                                        DotBoxD.Kernels.Sandbox.SandboxValue.FromInt32(42),
                                        span),
                                    span)
                            ]);
                        var manifest = new DotBoxD.Plugins.PluginManifest(
                            PluginId,
                            "AnonymousNullArgumentPayloadProbe",
                            DotBoxD.Kernels.ExecutionMode.Auto,
                            ["Cpu"],
                            [],
                            [])
                        {
                            RpcEntrypoint = "Run"
                        };
                        var module = new DotBoxD.Kernels.SandboxModule(
                            PluginId,
                            DotBoxD.Kernels.Model.SemVersion.One,
                            DotBoxD.Kernels.Model.SemVersion.One,
                            [],
                            [entrypoint],
                            new global::System.Collections.Generic.Dictionary<string, string>
                            {
                                ["pluginId"] = PluginId,
                                ["kernel"] = "AnonymousNullArgumentPayloadProbe"
                            });
                        return DotBoxD.Plugins.PluginPackage.Create(
                            manifest,
                            module,
                            new DotBoxD.Plugins.KernelEntrypoints("Run", "Run"));
                    }
                }

                public sealed class RecordingControlService : Regression.Game.Ipc.IGamePluginControlService
                {
                    private int _wireCalls;

                    public int WireCalls => Volatile.Read(ref _wireCalls);

                    public ValueTask<string> InstallPluginAsync(
                        string packageJson,
                        CancellationToken ct = default)
                        => InstallPackageAsync(packageJson, ct);

                    public ValueTask<string> InstallSubscriptionAsync(
                        string packageJson,
                        CancellationToken ct = default)
                        => InstallPackageAsync(packageJson, ct);

                    public ValueTask<string> InstallServerExtensionAsync(
                        string packageJson,
                        CancellationToken ct = default)
                        => InstallPackageAsync(packageJson, ct);

                    public ValueTask UpdateSettingsAsync(
                        string pluginId,
                        Regression.Game.Ipc.LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default)
                        => default;

                    public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                        => default;

                    public ValueTask<byte[]> InvokeServerExtensionAsync(
                        string pluginId,
                        byte[] arguments,
                        CancellationToken cancellationToken = default)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Interlocked.Increment(ref _wireCalls);
                        return ValueTask.FromResult(
                            DotBoxD.Plugins.KernelRpcBinaryCodec.EncodeValue(
                                DotBoxD.Plugins.KernelRpcValue.Int32(42)));
                    }

                    private static ValueTask<string> InstallPackageAsync(
                        string packageJson,
                        CancellationToken ct)
                    {
                        ct.ThrowIfCancellationRequested();
                        var package = DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Import(packageJson);
                        return ValueTask.FromResult(package.Manifest.PluginId);
                    }
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var control = Activator.CreateInstance(
            assembly.GetType("Regression.Plugin.RecordingControlService", throwOnError: true)!)!;
        var world = Activator.CreateInstance(
            assembly.GetType("Regression.Plugin.RecordingWorld", throwOnError: true)!)!;
        var serverType = assembly.GetType("Regression.Plugin.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [control, world])!;
        var method = assembly.GetType("Regression.Plugin.NullArgumentPayloadProbe", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;

        var task = Assert.IsAssignableFrom<Task>(method.Invoke(null, [server, control]));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var threw = (bool)result.GetType().GetProperty("Threw")!.GetValue(result)!;
        var wireCalls = (int)result.GetType().GetProperty("WireCalls")!.GetValue(result)!;
        var exceptionType = result.GetType().GetProperty("ExceptionType")!.GetValue(result);

        Assert.True(
            threw,
            $"Expected a local failure for a null argument payload; WireCalls={wireCalls}, " +
            $"ExceptionType={exceptionType ?? "<none>"}.");
        Assert.Equal(0, wireCalls);
    }
}
