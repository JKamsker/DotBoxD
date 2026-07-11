using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.Capability;

public sealed class EventPropertyCapabilityIdValidationSurpriseTests
{
    [Fact]
    public void Event_property_capability_with_control_character_id_reports_generation_diagnostic()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record GatedEvent(
                string TargetId,
                string Message,
                [property: Capability("event.read.bad\u0001id")] int Health);

            [Plugin("control-character-capability")]
            public sealed partial class GatedKernel : IEventKernel<GatedEvent>
            {
                public bool ShouldHandle(GatedEvent e, HookContext ctx) => e.Health > 0;

                public void Handle(GatedEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("Capability", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("capability id", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedTrees);
    }
}
