using System.Reflection;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginServerSurpriseRegressionTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Fact]
    public async Task Generated_plugin_server_capture_InvokeAsync_observes_cancellation_before_decoding_response()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource(extraPluginTypes: """

                public sealed record InvokeCaptureCancellationProbeResult(
                    bool ThrewOperationCanceled,
                    int DecodeCalls,
                    int WireCalls,
                    bool ReturnedDecodedValue);

                public sealed class InvokeCaptureBag;

                public static class InvokeCaptureCancellationProbe
                {
                    public static async Task<InvokeCaptureCancellationProbeResult> RunAsync()
                    {
                        using var cts = new CancellationTokenSource();
                        var control = new RecordingCaptureControlService(cts);
                        var server = new RemotePluginServer(control, null);
                        var captures = new InvokeCaptureBag();
                        var decodeCalls = 0;
                        var returnedDecodedValue = false;
                        var threwOperationCanceled = false;
                        var invocation = IRInvocation<
                            InvokeCaptureBag,
                            RemoteServerInvocation<IGameWorldAccess, InvokeCaptureBag, int>,
                            int>.FromGenerated(
                                "anon-capture-probe",
                                CreatePackage,
                                (_, _) => [],
                                (_, _, _) =>
                                {
                                    decodeCalls++;
                                    return 123;
                                });

                        try
                        {
                            var value = await server.InvokeAsync(
                                captures,
                                static (_, _) => new ValueTask<int>(0),
                                invocation,
                                cts.Token).ConfigureAwait(false);
                            returnedDecodedValue = value == 123;
                        }
                        catch (global::System.OperationCanceledException)
                        {
                            threwOperationCanceled = true;
                        }

                        return new InvokeCaptureCancellationProbeResult(
                            threwOperationCanceled,
                            decodeCalls,
                            control.WireCalls,
                            returnedDecodedValue);
                    }

                    private static PluginPackage CreatePackage()
                        => PluginPackage.Create(
                            new PluginManifest(
                                "anon-capture-probe",
                                "AnonymousCaptureInvokeAsync",
                                DotBoxD.Kernels.ExecutionMode.Interpreted,
                                ["Cpu"],
                                [],
                                [])
                            {
                                RpcEntrypoint = "Handle"
                            },
                            new DotBoxD.Kernels.SandboxModule(
                                "anon-capture-probe",
                                new DotBoxD.Kernels.Model.SemVersion(1, 0, 0),
                                new DotBoxD.Kernels.Model.SemVersion(1, 0, 0),
                                [],
                                [
                                    new DotBoxD.Kernels.SandboxFunction(
                                        "Handle",
                                        true,
                                        [],
                                        DotBoxD.Kernels.Sandbox.SandboxType.I32,
                                        [
                                            new DotBoxD.Kernels.ReturnStatement(
                                                new DotBoxD.Kernels.LiteralExpression(
                                                    DotBoxD.Kernels.Sandbox.SandboxValue.FromInt32(123),
                                                    new DotBoxD.Kernels.Model.SourceSpan(0, 0)),
                                                new DotBoxD.Kernels.Model.SourceSpan(0, 0))
                                        ],
                                        DotBoxD.Kernels.Sandbox.SandboxEffect.Cpu)
                                ],
                                new global::System.Collections.Generic.Dictionary<string, string>
                                {
                                    ["pluginId"] = "anon-capture-probe",
                                    ["kernel"] = "AnonymousCaptureInvokeAsync"
                                }),
                            new KernelEntrypoints("Handle", "Handle"));
                }

                public sealed class RecordingCaptureControlService : Regression.Game.Ipc.IGamePluginControlService
                {
                    private readonly CancellationTokenSource _cts;
                    private int _wireCalls;

                    public RecordingCaptureControlService(CancellationTokenSource cts)
                        => _cts = cts;

                    public int WireCalls => Volatile.Read(ref _wireCalls);

                    public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default) =>
                        throw new global::System.NotSupportedException();

                    public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default) =>
                        throw new global::System.NotSupportedException();

                    public ValueTask<string> InstallServerExtensionAsync(
                        string packageJson,
                        CancellationToken ct = default) =>
                        ValueTask.FromResult("anon-capture-probe");

                    public ValueTask UpdateSettingsAsync(
                        string pluginId,
                        Regression.Game.Ipc.LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default) =>
                        throw new global::System.NotSupportedException();

                    public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) =>
                        throw new global::System.NotSupportedException();

                    public ValueTask<byte[]> InvokeServerExtensionAsync(
                        string pluginId,
                        byte[] arguments,
                        CancellationToken cancellationToken = default)
                    {
                        Interlocked.Increment(ref _wireCalls);
                        cancellationToken.ThrowIfCancellationRequested();
                        _cts.Cancel();
                        return ValueTask.FromResult(
                            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(123)));
                    }
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        var assembly = Emit(outputCompilation);
        var probe = assembly.GetType("Regression.Plugin.InvokeCaptureCancellationProbe", throwOnError: true)!;
        var method = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var resultTask = Assert.IsAssignableFrom<Task>(method.Invoke(null, null));
        await resultTask;
        var resultValue = resultTask.GetType().GetProperty("Result")!.GetValue(resultTask)!;

        Assert.True(ReadBool(resultValue, "ThrewOperationCanceled"));
        Assert.Equal(0, ReadInt(resultValue, "DecodeCalls"));
        Assert.Equal(1, ReadInt(resultValue, "WireCalls"));
        Assert.False(ReadBool(resultValue, "ReturnedDecodedValue"));
    }
}
