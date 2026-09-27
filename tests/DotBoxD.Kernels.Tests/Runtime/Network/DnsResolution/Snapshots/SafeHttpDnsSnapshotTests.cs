using System.Net;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpDnsSnapshotTests
{
    public static IEnumerable<object[]> SnapshotCases()
    {
        foreach (var kind in new[] { "array", "list", "readonly", "custom" })
        {
            foreach (var delivery in new[] { "inline", "task", "source", "pending" })
            {
                yield return [kind, delivery];
            }
        }
    }

    [Theory]
    [MemberData(nameof(SnapshotCases))]
    public async Task Resolved_addresses_preserve_values_order_and_duplicates_without_sharing_source_state(
        string kind, string delivery)
    {
        var fixture = new DnsSnapshotFixture(kind);
        var expected = fixture.Originals.Select(address => address.ToString()).ToArray();
        var snapshot = await fixture.ResolveAsync(delivery);
        Assert.NotSame(fixture.Source, snapshot);
        Assert.Equal(expected.Length, snapshot.Count);
        for (var index = 0; index < snapshot.Count; index++)
        {
            Assert.NotSame(fixture.Originals[index], snapshot[index]);
            Assert.Equal(fixture.Originals[index], snapshot[index]);
        }
        if (fixture.Source is DnsSnapshotFixture.ObservedAddresses observed)
        {
            Assert.Equal(1, observed.Enumerations);
        }

        // Exercise ordinary post-copy changes to both supported mutable address properties.
#pragma warning disable CS0618 // IPv4 Address is obsolete but remains a public mutable property that snapshots must isolate.
        fixture.Originals[0].Address = IPAddress.Parse("8.8.8.8").Address;
#pragma warning restore CS0618
        fixture.Originals[1].ScopeId = 11;
        fixture.ChangeCollection();

        Assert.Equal(expected, snapshot.Select(address => address.ToString()));
        Assert.Equal(7, snapshot[1].ScopeId);
        Assert.True(snapshot[2].IsIPv4MappedToIPv6);
        Assert.Equal(snapshot[0], snapshot[3]);
    }
}
