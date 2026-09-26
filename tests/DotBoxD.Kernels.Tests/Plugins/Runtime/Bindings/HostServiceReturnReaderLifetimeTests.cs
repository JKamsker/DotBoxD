using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime.Bindings;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class HostServiceReturnReaderLifetimeTests
{
    [Theory]
    [InlineData("uncached")]
    [InlineData(nameof(Service<Payload>.Get))]
    [InlineData(nameof(Service<Payload>.GetTask))]
    [InlineData(nameof(Service<Payload>.GetValueTask))]
    public async Task Return_readers_release_unused_collectible_payload_types(string methodName)
    {
        var reference = CreateAndReleasePayloadType(methodName);
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

        Assert.False(reference.IsAlive, "The return-reader cache must not retain an unused collectible payload type.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndReleasePayloadType(string methodName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"HostServicePayloads-{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Payloads")
            .DefineType("Payload", TypeAttributes.Public, typeof(Payload));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var type = builder.CreateType()!;
        var serviceType = typeof(Service<>).MakeGenericType(type);
        var service = Activator.CreateInstance(serviceType);
        if (methodName != "uncached")
        {
            var target = new HostServiceCallTarget(serviceType.GetMethod(methodName)!);
            var raw = target.Invoke(service, []);
            var value = target.ReadReturnAsync(raw, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(42, Assert.IsAssignableFrom<Payload>(value).Value);
        }

        return new WeakReference(type);
    }

    public class Payload
    {
        public int Value { get; set; } = 42;
    }

    public sealed class Service<T> where T : Payload, new()
    {
        public T Get() => new();
        public Task<T> GetTask() => Task.FromResult(new T());
        public ValueTask<T> GetValueTask() => ValueTask.FromResult(new T());
    }
}
