namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// #1 (High, fail-open) from the 2026-06-26 surprise hunt: a whole-event <c>RunLocal</c> chain (no
/// <c>Select</c>) pushes every event property across the IPC boundary but never collected the
/// <c>[Capability]</c> gate of a gated property, so a gated field crossed the wire and the package
/// declared no capability. The same read written as <c>.Select(e =&gt; e.Secret)</c> does require it, so
/// adding/removing a Select must not flip the security posture.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string GatedWholeEventSource = """
        using System.Collections.Generic;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace CapSample;

        public sealed record GatedAggroEvent(
            string MonsterId,
            int Distance,
            [property: Capability("event.read.secret")] int Secret);

        public static class GatedWholeEventUsage
        {
            public static readonly List<GatedAggroEvent> Received = new();

            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<GatedAggroEvent>()
                    .Where(e => e.Distance <= 4)
                    .RunLocal((e, ctx) => Received.Add(e));
        }
        """;

    [Fact]
    public void Whole_event_chain_collects_capabilities_of_gated_event_properties()
    {
        var package = LowerToPackage(GatedWholeEventSource);

        var subscription = Assert.Single(package.Manifest.Subscriptions);
        Assert.Equal("record", subscription.ProjectedType); // no Select => whole-event projection

        // The whole-event push reads the gated 'Secret' property, so its capability must be required.
        Assert.Contains("event.read.secret", package.Manifest.RequiredCapabilities);
    }
}
