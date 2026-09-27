using System.Net;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

[Trait("Category", "AllocationMeasurement")]
public sealed class SafeHttpRequestAllocationTests
{
    [Theory]
    [InlineData(false, 2904)]
    [InlineData(true, 1808)]
    public async Task Request_settings_do_not_require_a_separate_allocation(bool pendingDns, long maximumBytes)
    {
        using var fixture = new CompletedDnsFixture();
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 250; sample++)
        {
            var scenario = fixture.CreateScenario();
            using var context = scenario.Context;
            var resolution = new TaskCompletionSource<IReadOnlyList<IPAddress>>(TaskCreationOptions.RunContinuationsAsynchronously);
            SafeDnsResolver resolver = (_, _) => new ValueTask<IReadOnlyList<IPAddress>>(resolution.Task);
            if (!pendingDns)
            {
                resolution.SetResult(CompletedDnsFixture.Addresses);
            }

            // Measure the synchronous call or the work up to the pending DNS await.
            var before = GC.GetAllocatedBytesForCurrentThread();
            var request = fixture.Start(context, resolver);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var completed = request.IsCompletedSuccessfully;
            if (pendingDns)
            {
                resolution.SetResult(CompletedDnsFixture.Addresses);
            }

            Assert.Equal("ok", await request);
            Assert.Equal(!pendingDns, completed);
            Assert.True(Assert.Single(scenario.Audit.Events).Success);
            if (sample >= 50)
            {
                minimum = Math.Min(minimum, allocated);
            }
        }

        Assert.True(minimum <= maximumBytes,
            $"Starting the request allocated {minimum} B; expected at most {maximumBytes} B.");
    }
}
