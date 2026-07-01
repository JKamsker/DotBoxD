namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    // A consumer defines its OWN fluent surface — a custom-named pipeline type with custom-named seed and
    // filter methods — and opts into lowering purely by applying the public [PipelineSurface]/[PipelineStep]
    // attributes. The generator recognizes the chain by role (not by the framework's method names): "When"
    // and "Keep" have no name-based fallback, so a lowered package proves the attribute path drove recognition.
    // (The terminal keeps the standard verb "Run" only because the generator's syntactic candidate filter is a
    // name/shape fast-gate that runs without a semantic model — an inherent incremental-generator constraint.)
    private const string ConsumerSurfaceSource = """
        using System;
        using DotBoxD.Abstractions;

        namespace Consumer;

        public sealed record HitEvent(string TargetId, int Distance);

        {0}
        public sealed class Reaction<TEvent>
        {
            {1}
            public Reaction<TEvent> Keep(Func<TEvent, HookContext, bool> predicate) => this;

            [PipelineStep(PipelineStepRole.Run)]
            public Reaction<TEvent> Run(Action<TEvent, HookContext> handler) => this;

            // The install target the generated interceptor redirects to — ordinary public API the consumer
            // could equally call by hand, honoring the "delete the attribute and hand-write it" rule.
            public Reaction<TEvent> UseGeneratedChain(global::DotBoxD.Plugins.PluginPackage package) => this;
        }

        public sealed class Reactions
        {
            {2}
            public Reaction<TEvent> When<TEvent>() => new();
        }

        public static class Usage
        {
            public static void Configure(Reactions reactions)
                => reactions.When<HitEvent>()
                    .Keep((e, ctx) => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
        }
        """;

    [Fact]
    public void Lowers_a_consumer_defined_custom_named_pipeline_surface()
    {
        var source = ConsumerSurfaceSource
            .Replace("{0}", "[PipelineSurface(PipelineTransport.Local)]", StringComparison.Ordinal)
            .Replace("{1}", "[PipelineStep(PipelineStepRole.Filter)]", StringComparison.Ordinal)
            .Replace("{2}", "[PipelineStep(PipelineStepRole.Seed)]", StringComparison.Ordinal);

        var result = RunGenerator(source);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        // A package only appears if the seed ("When"), the filter ("Keep") and the terminal were all
        // recognized by attribute — the back-walk from the terminal bails on the first unrecognized node.
        Assert.Contains("HookChain_", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "DBXK113" or "DBXK100" or "DBXK114");
    }

    [Fact]
    public void Does_not_lower_a_custom_named_pipeline_surface_without_the_attributes()
    {
        // Negative control: the SAME custom-named surface with the attributes removed does not lower. Custom
        // names carry no name-based fallback, so recognition depends entirely on the opt-in attributes.
        var source = ConsumerSurfaceSource
            .Replace("{0}", string.Empty, StringComparison.Ordinal)
            .Replace("{1}", string.Empty, StringComparison.Ordinal)
            .Replace("{2}", string.Empty, StringComparison.Ordinal);

        var result = RunGenerator(source);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }
}
