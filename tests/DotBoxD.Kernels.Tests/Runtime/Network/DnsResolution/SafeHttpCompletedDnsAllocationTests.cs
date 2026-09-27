using System.Net;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

[Trait("Category", "AllocationMeasurement")]
public sealed class SafeHttpCompletedDnsAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_dns_result_avoids_task_wrapper_allocation(bool customSource)
    {
        using var fixture = new CompletedDnsFixture();
        var cachedTask = Task.FromResult(CompletedDnsFixture.Addresses);
        SafeDnsResolver cachedResolver = (_, _) => new ValueTask<IReadOnlyList<IPAddress>>(cachedTask);
        var inlineMinimum = long.MaxValue;
        var cachedMinimum = long.MaxValue;
        for (var sample = 0; sample < 250; sample++)
        {
            var source = new CompletedDnsFixture.Source();
            source.Complete();
            SafeDnsResolver resolver = customSource
                ? (_, _) => source.Result
                : (_, _) => new ValueTask<IReadOnlyList<IPAddress>>(CompletedDnsFixture.Addresses);
            var inline = await Measure(fixture, resolver);
            var cached = await Measure(fixture, cachedResolver);
            if (sample >= 50)
            {
                inlineMinimum = Math.Min(inlineMinimum, inline);
                cachedMinimum = Math.Min(cachedMinimum, cached);
            }
            Assert.Equal(customSource ? 1 : 0, source.ResultCalls);
        }

        Assert.True(inlineMinimum <= cachedMinimum,
            $"Completed DNS allocated {inlineMinimum} B; cached-task control allocated {cachedMinimum} B.");
    }

    private static async Task<long> Measure(CompletedDnsFixture fixture, SafeDnsResolver resolver)
    {
        using var context = fixture.CreateScenario().Context;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var operation = fixture.Start(context, resolver);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(operation.IsCompletedSuccessfully);
        Assert.Equal("ok", await operation);
        return allocated;
    }
}
