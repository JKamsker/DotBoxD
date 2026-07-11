namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
{
    [Fact]
    public void Same_compilation_generated_remote_stages_emit_ir_companion_interceptors()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class StageCompanionUsage
            {
                public static void Configure(AlphaPluginServer server)
                    => server.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Select(e => e.MonsterId)
                        .Run((monsterId, ctx) => ctx.Messages.Send(monsterId, "stage-ir"));
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("HookChainStageIrInterceptors", generated, StringComparison.Ordinal);
        Assert.Contains("@irFilter:", generated, StringComparison.Ordinal);
        Assert.Contains("@irFilter ??", generated, StringComparison.Ordinal);
        Assert.Contains("@irProjection:", generated, StringComparison.Ordinal);
        Assert.Contains("@irProjection ??", generated, StringComparison.Ordinal);
        Assert.Contains(
            "IRFunc<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, bool>",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "IRFunc<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, global::System.String>",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "RemoteHookStage<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, global::System.String, " +
            "global::ChainSample.Plugin.AlphaPluginContext>",
            generated,
            StringComparison.Ordinal);
    }
}
