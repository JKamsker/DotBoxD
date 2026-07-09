using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainRemoteStagedUseTests
{
    [Fact]
    public void Remote_staged_Use_through_local_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_subscription_staged_Use_through_local_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteSubscriptionRegistry subscriptions)
                {
                    var staged = subscriptions.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_discarded_staged_hook_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var pipeline = hooks.On<DamageEvent>();
                    pipeline.Where(e => e.TargetId == "monster-1");
                    pipeline.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("discarding", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_discarded_staged_subscription_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteSubscriptionRegistry subscriptions)
                {
                    var pipeline = subscriptions.On<DamageEvent>();
                    pipeline.Select(e => e.TargetId);
                    pipeline.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("discarding", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_server_remote_staged_Use_through_local_reports_DBXK100_error()
    {
        var result = RunGenerator(RemotePluginServerUsageSource("""
            var staged = this.Server.Hooks.On<DamageEvent>()
                .Where(e => e.TargetId == "monster-1");
            staged.Use<DamageKernel>();
            """));

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_server_remote_discarded_staged_hook_through_registry_alias_reports_DBXK100_error()
    {
        var result = RunGenerator(RemotePluginServerUsageSource("""
            var hooks = this.Server.Hooks;
            var pipeline = hooks.On<DamageEvent>();
            pipeline.Where(e => e.TargetId == "monster-1");
            pipeline.Use<DamageKernel>();
            """));

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("discarding", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_server_remote_staged_Run_through_local_lowers()
    {
        var result = RunGenerator(RemotePluginServerUsageSource("""
            var staged = this.Server.Hooks.On<DamageEvent>()
                .Where(e => e.TargetId == "monster-1");
            staged.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            """));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("IRKernel.FromPackage", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_server_remote_staged_Use_from_cross_tree_helper_reports_DBXK100_without_DBXK117()
    {
        var helperTree = CSharpSyntaxTree.ParseText(RemotePluginServerSource("""
            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class HookStages
            {
                public static DotBoxD.Plugins.Runtime.Hooks.RemoteHookStage<DamageEvent, RemotePluginContext> Damage(
                    RemotePluginServer server)
                {
                    var staged = server.Hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    return staged;
                }
            }
            """), HookChainGeneratorTestSupport.ParseOptions);
        var usageTree = CSharpSyntaxTree.ParseText("""
            namespace Sample.Plugin
            {
                public sealed class Usage
                {
                    public RemotePluginServer Server { get; init; } = null!;

                    public void Configure()
                        => HookStages.Damage(Server).Use<DamageKernel>();
                }
            }
            """, HookChainGeneratorTestSupport.ParseOptions);

        var result = RunGenerator(helperTree, usageTree);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK117");
        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
