using DotBoxD.Plugins.Indexing;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class EventIndexStatsContractTests
{
    [Theory]
    [MemberData(nameof(NegativeCounterValues))]
    public void Constructor_rejects_negative_counter_values(
        long considered,
        long prefiltered,
        long dispatched,
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new EventIndexStats(considered, prefiltered, dispatched));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 1)]
    public void Constructor_allows_non_quiescent_non_negative_snapshots(
        long considered,
        long prefiltered,
        long dispatched)
    {
        var stats = new EventIndexStats(considered, prefiltered, dispatched);

        Assert.Equal(considered, stats.Considered);
        Assert.Equal(prefiltered, stats.Prefiltered);
        Assert.Equal(dispatched, stats.Dispatched);
    }

    [Theory]
    [MemberData(nameof(NegativeCounterValues))]
    public void Initializer_rejects_negative_counter_values(
        long considered,
        long prefiltered,
        long dispatched,
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new EventIndexStats
            {
                Considered = considered,
                Prefiltered = prefiltered,
                Dispatched = dispatched,
            });

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(NegativeCounterValues))]
    public void With_copy_rejects_negative_counter_values(
        long considered,
        long prefiltered,
        long dispatched,
        string parameterName)
    {
        var stats = new EventIndexStats(0, 0, 0);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = stats with
            {
                Considered = considered,
                Prefiltered = prefiltered,
                Dispatched = dispatched,
            });

        Assert.Equal(parameterName, exception.ParamName);
    }

    public static TheoryData<long, long, long, string> NegativeCounterValues()
        => new()
        {
            { -1, 0, 0, nameof(EventIndexStats.Considered) },
            { 1, -1, 0, nameof(EventIndexStats.Prefiltered) },
            { 1, 0, -1, nameof(EventIndexStats.Dispatched) },
        };
}
