using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Authoring;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class EventQueryBooleanRoutingAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Property", false, 56)]
    [InlineData("Property", true, 56)]
    [InlineData("Field", false, 56)]
    [InlineData("Field", true, 56)]
    [InlineData("Nested", false, 56)]
    [InlineData("Nested", true, 56)]
    [InlineData("Composite", false, 88)]
    [InlineData("Composite", true, 88)]
    [InlineData("Nullable", false, 56)]
    [InlineData("Nullable", true, 56)]
    [InlineData("Nullable", null, 0)]
    public async Task Boolean_routing_avoids_temporary_query_values(string shape, bool? value, int bytesPerPublish)
    {
        var selected = value is not true;
        Expression<Func<Sample, bool>> predicate = shape switch
        {
            "Property" => e => e.Value == selected,
            "Field" => e => e.Field == selected,
            "Nested" => e => e.Child.Value == selected,
            "Composite" => e => e.Value == selected && e.Other == selected,
            "Nullable" => e => e.Optional == selected,
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var host = new EventQueryHost();
        using var handle = await host.Query<Sample>().Where(predicate)
            .SubscribeAsync((_, _) => ValueTask.CompletedTask);
        var input = new Sample(value);
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        _ = Measure(host, input, context);

        var bytes = Measure(host, input, context);

        output.WriteLine($"{shape}, value={value}: {bytes / 1000D} B/publish.");
        Assert.True(handle.Plan.IsRoutable);
        Assert.Equal(2000, handle.EventsObserved);
        Assert.Equal(0, handle.FilterEvaluations);
        Assert.True(bytes <= bytesPerPublish * 1000L,
            $"Boolean routing allocated {bytes / 1000D} B/publish; budget is {bytesPerPublish}.");
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long Measure(EventQueryHost host, Sample value, HookContext context)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            host.PublishAsync(value, context).GetAwaiter().GetResult();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class Sample(bool? value)
    {
        public bool Value { get; } = value.GetValueOrDefault();
        public readonly bool Field = value.GetValueOrDefault();
        public bool Other => Value;
        public bool? Optional { get; } = value;
        public ChildValue Child { get; } = new(value.GetValueOrDefault());
    }

    private sealed record ChildValue(bool Value);
}
