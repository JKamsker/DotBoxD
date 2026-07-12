using Microsoft.CodeAnalysis;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainStageIrTests
{
    [Fact]
    public void Stage_interceptors_pass_generated_ir_func_companions()
    {
        var (output, result) = RunGeneratorCore("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance, int MonsterLevel, int PlayerLevel);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Where((e, ctx) => e.Distance <= 5)
                        .Select((e, ctx) => e.MonsterLevel - e.PlayerLevel)
                        .Where((gap, ctx) => gap >= 3)
                        .Run((gap, ctx) => ctx.Messages.Send("monster", "calm"));
            }
            """);

        AssertNoUnexpectedErrors(output);
        var generated = GeneratedSource(result);

        Assert.Contains("HookChainStageIrInterceptors", generated, StringComparison.Ordinal);
        Assert.Contains("CreateIRFunc()", generated, StringComparison.Ordinal);
        Assert.Contains("@irFilter:", generated, StringComparison.Ordinal);
        Assert.Contains("@irProjection:", generated, StringComparison.Ordinal);
        Assert.Contains("IRFunc<global::Sample.MonsterAggroEvent, global::DotBoxD.Abstractions.HookContext, bool>", generated, StringComparison.Ordinal);
        Assert.Contains("IRFunc<int, global::DotBoxD.Abstractions.HookContext, bool>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage_interceptors_replace_explicit_null_ir_companions()
    {
        var (output, result) = RunGeneratorCore("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Where(e => e.Damage > 0, irFilter: null)
                        .Select(e => e.TargetId, irProjection: null)
                        .Run((targetId, ctx) => ctx.Messages.Send(targetId, "damage"));
            }
            """);

        AssertNoUnexpectedErrors(output);
        var generated = GeneratedSource(result);

        Assert.Contains("@irFilter:", generated, StringComparison.Ordinal);
        Assert.Contains("@irProjection:", generated, StringComparison.Ordinal);
        Assert.Contains("IRFunc<global::Sample.DamageEvent, bool>", generated, StringComparison.Ordinal);
        Assert.Contains("IRFunc<global::Sample.DamageEvent, string>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage_interceptors_include_inherited_event_members_in_the_ir_input_shape()
    {
        var (output, result) = RunGeneratorCore("""
            #nullable enable

            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public abstract record DamageEventBase(string MonsterId);

            [Hook("damage", typeof(DamageResult))]
            public sealed record DamageEvent(string MonsterId, int Damage) : DamageEventBase(MonsterId);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Where(e => e.MonsterId == "boss" && e.Damage > 10)
                        .RegisterLocal((e, _) => new DamageResult(true, null, e.Damage), priority: 7);
            }
            """);

        AssertNoUnexpectedErrors(output);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK113");

        var generated = GeneratedSource(result);
        Assert.Contains("HookChainStageIrInterceptors", generated, StringComparison.Ordinal);
        Assert.Contains("IRFunc<global::Sample.DamageEvent, bool>", generated, StringComparison.Ordinal);
        Assert.Contains("SandboxType.String, global::DotBoxD.Kernels.Sandbox.SandboxType.I32", generated, StringComparison.Ordinal);
    }

    private static string GeneratedSource(GeneratorDriverRunResult result)
        => string.Join(Environment.NewLine, result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

    private static void AssertNoUnexpectedErrors(Compilation output)
        => Assert.DoesNotContain(
            output.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS9137");
}
