using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientResponseCancellationSurpriseTests
{
    [Fact]
    public async Task Service_backed_generated_client_observes_cancellation_before_decoding_response()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedSource);
        using var cts = new CancellationTokenSource();
        var registry = new CancellingRegistry("counter", cts.Cancel);
        var control = CreateControl(assembly, registry);

        var valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Count", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, cts.Token])!;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => AwaitValueTaskResult(valueTask));
        Assert.Equal(1, registry.InvocationCount);
    }

    [Fact]
    public void Direct_generated_client_observes_cancellation_before_decoding_response()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectSource);
        using var cts = new CancellationTokenSource();
        var registry = new CancellingRegistry("direct-counter", cts.Cancel);
        var control = CreateControl(assembly, registry);

        var ex = Assert.Throws<TargetInvocationException>(() =>
            assembly.GetType("Sample.Probe", throwOnError: true)!
                .GetMethod("Count", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [control, cts.Token]));

        Assert.IsType<OperationCanceledException>(ex.InnerException);
        Assert.Equal(1, registry.InvocationCount);
    }

    private static object CreateControl(Assembly assembly, CancellingRegistry registry)
    {
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        return Activator.CreateInstance(controlType, [registry])!;
    }

    private static async Task<object?> AwaitValueTaskResult(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private sealed class CancellingRegistry(string expectedPluginId, Action cancel)
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
            Assert.False(cancellationToken.IsCancellationRequested);
            cancel();
            Assert.True(cancellationToken.IsCancellationRequested);
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

        public interface ICounterService
        {
            ValueTask<int> CountAsync(CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl))]
        [ServerExtension("counter", typeof(ICounterService))]
        public sealed partial class CounterKernel
        {
            public int Count(HookContext ctx) => 1;
        }

        public static class Probe
        {
            public static ValueTask<int> Count(RemoteControl control, CancellationToken cancellationToken)
                => control.Counter.CountAsync(cancellationToken);
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

        [ServerExtension(typeof(IRemoteControl), "direct-counter")]
        public sealed partial class CounterKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public int Count(HookContext ctx) => 1;
        }

        public static class Probe
        {
            public static int Count(RemoteControl control, CancellationToken cancellationToken)
                => control.Count(cancellationToken);
        }
        """;
}
