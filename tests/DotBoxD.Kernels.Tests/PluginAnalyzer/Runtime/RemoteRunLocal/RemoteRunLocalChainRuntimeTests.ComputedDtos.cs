namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public readonly record struct CalculatedPoint(int X, int Y)
{
    public int Sum => X + Y;
}

public sealed record CalculatedPointEvent(int Distance, CalculatedPoint Point);

public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string ComputedDtoProjectionSource = Prelude + """
        public static class ComputedDtoProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.CalculatedPointEvent>().Where(e => e.Distance <= 4)
                    .Select(e => e.Point).RunLocal((point, ctx) => { });
        }
        """;

    [Fact]
    public async Task Computed_dto_projection_round_trips_over_generated_payload_decoder()
    {
        var expected = new CalculatedPoint(3, 4);
        var payload = await PushFirstMatching(
            ComputedDtoProjectionSource,
            new CalculatedPointEvent(3, expected),
            new CalculatedPointEvent(99, new CalculatedPoint(99, 1)));

        Assert.Equal(expected, DecodeReflective<CalculatedPoint>(payload));
        Assert.Equal(expected, DecodeGenerated<CalculatedPoint>(ComputedDtoProjectionSource, payload));
    }
}
