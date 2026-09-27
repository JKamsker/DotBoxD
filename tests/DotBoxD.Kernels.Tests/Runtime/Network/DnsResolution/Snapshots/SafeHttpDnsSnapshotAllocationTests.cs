using System.Net;
using System.Runtime.CompilerServices;
using DotBoxD.Hosting.Http.Internal;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class SafeHttpDnsSnapshotAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("array", false, 120)]
    [InlineData("list", false, 120)]
    [InlineData("readonly", false, 120)]
    [InlineData("custom", false, 152)]
    [InlineData("array", true, 200)]
    [InlineData("list", true, 200)]
    [InlineData("readonly", true, 200)]
    [InlineData("custom", true, 232)]
    public void Two_address_snapshot_allocations_remain_bounded(string kind, bool ipv6, long maximumBytes)
    {
        var address = ipv6 ? IPAddress.Parse("2001:4860:4860::8888%7") : IPAddress.Parse("93.184.216.34");
        var fixture = new DnsSnapshotFixture(kind, [address, address]);
        SafeDnsResolver resolver = (_, _) => ValueTask.FromResult(fixture.Source);
        _ = ResolveMany(resolver, 2_000);
        var minimum = long.MaxValue;
        IReadOnlyList<IPAddress>? snapshot = null;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            snapshot = ResolveMany(resolver, 10_000);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(address, snapshot[0]);
        Assert.NotSame(address, snapshot[0]);
        if (fixture.Source is DnsSnapshotFixture.ObservedAddresses observed)
        {
            Assert.Equal(52_000, observed.Enumerations);
        }
        output.WriteLine($"{kind}, {(ipv6 ? "IPv6" : "IPv4")}: {minimum / 10_000d} B/two-address snapshot.");
        Assert.InRange(minimum, 0, maximumBytes * 10_000);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static IReadOnlyList<IPAddress> ResolveMany(SafeDnsResolver resolver, int count)
    {
        IReadOnlyList<IPAddress> result = [];
        for (var index = 0; index < count; index++)
        {
            result = SafeHttpDnsResolution.ResolveAsync(
                DnsSnapshotFixture.Grant, "api.example.com", resolver, CancellationToken.None).GetAwaiter().GetResult();
        }
        return result;
    }
}
