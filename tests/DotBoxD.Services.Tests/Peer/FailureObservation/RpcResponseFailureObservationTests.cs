using Xunit;

namespace DotBoxD.Services.Tests.Peer.FailureObservation;

public sealed class RpcResponseFailureObservationTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in new[] { "void", "void-request", "response", "request-response" })
        {
            foreach (var serialize in new[] { false, true })
            {
                yield return [kind, serialize];
                yield return ["value-" + kind, serialize];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Send_or_setup_failure_observes_an_abandoned_response(string kind, bool serialize)
        => ResponseFailureObservation.AssertObservedAsync(
            marker => RpcResponseFailureFixture.Exercise(kind, serialize, marker));
}
