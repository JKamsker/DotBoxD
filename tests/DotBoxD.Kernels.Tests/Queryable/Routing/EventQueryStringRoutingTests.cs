using System.Linq.Expressions;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryStringRoutingTests
{
    [Theory]
    [InlineData("alpha", false)]
    [InlineData("alpha", true)]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("\0", false)]
    [InlineData("\0", true)]
    [InlineData("\u0001", false)]
    [InlineData("\u0001", true)]
    [InlineData("a\u0001Sb", false)]
    [InlineData("a\u0001Sb", true)]
    [InlineData("λ雪🙂", false)]
    [InlineData("λ雪🙂", true)]
    [InlineData("iIß", false)]
    [InlineData("iIß", true)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public async Task String_keys_preserve_filter_results_before_and_after_compilation(string? selected, bool composite)
    {
        Expression<Func<Sample, bool>> predicate = composite
            ? e => e.Value == selected && e.Category == "chosen"
            : e => e.Value == selected;
        var host = new EventQueryHost();
        var actual = new List<Sample>();
        using var handle = await host.Query<Sample>().Where(predicate).SubscribeAsync((value, _) =>
        {
            actual.Add(value);
            return ValueTask.CompletedTask;
        });
        Sample[] events =
        [
            new(selected, "chosen"), new(selected, "other"), new(selected + "!", "chosen"),
            new(null, "chosen"), new(selected, "chosen")
        ];
        var compiled = predicate.Compile();
        var expected = new List<Sample>();
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        for (var i = 0; i < 12; i++)
        {
            expected.AddRange(events.Where(compiled));
            foreach (var value in events)
            {
                await host.PublishAsync(value, context);
            }

            Assert.Equal(expected, actual);
        }

        Assert.True(handle.IsCompiled);
        Assert.Equal(expected.Count, handle.Dispatches);
    }

    private sealed record Sample(string? Value, string Category);
}
