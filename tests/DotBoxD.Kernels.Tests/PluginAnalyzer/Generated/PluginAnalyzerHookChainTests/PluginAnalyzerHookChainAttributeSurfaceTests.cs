using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainAttributeSurfaceTests
{
    private const string CustomNamedSurfaceSource = """
        using System;
        using DotBoxD.Abstractions;

        namespace Consumer;

        public sealed record HitEvent(string TargetId, int Distance);

        {0}
        public sealed class Reaction<TEvent>
        {
            public Reaction<TEvent> Keep(
                Func<TEvent, HookContext, bool> predicate,
                [IRBodyOf(nameof(predicate))] IRFunc<TEvent, HookContext, bool>? irPredicate = null)
                => this;

            public Reaction<TEvent> Finish(
                Action<TEvent, HookContext> handler,
                [IRBodyOf(nameof(handler))] global::DotBoxD.Plugins.IRKernel? irHandler = null)
                => this;
        }

        public sealed class Reactions
        {
            public Reaction<TEvent> When<TEvent>() => new();
        }

        public static class Usage
        {
            public static void Configure(Reactions reactions)
                => reactions.When<HitEvent>()
                    .Keep((e, ctx) => e.Distance <= 5)
                    .Finish((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
        }
        """;

    [Fact]
    public void Does_not_lower_a_custom_named_pipeline_surface()
    {
        var source = CustomNamedSurfaceSource
            .Replace("{0}", "[PipelineSurface(PipelineTransport.Local)]", StringComparison.Ordinal);

        var result = RunGenerator(source);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Does_not_lower_a_pipeline_surface_with_an_unknown_transport()
    {
        var source = CustomNamedSurfaceSource
            .Replace("{0}", "[PipelineSurface((PipelineTransport)42)]", StringComparison.Ordinal);

        var result = RunGenerator(source);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Method_group_Run_terminal_reports_not_lowered_diagnostic()
    {
        var result = RunGenerator("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record HitEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<HitEvent>().Run(Handle);

                private static void Handle(HitEvent e, HookContext ctx) { }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK114");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    private const string StandardNamedSurfaceSource = """
        using System;
        using DotBoxD.Abstractions;

        namespace Consumer;

        public sealed record HitEvent(string TargetId, int Distance);

        [PipelineSurface(PipelineTransport.Local)]
        public sealed class Flow<TEvent>
        {
            public Flow<TEvent> Where(
                Func<TEvent, HookContext, bool> predicate,
                [IRBodyOf(nameof(predicate))] IRFunc<TEvent, HookContext, bool>? irPredicate = null)
                => this;

            public Flow<TEvent> Run(
                Action<TEvent, HookContext> handler,
                [IRBodyOf(nameof(handler))] global::DotBoxD.Plugins.IRKernel? irHandler = null)
                => this;
        }

        [PipelineSurface(PipelineTransport.Local)]
        public sealed class Flows
        {
            public Flow<TEvent> On<TEvent>() => new();
        }

        public static class Usage
        {
            public static void Configure(Flows flows)
                => flows.On<HitEvent>()
                    .Where((e, ctx) => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
        }
        """;

    [Fact]
    public void Lowers_standard_named_surface_stages_with_explicit_ir_companions()
    {
        var result = RunGenerator(StandardNamedSurfaceSource);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.Contains("HookChain_", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "DBXK113" or "DBXK100" or "DBXK114");
    }

    [Fact]
    public void Does_not_lower_standard_named_stages_without_explicit_ir_companions()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Abstractions;

            namespace Consumer;

            public sealed record HitEvent(string TargetId, int Distance);

            [PipelineSurface(PipelineTransport.Local)]
            public sealed class Flow<TEvent>
            {
                public Flow<TEvent> Where(Func<TEvent, HookContext, bool> predicate) => this;
                public Flow<TEvent> Run(Action<TEvent, HookContext> handler) => this;
            }

            [PipelineSurface(PipelineTransport.Local)]
            public sealed class Flows
            {
                public Flow<TEvent> On<TEvent>() => new();
            }

            public static class Usage
            {
                public static void Configure(Flows flows)
                    => flows.On<HitEvent>()
                        .Where((e, ctx) => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            }
            """);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }
}
