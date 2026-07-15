using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientDirectPayloadTypeCoverageTests
{
    [Fact]
    public void Generated_client_reads_Guid_and_jagged_array_responses_directly()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ClientSource);
        var expectedId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var response = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
        [
            KernelRpcValue.Guid(expectedId),
            KernelRpcValue.List(
            [
                KernelRpcValue.List([KernelRpcValue.Int32(1), KernelRpcValue.Int32(2)]),
                KernelRpcValue.List([KernelRpcValue.Int32(3)])
            ])
        ]));
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        var payloadType = assembly.GetType("Sample.Payload", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [new RecordingRegistry(response)])!;
        var input = Activator.CreateInstance(payloadType, [Guid.Empty, Array.Empty<int[]>()])!;

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, input])!;

        Assert.Equal(expectedId, payloadType.GetProperty("Id")!.GetValue(result));
        var grid = Assert.IsType<int[][]>(payloadType.GetProperty("Grid")!.GetValue(result));
        Assert.Equal([1, 2], grid[0]);
        Assert.Equal([3], grid[1]);
    }

    private const string ClientSource = """
        using System;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public sealed record Payload(Guid Id, int[][] Grid);

        [ServerExtension(typeof(IRemoteControl), "type-coverage")]
        public sealed partial class PayloadKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public Payload Echo(Payload value, HookContext ctx) => value;
        }

        public static class Probe
        {
            public static Payload Echo(RemoteControl control, Payload value) => control.Echo(value);
        }
        """;

    private sealed class RecordingRegistry(byte[] response)
        : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => "type-coverage";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("type-coverage", pluginId);
            Assert.NotEmpty(arguments);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
