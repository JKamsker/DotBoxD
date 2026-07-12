using System.Reflection;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginServerSurpriseRegressionTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Theory]
    [InlineData("RunNoCaptureAsync")]
    [InlineData("RunWithCapturesAsync")]
    public async Task Generated_plugin_server_InvokeAsync_checks_cancellation_before_argument_encoding(
        string methodName)
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource(extraPluginTypes: """

                public sealed class RecordingWorld : IGameWorldAccess;

                public sealed class CaptureBag
                {
                    public int Value { get; init; } = 42;
                }

                public sealed record InvokeCancellationProbeResult(
                    bool Canceled,
                    int EncodeCallsAfterCancellation,
                    int WireCallsAfterCancellation,
                    string? UnexpectedException);

                public static class InvokeCancellationProbe
                {
                    private const string PluginId = "anonymous.cancel";

                    public static async Task<InvokeCancellationProbeResult> RunNoCaptureAsync(
                        RemotePluginServer server,
                        RecordingControlService control)
                    {
                        await server.InvokeAsync(
                            static _ => new ValueTask<int>(1),
                            NoCaptureInvocation(static () => []),
                            CancellationToken.None).ConfigureAwait(false);

                        var wireCallsBeforeCancellation = control.WireCalls;
                        var encodeCalls = 0;
                        using var cts = new global::System.Threading.CancellationTokenSource();
                        cts.Cancel();

                        var invocation = NoCaptureInvocation(() =>
                        {
                            Interlocked.Increment(ref encodeCalls);
                            throw new global::System.InvalidOperationException("encoded no-capture arguments");
                        });

                        try
                        {
                            await server.InvokeAsync(
                                static _ => new ValueTask<int>(1),
                                invocation,
                                cts.Token).ConfigureAwait(false);
                        }
                        catch (global::System.OperationCanceledException)
                        {
                            return Result(true, encodeCalls, control, wireCallsBeforeCancellation, null);
                        }
                        catch (global::System.Exception ex)
                        {
                            return Result(false, encodeCalls, control, wireCallsBeforeCancellation, ex);
                        }

                        return Result(false, encodeCalls, control, wireCallsBeforeCancellation, null);
                    }

                    public static async Task<InvokeCancellationProbeResult> RunWithCapturesAsync(
                        RemotePluginServer server,
                        RecordingControlService control)
                    {
                        await server.InvokeAsync(
                            new CaptureBag(),
                            InvokeWithCaptures,
                            CaptureInvocation(static () => []),
                            CancellationToken.None).ConfigureAwait(false);

                        var wireCallsBeforeCancellation = control.WireCalls;
                        var encodeCalls = 0;
                        using var cts = new global::System.Threading.CancellationTokenSource();
                        cts.Cancel();

                        var invocation = CaptureInvocation(() =>
                        {
                            Interlocked.Increment(ref encodeCalls);
                            throw new global::System.InvalidOperationException("encoded capture arguments");
                        });

                        try
                        {
                            await server.InvokeAsync(
                                new CaptureBag(),
                                InvokeWithCaptures,
                                invocation,
                                cts.Token).ConfigureAwait(false);
                        }
                        catch (global::System.OperationCanceledException)
                        {
                            return Result(true, encodeCalls, control, wireCallsBeforeCancellation, null);
                        }
                        catch (global::System.Exception ex)
                        {
                            return Result(false, encodeCalls, control, wireCallsBeforeCancellation, ex);
                        }

                        return Result(false, encodeCalls, control, wireCallsBeforeCancellation, null);
                    }

                    private static ValueTask<int> InvokeWithCaptures(
                        IGameWorldAccess world,
                        CaptureBag captures)
                        => new(captures.Value);

                    private static IRInvocation<global::System.Func<IGameWorldAccess, ValueTask<int>>, int> NoCaptureInvocation(
                        global::System.Func<byte[]> encode)
                        => IRInvocation<global::System.Func<IGameWorldAccess, ValueTask<int>>, int>.FromGenerated(
                            PluginId,
                            CreatePackage,
                            _ => encode(),
                            static (_, _) => 42);

                    private static IRInvocation<
                        CaptureBag,
                        RemoteServerInvocation<IGameWorldAccess, CaptureBag, int>,
                        int> CaptureInvocation(global::System.Func<byte[]> encode)
                        => IRInvocation<
                            CaptureBag,
                            RemoteServerInvocation<IGameWorldAccess, CaptureBag, int>,
                            int>.FromGenerated(
                                PluginId,
                                CreatePackage,
                                (_, _) => encode(),
                                static (_, _, _) => 42);

                    private static InvokeCancellationProbeResult Result(
                        bool canceled,
                        int encodeCalls,
                        RecordingControlService control,
                        int wireCallsBeforeCancellation,
                        global::System.Exception? exception)
                        => new(
                            canceled,
                            encodeCalls,
                            control.WireCalls - wireCallsBeforeCancellation,
                            exception is null
                                ? null
                                : exception.GetType().Name + ": " + exception.Message);

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
                            "AnonymousCancellationProbe",
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
                                ["kernel"] = "AnonymousCancellationProbe"
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
        var method = assembly.GetType("Regression.Plugin.InvokeCancellationProbe", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;

        var task = Assert.IsAssignableFrom<Task>(method.Invoke(null, [server, control]));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var encodeCalls = ReadInt(result, "EncodeCallsAfterCancellation");
        var wireCalls = ReadInt(result, "WireCallsAfterCancellation");
        var unexpectedException = result.GetType().GetProperty("UnexpectedException")!.GetValue(result);

        Assert.True(
            ReadBool(result, "Canceled"),
            $"Expected cancellation before argument encoding; EncodeCalls={encodeCalls}, " +
            $"WireCalls={wireCalls}, UnexpectedException={unexpectedException ?? "<none>"}.");
        Assert.Equal(0, encodeCalls);
        Assert.Equal(0, wireCalls);
        Assert.Null(unexpectedException);
    }
}
