using DotBoxD.Services.Client;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed class PendingRequestsTakeTests
{
    [Fact]
    public async Task TryTake_RemovesAndReturnsOnlyTheMatchedResponse()
    {
        using var requests = new PendingRequests();
        Assert.True(requests.TryAdd(messageId: 41, out var expected));
        Assert.True(requests.TryAdd(messageId: 42, out var remaining));

        Assert.True(requests.TryTake(messageId: 41, out var actual));

        Assert.Same(expected, actual);
        Assert.Equal(1, requests.Count);
        Assert.False(requests.TryTake(messageId: 41, out var missing));
        Assert.Null(missing);

        var firstError = new InvalidOperationException("first response completed by the test");
        actual.SetError(firstError);
        Assert.Same(firstError, await Assert.ThrowsAsync<InvalidOperationException>(() => expected.Task));

        Assert.True(requests.TryTake(messageId: 42, out actual));
        Assert.Same(remaining, actual);
        Assert.Equal(0, requests.Count);

        var secondError = new InvalidOperationException("second response completed by the test");
        actual.SetError(secondError);
        Assert.Same(secondError, await Assert.ThrowsAsync<InvalidOperationException>(() => remaining.Task));
    }
}
