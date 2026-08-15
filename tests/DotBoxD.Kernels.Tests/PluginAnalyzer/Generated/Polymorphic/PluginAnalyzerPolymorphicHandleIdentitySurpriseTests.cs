using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerPolymorphicHandleTests
{
    [Fact]
    public void Foreign_polymorphic_handle_attribute_does_not_lower_event_dto_as_key_scalar()
    {
        var foreignHandleAttribute = PluginServerGenerationTestDriver.CompileReference(
                """
                namespace DotBoxD.Abstractions;

                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class PolymorphicHandleAttribute : System.Attribute
                {
                    public PolymorphicHandleAttribute(string keyMember) { }
                }
                """,
                "ForeignPolymorphicHandle")
            .WithAliases(ImmutableArray.Create("ForeignHandle"));

        var result = PluginAnalyzerGeneratedPackageFactory.RunGeneratorWithReferences(
            """
            extern alias ForeignHandle;

            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            [ForeignHandle::DotBoxD.Abstractions.PolymorphicHandle(nameof(Id))]
            public sealed record ForeignCombatant(long Id, string Payload);

            [PolymorphicHandle(nameof(Id))]
            public sealed record RealCombatant(long Id, string Payload);

            public sealed record ForeignDamageEvent(ForeignCombatant Target);

            public sealed record RealDamageEvent(RealCombatant Target);

            [Plugin("foreign-polymorphic-handle")]
            public sealed partial class ForeignDamageKernel : IEventKernel<ForeignDamageEvent>
            {
                public bool ShouldHandle(ForeignDamageEvent e, HookContext ctx) => true;

                public void Handle(ForeignDamageEvent e, HookContext ctx)
                    => ctx.Messages.Send("sample", "handled");
            }

            [Plugin("real-polymorphic-handle")]
            public sealed partial class RealDamageKernel : IEventKernel<RealDamageEvent>
            {
                public bool ShouldHandle(RealDamageEvent e, HookContext ctx) => true;

                public void Handle(RealDamageEvent e, HookContext ctx)
                    => ctx.Messages.Send("sample", "handled");
            }
            """,
            foreignHandleAttribute);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.Contains(
            "SandboxType.Record(new global::DotBoxD.Kernels.Sandbox.SandboxType[] { global::DotBoxD.Kernels.Sandbox.SandboxType.I64, global::DotBoxD.Kernels.Sandbox.SandboxType.String })",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "parameters[0] = new global::DotBoxD.Kernels.Parameter(\"e_Target\", global::DotBoxD.Kernels.Sandbox.SandboxType.I64);",
            generated,
            StringComparison.Ordinal);
    }
}
