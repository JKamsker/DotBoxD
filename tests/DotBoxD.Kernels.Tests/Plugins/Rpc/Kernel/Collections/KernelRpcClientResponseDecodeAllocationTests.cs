using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class KernelRpcClientResponseDecodeAllocationTests
{
    private const int WarmupIterations = 1_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public void Direct_list_response_projection_avoids_the_intermediate_wire_tree()
    {
        var payload = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(
            [KernelRpcValue.Int32(1), KernelRpcValue.Int32(2), KernelRpcValue.Int32(3)]));
        var readDirect = CreateGeneratedReader();

        _ = Measure(payload, ReadLegacy, WarmupIterations);
        _ = Measure(payload, readDirect, WarmupIterations);

        var legacy = Measure(payload, ReadLegacy, MeasurementIterations);
        var direct = Measure(payload, readDirect, MeasurementIterations);

        Assert.Equal(264L * MeasurementIterations, legacy.Bytes);
        Assert.Equal(72L * MeasurementIterations, direct.Bytes);
        Assert.Equal(192L * MeasurementIterations, legacy.Bytes - direct.Bytes);
        Assert.Equal(9L * MeasurementIterations, legacy.Checksum);
        Assert.Equal(legacy.Checksum, direct.Checksum);
    }

    private static Measurement Measure(
        byte[] payload,
        Func<byte[], List<int>> read,
        int iterations)
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var values = read(payload);
            checksum += values.Count;
            for (var item = 0; item < values.Count; item++)
            {
                checksum += values[item];
            }
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<int> ReadLegacy(byte[] payload)
    {
        var value = KernelRpcBinaryCodec.DecodeValue(payload);
        value.RequireKind(KernelRpcValueKind.List);
        var result = new List<int>(value.ItemCount);
        for (var i = 0; i < value.ItemCount; i++)
        {
            result.Add(value.GetItem(i).Int32Value);
        }

        return result;
    }

    private static Func<byte[], List<int>> CreateGeneratedReader()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(GeneratedClientSource);
        var clientType = assembly.GetType(
            "Sample.PayloadKernelDirectServerExtensionClientExtensions",
            throwOnError: true)!;
        var readerType = clientType.GetNestedType("KernelRpcResponseReader", System.Reflection.BindingFlags.NonPublic)!;
        var read = readerType.GetMethod("Read0", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        return read.CreateDelegate<Func<byte[], List<int>>>();
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private const string GeneratedClientSource = """
        using System.Collections.Generic;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public IServerExtensionClientRegistry ServerExtensions { get; } = null!;
        }

        [ServerExtension(typeof(IRemoteControl), "allocation")]
        public sealed partial class PayloadKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public List<int> Read(HookContext ctx) => new();
        }
        """;

    private readonly record struct Measurement(long Bytes, long Checksum);
}
