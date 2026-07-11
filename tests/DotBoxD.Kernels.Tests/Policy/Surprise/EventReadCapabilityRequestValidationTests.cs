using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Policy.Surprise;

public sealed class EventReadCapabilityRequestValidationTests
{
    [Theory]
    [InlineData("event.read.*")]
    [InlineData("event.read.")]
    public async Task Prepare_rejects_non_concrete_event_read_capability_requests(string capabilityId)
    {
        var policy = new SandboxPolicy(
            "event-read-request-validation",
            SandboxEffects.Pure,
            [new CapabilityGrant(capabilityId, new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await PolicyMutationTestSupport.CreateDefaultHost().PrepareAsync(
                PolicyMutationTestSupport.EventRequestModule(capabilityId),
                policy));

        Assert.Contains(ex.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("capability request", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains("concrete", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains(capabilityId, StringComparison.Ordinal));
    }
}
