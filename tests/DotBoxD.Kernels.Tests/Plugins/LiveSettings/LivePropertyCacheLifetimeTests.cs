using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DotBoxD.Plugins.Runtime;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class LivePropertyCacheLifetimeTests(ITestOutputHelper output)
{
    [Fact]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public void Repeated_property_copy_preserves_the_cached_allocation_budget()
    {
        var source = new ReferenceState { Value = "ready" };
        var target = new ReferenceState();
        for (var i = 0; i < 100; i++)
        {
            LiveKernelValueFactory.CopyLiveProperties(source, target);
        }

        const int iterations = 1_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            LiveKernelValueFactory.CopyLiveProperties(source, target);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Cached property copy: {allocated / iterations} bytes per call.");
        Assert.Equal(source.Value, target.Value);
        Assert.InRange(allocated, 0, 32L * iterations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unused_collectible_setting_types_are_released(bool cacheProperties)
    {
        var reference = CreateAndReleaseSettingType(cacheProperties);
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

        Assert.False(reference.IsAlive, "The property cache must not retain an unused collectible setting type.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndReleaseSettingType(bool cacheProperties)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"LiveSettings-{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Settings")
            .DefineType("Settings", TypeAttributes.Public, typeof(SettingState));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var type = builder.CreateType()!;
        var source = (SettingState)Activator.CreateInstance(type)!;
        source.Value = 42;
        if (cacheProperties)
        {
            var method = typeof(LiveKernelValueFactory).GetMethod(nameof(LiveKernelValueFactory.CreateDraft))!;
            var draft = Assert.IsAssignableFrom<SettingState>(method.MakeGenericMethod(type).Invoke(null, [source]));
            Assert.Equal(42, draft.Value);
        }

        return new WeakReference(type);
    }

    public class SettingState
    {
        public int Value { get; set; }
    }

    private sealed class ReferenceState
    {
        public string Value { get; set; } = string.Empty;
    }
}
