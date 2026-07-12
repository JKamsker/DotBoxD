using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientPostConversionCancellationSurpriseTests
{
    [Fact]
    public async Task Service_backed_generated_client_observes_cancellation_after_payload_conversion()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedSource);
        var registry = new RecordingRegistry("echo");
        var control = CreateControl(assembly, registry);
        using var cts = new CancellationTokenSource();
        var payload = CreatePayload(assembly, cts);

        var valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, payload, cts.Token])!;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => AwaitValueTaskResult(valueTask));
        Assert.Equal(0, registry.InvocationCount);
    }

    [Fact]
    public void Direct_generated_client_observes_cancellation_after_payload_conversion()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectSource);
        var registry = new RecordingRegistry("direct-echo");
        var control = CreateControl(assembly, registry);
        using var cts = new CancellationTokenSource();
        var payload = CreatePayload(assembly, cts);

        var ex = Assert.Throws<TargetInvocationException>(() =>
            assembly.GetType("Sample.Probe", throwOnError: true)!
                .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [control, payload, cts.Token]));

        Assert.IsType<OperationCanceledException>(ex.InnerException);
        Assert.Equal(0, registry.InvocationCount);
    }

    private static object CreateControl(Assembly assembly, RecordingRegistry registry)
    {
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        return Activator.CreateInstance(controlType, [registry])!;
    }

    private static object CreatePayload(Assembly assembly, CancellationTokenSource cancellation)
    {
        var payloadType = assembly.GetType("Sample.CancelingPayload", throwOnError: true)!;
        return Activator.CreateInstance(payloadType, [cancellation])!;
    }

    private static async Task<object?> AwaitValueTaskResult(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private sealed class RecordingRegistry(string expectedPluginId)
        : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        private static readonly byte[] Response = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(42));

        public int InvocationCount { get; private set; }

        public string PluginId<TService>()
            where TService : class
            => expectedPluginId;

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            Assert.Equal(expectedPluginId, pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Response);
        }
    }

    private const string ServiceBackedSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl
        {
        }

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public sealed class CancelingPayload
        {
            private readonly CancellationTokenSource _cancellation;

            public CancelingPayload(CancellationTokenSource cancellation)
                => _cancellation = cancellation;

            public int Value
            {
                get
                {
                    _cancellation.Cancel();
                    return 7;
                }
            }
        }

        public interface IEchoService
        {
            ValueTask<int> EchoAsync(CancelingPayload payload, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl))]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            public int Echo(CancelingPayload payload, HookContext ctx) => payload.Value;
        }

        public static class Probe
        {
            public static ValueTask<int> Echo(
                RemoteControl control,
                CancelingPayload payload,
                CancellationToken cancellationToken)
                => control.Echo.EchoAsync(payload, cancellationToken);
        }
        """;

    private const string DirectSource = """
        using System.Threading;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl
        {
        }

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public sealed class CancelingPayload
        {
            private readonly CancellationTokenSource _cancellation;

            public CancelingPayload(CancellationTokenSource cancellation)
                => _cancellation = cancellation;

            public int Value
            {
                get
                {
                    _cancellation.Cancel();
                    return 7;
                }
            }
        }

        [ServerExtension(typeof(IRemoteControl), "direct-echo")]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public int Echo(CancelingPayload payload, HookContext ctx) => payload.Value;
        }

        public static class Probe
        {
            public static int Echo(
                RemoteControl control,
                CancelingPayload payload,
                CancellationToken cancellationToken)
                => control.Echo(payload, cancellationToken);
        }
        """;
}
