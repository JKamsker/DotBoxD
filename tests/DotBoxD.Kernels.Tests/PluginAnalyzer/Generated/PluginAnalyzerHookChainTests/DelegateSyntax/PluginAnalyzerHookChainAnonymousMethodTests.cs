using Microsoft.CodeAnalysis;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainAnonymousMethodTests
{
    [Fact]
    public void Generated_server_remote_Where_anonymous_method_lowers_or_fails_closed()
    {
        var result = RunGenerator(RemotePluginServerUsageSource("""
            this.Server.Hooks.On<DamageEvent>()
                .Where(delegate(DamageEvent e, RemotePluginContext ctx)
                {
                    return e.TargetId == "monster-1";
                })
                .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            """));

        var lowered = result.GeneratedTrees.Any(
            tree => tree.ToString().Contains("IRKernel.FromPackage", StringComparison.Ordinal));
        var failClosed = result.Diagnostics.Any(
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Id is "DBXK100" or "DBXK113" or "DBXK114");

        Assert.True(
            lowered || failClosed,
            "Remote hook Where(delegate ...) must either lower to generated IR or fail closed with a focused DBXK diagnostic.");
    }
}
