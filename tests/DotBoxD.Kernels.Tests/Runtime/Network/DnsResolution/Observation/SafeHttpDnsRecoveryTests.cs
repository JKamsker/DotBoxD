namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpDnsRecoveryTests
{
    public static IEnumerable<object[]> CompletionCases()
    {
        foreach (var mode in new[] { "caller", "context", "timeout" })
        {
            foreach (var customSource in new[] { false, true })
            {
                yield return [mode, customSource, "success"];
                yield return [mode, customSource, "cancel"];
            }
        }
    }

    [Theory]
    [MemberData(nameof(CompletionCases))]
    public Task Late_success_and_cancellation_are_consumed_once_without_side_effects(
        string mode, bool customSource, string completion)
        => SafeHttpDnsFailureObservationTests.AssertObservedAsync(marker =>
            DnsFailureObservationFixture.Run(mode, customSource, completion, marker));

    [Theory]
    [InlineData("caller", false)]
    [InlineData("caller", true)]
    [InlineData("context", false)]
    [InlineData("context", true)]
    [InlineData("timeout", false)]
    [InlineData("timeout", true)]
    public Task New_request_can_finish_before_earlier_resolution_fails(string mode, bool customSource)
        => SafeHttpDnsFailureObservationTests.AssertObservedAsync(marker =>
            DnsFailureObservationFixture.Run(mode, customSource, "failure", marker, reuse: true));
}
