using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class MarshallerCacheLifetimeTests
{
    [Theory]
    [InlineData("control", false)]
    [InlineData("enum-control", false)]
    [InlineData("dto", false)]
    [InlineData("dto", true)]
    [InlineData("array", false)]
    [InlineData("array", true)]
    [InlineData("list", false)]
    [InlineData("list", true)]
    [InlineData("readonly-list", false)]
    [InlineData("readonly-list", true)]
    [InlineData("map", false)]
    [InlineData("map", true)]
    [InlineData("readonly-map", false)]
    [InlineData("readonly-map", true)]
    [InlineData("enum-map", false)]
    [InlineData("enum-map", true)]
    [InlineData("readonly-enum-map", false)]
    [InlineData("readonly-enum-map", true)]
    public async Task Unused_collectible_marshalling_types_are_released(string shape, bool wireValue)
    {
        var reference = ExerciseAndRelease(shape, wireValue);
        var timer = Stopwatch.StartNew();
        while (reference.IsAlive && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (reference.IsAlive)
            {
                await Task.Delay(10);
            }
        }

        Assert.False(reference.IsAlive, $"The {shape} cache retained an unused collectible type.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExerciseAndRelease(string shape, bool wireValue)
    {
        var type = CollectibleType(shape.Contains("enum", StringComparison.Ordinal));
        if (shape.EndsWith("control", StringComparison.Ordinal))
        {
            return new WeakReference(type);
        }

        var (value, declaredType) = MarshallerCacheFixture.Value(shape, type);
        var sandbox = KernelRpcMarshaller.ToSandboxValue(value, declaredType);
        var wire = KernelRpcValueConverter.FromSandboxValue(sandbox);
        for (var i = 0; i < 2; i++)
        {
            var restored = wireValue
                ? KernelRpcMarshaller.FromKernelRpcValue(wire, declaredType)
                : KernelRpcMarshaller.FromSandboxValue(sandbox, declaredType);
            Assert.True(declaredType.IsInstanceOfType(restored));
            Assert.Equal(
                KernelRpcBinaryCodec.EncodeValue(sandbox),
                KernelRpcBinaryCodec.EncodeValue(KernelRpcMarshaller.ToSandboxValue(restored, declaredType)));
        }

        return new WeakReference(type);
    }

    private static Type CollectibleType(bool enumKey)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"RpcMarshalling-{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Values");
        if (enumKey)
        {
            var enumBuilder = module.DefineEnum("Key", TypeAttributes.Public, typeof(int));
            enumBuilder.DefineLiteral("One", 1);
            return enumBuilder.CreateTypeInfo()!;
        }

        var builder = module.DefineType("State", TypeAttributes.Public, typeof(MarshallerCacheState));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        return builder.CreateType()!;
    }
}
