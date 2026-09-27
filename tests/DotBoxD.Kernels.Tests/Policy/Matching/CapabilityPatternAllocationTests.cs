using DotBoxD.Kernels.Policies;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Policy.Matching;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class CapabilityPatternAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("game.world.*", "game.world.move", true)]
    [InlineData("game.world.*", "game.other.move", false)]
    [InlineData("game.world.*", "game.world", false)]
    [InlineData("game.world.*", "game.world.", false)]
    [InlineData("game.*", "GAME.move", false)]
    [InlineData("game.*", "game.玩家.move", true)]
    public void Wildcard_matching_does_not_allocate_a_prefix(
        string pattern,
        string requiredCapability,
        bool expected)
    {
        for (var i = 0; i < 1_000; i++)
        {
            Assert.Equal(expected, CapabilityPattern.Matches(pattern, requiredCapability));
        }

        const int iterations = 10_000;
        var matches = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            if (CapabilityPattern.Matches(pattern, requiredCapability))
            {
                matches++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{pattern} / {requiredCapability}: {allocated / (double)iterations:N3} B/check.");
        Assert.Equal(expected ? iterations : 0, matches);
        Assert.Equal(0, allocated);
    }
}
