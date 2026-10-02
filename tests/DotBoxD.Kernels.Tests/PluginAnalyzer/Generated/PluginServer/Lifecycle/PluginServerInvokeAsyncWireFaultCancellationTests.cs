using System.Reflection;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginServerSurpriseRegressionTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Theory]
    [InlineData("RunNoCaptureAsync", true)]
    [InlineData("RunWithCapturesAsync", true)]
    [InlineData("RunNoCaptureAsync", false)]
    [InlineData("RunWithCapturesAsync", false)]
    public async Task Generated_plugin_server_InvokeAsync_prioritizes_caller_cancellation_over_wire_faults(
        string methodName,
        bool cancelCaller)
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource(extraPluginTypes: """

                public sealed class RecordingWorld : IGameWorldAccess;

                public sealed class CaptureBag
                {
                    public int Value { get; init; } = 42;
                }

                public sealed record WireFaultProbeResult(
                    bool Canceled,
                    bool CallerTokenPreserved,
                    int DecodeCalls,
                    int WireCalls,
                    string? UnexpectedException);

                public static class WireFaultProbe
                {
                    private const string PluginId = "anonymous.wire-fault";

                    public static async Task<WireFaultProbeResult> RunNoCaptureAsync(
                        RemotePluginServer server,
                        RecordingControlService control,
                        bool cancelCaller)
                    {
                        using var cts = new CancellationTokenSource();
                        control.Configure(cts, cancelCaller);
                        var decodeCalls = 0;
                        var invocation = IRInvocation<
                            global::System.Func<IGameWorldAccess, ValueTask<int>>,
                            int>.FromGenerated(
                                PluginId,
                                CreatePackage,
                                static _ => [],
                                (_, _) =>
                                {
                                    Interlocked.Increment(ref decodeCalls);
                                    return 42;
                                });

                        try
                        {
                            await server.InvokeAsync(
                                static _ => new ValueTask<int>(42),
                                invocation,
                                cts.Token).ConfigureAwait(false);
                        }
                        catch (global::System.OperationCanceledException ex)
                        {
                            return new(true, ex.CancellationToken == cts.Token, decodeCalls, control.WireCalls, null);
                        }
                        catch (global::System.Exception ex)
                        {
                            return new(false, false, decodeCalls, control.WireCalls, ex.GetType().Name + ": " + ex.Message);
                        }

                        return new(false, false, decodeCalls, control.WireCalls, null);
                    }

                    public static async Task<WireFaultProbeResult> RunWithCapturesAsync(
                        RemotePluginServer server,
                        RecordingControlService control,
                        bool cancelCaller)
                    {
                        using var cts = new CancellationTokenSource();
                        control.Configure(cts, cancelCaller);
                        var decodeCalls = 0;
                        var invocation = IRInvocation<
                            CaptureBag,
                            RemoteServerInvocation<IGameWorldAccess, CaptureBag, int>,
                            int>.FromGenerated(
                                PluginId,
                                CreatePackage,
                                static (_, _) => [],
                                (_, _, _) =>
                                {
                                    Interlocked.Increment(ref decodeCalls);
                                    return 42;
                                });

                        try
                        {
                            await server.InvokeAsync(
                                new CaptureBag(),
                                static (_, captures) => new ValueTask<int>(captures.Value),
                                invocation,
                                cts.Token).ConfigureAwait(false);
                        }
                        catch (global::System.OperationCanceledException ex)
                        {
                            return new(true, ex.CancellationToken == cts.Token, decodeCalls, control.WireCalls, null);
                        }
                        catch (global::System.Exception ex)
                        {
                            return new(false, false, decodeCalls, control.WireCalls, ex.GetType().Name + ": " + ex.Message);
                        }

                        return new(false, false, decodeCalls, control.WireCalls, null);
                    }

                    private static PluginPackage CreatePackage()
                    {
                        var span = new DotBoxD.Kernels.Model.SourceSpan(0, 0);
                        return PluginPackage.Create(
                            new PluginManifest(PluginId, "WireFaultProbe", DotBoxD.Kernels.ExecutionMode.Interpreted, ["Cpu"], [], [])
                            {
                                RpcEntrypoint = "Run"
                            },
                            new DotBoxD.Kernels.SandboxModule(
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
                                        [new DotBoxD.Kernels.ReturnStatement(new DotBoxD.Kernels.LiteralExpression(DotBoxD.Kernels.Sandbox.SandboxValue.FromInt32(42), span), span)],
                                        DotBoxD.Kernels.Sandbox.SandboxEffect.Cpu)
                                ],
                                new global::System.Collections.Generic.Dictionary<string, string>
                                {
                                    ["pluginId"] = PluginId,
                                    ["kernel"] = "WireFaultProbe"
                                }),
                            new KernelEntrypoints("Run", "Run"));
                    }
                }

                public sealed class RecordingControlService : Regression.Game.Ipc.IGamePluginControlService
                {
                    private CancellationTokenSource? _callerCancellation;
                    private bool _cancelCaller;
                    private int _wireCalls;

                    public int WireCalls => Volatile.Read(ref _wireCalls);

                    public void Configure(CancellationTokenSource callerCancellation, bool cancelCaller)
                    {
                        _callerCancellation = callerCancellation;
                        _cancelCaller = cancelCaller;
                    }

                    public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default) => throw new global::System.NotSupportedException();

                    public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default) => throw new global::System.NotSupportedException();

                    public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default) => ValueTask.FromResult("anonymous.wire-fault");

                    public ValueTask UpdateSettingsAsync(string pluginId, Regression.Game.Ipc.LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default) => default;

                    public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => default;

                    public ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, CancellationToken cancellationToken = default)
                    {
                        Interlocked.Increment(ref _wireCalls);
                        if (_cancelCaller)
                        {
                            _callerCancellation!.Cancel();
                        }

                        return ValueTask.FromException<byte[]>(new global::System.InvalidOperationException("wire failed"));
                    }
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var control = Activator.CreateInstance(
            assembly.GetType("Regression.Plugin.RecordingControlService", throwOnError: true)!)!;
        var world = Activator.CreateInstance(
            assembly.GetType("Regression.Plugin.RecordingWorld", throwOnError: true)!)!;
        var server = Activator.CreateInstance(
            assembly.GetType("Regression.Plugin.RemotePluginServer", throwOnError: true)!,
            [control, world])!;
        var method = assembly.GetType("Regression.Plugin.WireFaultProbe", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;

        var task = Assert.IsAssignableFrom<Task>(method.Invoke(null, [server, control, cancelCaller]));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;

        Assert.Equal(1, ReadInt(result, "WireCalls"));
        Assert.Equal(0, ReadInt(result, "DecodeCalls"));
        if (cancelCaller)
        {
            Assert.True(ReadBool(result, "Canceled"));
            Assert.True(ReadBool(result, "CallerTokenPreserved"));
            Assert.Null(result.GetType().GetProperty("UnexpectedException")!.GetValue(result));
        }
        else
        {
            Assert.False(ReadBool(result, "Canceled"));
            Assert.Equal("InvalidOperationException: wire failed", result.GetType().GetProperty("UnexpectedException")!.GetValue(result));
        }
    }
}
