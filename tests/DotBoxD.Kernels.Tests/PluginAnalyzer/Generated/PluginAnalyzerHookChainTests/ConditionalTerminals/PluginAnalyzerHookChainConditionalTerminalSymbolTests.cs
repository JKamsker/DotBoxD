using Microsoft.CodeAnalysis;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainConditionalTerminalSymbolTests
{
    [Fact]
    public void Remote_conditional_user_extension_Run_does_not_report_terminal_diagnostic()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class UserRunExtensions
            {
                public static int Run(this RemoteHookPipeline<DamageEvent> pipeline, string value)
                    => value.Length;
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    RemoteHookPipeline<DamageEvent>? pipeline = hooks.On<DamageEvent>();

                    pipeline?.Run("ordinary-extension");
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
    }

    [Fact]
    public void Remote_conditional_real_Run_terminal_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    RemoteHookPipeline<DamageEvent>? pipeline = hooks.On<DamageEvent>();

                    pipeline?.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Run", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("conditional access", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Conditional_role_terminal_with_invalid_lambda_shape_does_not_report_DBXK100()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            [PipelineSurface(PipelineTransport.Remote)]
            public sealed class Flow<TEvent>
            {
                public Flow<TEvent> Run(
                    Action handler,
                    [IRBodyOf(nameof(handler))] global::DotBoxD.Plugins.IRKernel? irHandler = null)
                    => this;
            }

            public static class Usage
            {
                public static void Configure(Flow<string>? flow)
                {
                    flow?.Run(() => { });
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
    }
}
