using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class EventCapabilityCacheLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unused_collectible_event_types_are_released(bool validate, bool gated)
    {
        var reference = CreateAndReleaseEventType(validate, gated);
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

        Assert.False(reference.IsAlive, "The capability cache must not retain an unused collectible event type.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndReleaseEventType(bool validate, bool gated)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"EventCapabilities-{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Events")
            .DefineType("Event", TypeAttributes.Public, gated ? typeof(GatedEventState) : typeof(EventState));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var type = builder.CreateType()!;
        if (validate)
        {
            var environment = PluginEventAdapterValidationCacheTestFixture.Create(grantReadCapability: true);
            var method = typeof(PluginEventCapabilityValidator)
                .GetMethod(nameof(PluginEventCapabilityValidator.Validate))!
                .MakeGenericMethod(type);
            object?[] arguments =
            [
                environment.Plan,
                environment.Entrypoints,
                PluginEventAdapterValidationCacheTestFixture.Parameters
            ];
            _ = method.Invoke(null, arguments);
            _ = method.Invoke(null, arguments);
        }

        return new WeakReference(type);
    }

    public class EventState
    {
        public int Value { get; set; }
    }

    public class GatedEventState
    {
        [Capability(PluginEventAdapterValidationCacheTestFixture.ReadCapability)]
        public int Value { get; set; }
    }
}
