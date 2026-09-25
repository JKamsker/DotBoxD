using System.Linq.Expressions;
using DotBoxD.Queryable.Authoring;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryNumericRoutingAllocationTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "AllocationMeasurement")]
    public async Task MixedNumericRouting_DoesNotAddPerEventAllocations()
    {
        var integerBytes = await MeasureAsync(e => e.Value == 42);
        var decimalBytes = await MeasureAsync(e => (decimal)e.Value == 42m);
        output.WriteLine($"Routing allocation per event: integer literal {integerBytes / 1_000}, decimal literal {decimalBytes / 1_000} bytes.");

        Assert.True(decimalBytes <= integerBytes + 1_000,
            $"Mixed numeric routing allocated {decimalBytes} bytes versus {integerBytes} for the same numeric domain.");
    }

    private static async Task<long> MeasureAsync(Expression<Func<Sample, bool>> predicate)
    {
        var host = new EventQueryHost();
        using var subscription = await host.Query<Sample>().Where(predicate)
            .SubscribeAsync((_, _) => ValueTask.CompletedTask);
        var value = new Sample(41);
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        for (var i = 0; i < 100; i++)
        {
            await host.PublishAsync(value, context);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            await host.PublishAsync(value, context);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, subscription.FilterEvaluations);
        return bytes;
    }

    private sealed record Sample(int Value);
}
