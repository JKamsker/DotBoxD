using System.Reflection;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginServerSurpriseRegressionTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Theory]
    [InlineData("RunNoCaptureAsync")]
    [InlineData("RunWithCapturesAsync")]
    public async Task Generated_plugin_server_InvokeAsync_rejects_null_wire_response_before_decoding(
        string methodName)
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource(extraPluginTypes: """

                public sealed class CaptureBag;

                public sealed record NullWireResponseProbeResult(
                    bool Threw,
                    int DecodeCalls,
                    int WireCalls,
                    bool ReturnedSentinel);

                public static class NullWireResponseProbe
                {
                    private const string PluginId = "anonymous.null-wire-response";

                    public static Task<NullWireResponseProbeResult> RunNoCaptureAsync(
                        RemotePluginServer server,
                        RecordingControlService control)
                        => RunAsync(
                            () => server.InvokeAsync(
                                static _ => new ValueTask<int>(0),
                                NoCaptureInvocation,
                                CancellationToken.None),
                            control);

                    public static Task<NullWireResponseProbeResult> RunWithCapturesAsync(
                        RemotePluginServer server,
                        RecordingControlService control)
                        => RunAsync(
                            () => server.InvokeAsync(
                                new CaptureBag(),
                                static (_, _) => new ValueTask<int>(0),
                                CaptureInvocation,
                                CancellationToken.None),
                            control);

                    private static IRInvocation<
                        global::System.Func<IGameWorldAccess, ValueTask<int>>,
                        int> NoCaptureInvocation => IRInvocation<
                            global::System.Func<IGameWorldAccess, ValueTask<int>>,
                            int>.FromGenerated(
                                PluginId,
                                CreatePackage,
                                static _ => [],
                                static (_, _) =>
                                {
                                    DecodeCalls++;
                                    return Sentinel;
                                });

                    private static IRInvocation<
                        CaptureBag,
                        RemoteServerInvocation<IGameWorldAccess, CaptureBag, int>,
                        int> CaptureInvocation => IRInvocation<
                            CaptureBag,
                            RemoteServerInvocation<IGameWorldAccess, CaptureBag, int>,
                            int>.FromGenerated(
                                PluginId,
                                CreatePackage,
                                static (_, _) => [],
                                static (_, _, _) =>
                                {
                                    DecodeCalls++;
                                    return Sentinel;
                                });

                    private static int DecodeCalls { get; set; }

                    private const int Sentinel = 2468;

                    private static async Task<NullWireResponseProbeResult> RunAsync(
                        global::System.Func<ValueTask<int>> invoke,
                        RecordingControlService control)
                    {
                        DecodeCalls = 0;
                        var threw = false;
                        var returnedSentinel = false;

                        try
                        {
                            returnedSentinel = await invoke().ConfigureAwait(false) == Sentinel;
                        }
                        catch (global::System.Exception)
                        {
                            threw = true;
                        }

                        return new NullWireResponseProbeResult(
                            threw,
                            DecodeCalls,
                            control.WireCalls,
                            returnedSentinel);
                    }

                    private static object CreatePackage()
                    {
                        var span = new DotBoxD.Kernels.Model.SourceSpan(1, 1);
                        var manifest = new PluginManifest(
                            PluginId,
                            "NullWireResponseProbe",
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
                            [
                                new DotBoxD.Kernels.SandboxFunction(
                                    "Run",
                                    true,
                                    [],
                                    DotBoxD.Kernels.Sandbox.SandboxType.I32,
                                    [
                                        new DotBoxD.Kernels.ReturnStatement(
                                            new DotBoxD.Kernels.LiteralExpression(
                                                DotBoxD.Kernels.Sandbox.SandboxValue.FromInt32(0),
                                                span),
                                            span)
                                    ])
                            ],
                            new global::System.Collections.Generic.Dictionary<string, string>
                            {
                                ["pluginId"] = PluginId,
                                ["kernel"] = "NullWireResponseProbe"
                            });
                        return PluginPackage.Create(manifest, module, new KernelEntrypoints("Run", "Run"));
                    }
                }

                public sealed class RecordingControlService : Regression.Game.Ipc.IGamePluginControlService
                {
                    private int _wireCalls;

                    public int WireCalls => Volatile.Read(ref _wireCalls);

                    public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                        => InstallAsync(packageJson, ct);

                    public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                        => InstallAsync(packageJson, ct);

                    public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                        => InstallAsync(packageJson, ct);

                    public ValueTask UpdateSettingsAsync(
                        string pluginId,
                        Regression.Game.Ipc.LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default)
                        => default;

                    public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => default;

                    public ValueTask<byte[]> InvokeServerExtensionAsync(
                        string pluginId,
                        byte[] arguments,
                        CancellationToken cancellationToken = default)
                    {
                        Interlocked.Increment(ref _wireCalls);
                        return ValueTask.FromResult<byte[]>(null!);
                    }

                    private static ValueTask<string> InstallAsync(string packageJson, CancellationToken ct)
                    {
                        ct.ThrowIfCancellationRequested();
                        return ValueTask.FromResult(
                            DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Import(packageJson).Manifest.PluginId);
                    }
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var control = Activator.CreateInstance(
            assembly.GetType("Regression.Plugin.RecordingControlService", throwOnError: true)!)!;
        var serverType = assembly.GetType("Regression.Plugin.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [control, null])!;
        var method = assembly.GetType("Regression.Plugin.NullWireResponseProbe", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;

        var task = Assert.IsAssignableFrom<Task>(method.Invoke(null, [server, control]));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;

        Assert.True(ReadBool(result, "Threw"));
        Assert.Equal(0, ReadInt(result, "DecodeCalls"));
        Assert.Equal(1, ReadInt(result, "WireCalls"));
        Assert.False(ReadBool(result, "ReturnedSentinel"));
    }
}
