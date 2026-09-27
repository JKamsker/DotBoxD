using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Authoring;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class EventQueryStringRoutingAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Property", "other", 40)]
    [InlineData("Field", "other", 40)]
    [InlineData("Nested", "other", 40)]
    [InlineData("Composite", "other", 56)]
    [InlineData("Property", "", 32)]
    [InlineData("Field", "", 32)]
    [InlineData("Nested", "", 32)]
    [InlineData("Composite", "", 40)]
    [InlineData("Property", null, 0)]
    [InlineData("Field", null, 0)]
    [InlineData("Nested", null, 0)]
    [InlineData("Composite", null, 0)]
    public async Task String_routing_allocates_only_the_final_key(string shape, string? text, int bytesPerPublish)
    {
        Expression<Func<Sample, bool>> predicate = shape switch
        {
            "Property" => e => e.Value == "selected",
            "Field" => e => e.Field == "selected",
            "Nested" => e => e.Child.Value == "selected",
            "Composite" => e => e.Value == "selected" && e.Category == "fixed",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var host = new EventQueryHost();
        using var handle = await host.Query<Sample>().Where(predicate)
            .SubscribeAsync((_, _) => ValueTask.CompletedTask);
        var value = new Sample(text);
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        _ = Measure(host, value, context);

        var bytes = Measure(host, value, context);

        output.WriteLine($"{shape}, text={text ?? "<null>"}: {bytes / 1000D} B/publish.");
        Assert.True(handle.Plan.IsRoutable);
        Assert.Equal(2000, handle.EventsObserved);
        Assert.Equal(0, handle.FilterEvaluations);
        Assert.True(bytes <= bytesPerPublish * 1000L,
            $"String routing allocated {bytes / 1000D} B/publish; budget is {bytesPerPublish}.");
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

    private sealed class Sample(string? value)
    {
        public string? Value { get; } = value;
        public readonly string? Field = value;
        public ChildValue Child { get; } = new(value);
        public string Category => "fixed";
    }

    private sealed record ChildValue(string? Value);
}
