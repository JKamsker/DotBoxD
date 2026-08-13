using DotBoxD.Kernels.Debugging;
using DotBoxD.Plugins;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.HookChainRuntimeTestCompiler;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class HookChainDebugInfoRuntimeTests
{
    private const string SendChainSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where((e, ctx) => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
        }
        """;

    private const string ResultChainSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace ChainSample;

        [Hook("chain.damage", typeof(DamageResult))]
        public sealed record DamageContext(int Damage);

        [HookResult]
        public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<DamageContext>()
                    .Where(e => e.Damage > 10)
                    .Register(e => new DamageResult { Success = true, Damage = e.Damage }, priority: 5);
        }
        """;

    private const string ProjectedRunSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 4)
                    .Select(e => e.MonsterId)
                    .Run((monsterId, ctx) => ctx.Messages.Send(monsterId, "calm:inline"));
        }
        """;

    private const string WholeEventRunSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "observe:inline"));
        }
        """;

    private const string ComputedProjectedRunSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Select(e => e.MonsterId + ":selected")
                    .Run((monsterId, ctx) => ctx.Messages.Send(monsterId, "calm:inline"));
        }
        """;

    [Fact]
    public void Send_chain_maps_filter_and_terminal_into_both_execution_functions()
    {
        var package = PackageFrom(Compile(SendChainSource, enableInterceptors: true));

        AssertMapped(package, SendChainSource, "e.Distance", "e.MonsterId");
    }

    [Fact]
    public void Result_hook_maps_filter_and_result_body_into_both_execution_functions()
    {
        var package = PackageFrom(Compile(ResultChainSource, enableInterceptors: true));

        AssertMapped(package, ResultChainSource, "e.Damage");
    }

    [Fact]
    public void Projected_run_maps_the_authored_event_projection_and_context_names()
    {
        var package = PackageFrom(Compile(ProjectedRunSource, enableInterceptors: true));
        var bindings = Assert.IsType<KernelDebugInfo>(package.DebugInfo).VariableBindings;

        Assert.Contains(bindings, binding => binding.FunctionId == "ShouldHandle" && binding.SourceName == "e.Distance");
        Assert.Contains(bindings, binding =>
            binding.FunctionId == "Handle" &&
            binding.SlotName == "e_MonsterId" &&
            binding.SourceName == "monsterId");
        Assert.DoesNotContain(bindings, binding =>
            binding.FunctionId == "Handle" &&
            binding.SlotName.StartsWith("$dotboxd.select.", StringComparison.Ordinal) &&
            binding.SourceName == "monsterId");
        Assert.Contains(bindings, binding => binding.FunctionId == "Handle" && binding.SourceName == "ctx.Messages");
        Assert.DoesNotContain(bindings, binding => binding.SourceName.StartsWith("monsterId.", StringComparison.Ordinal));
    }

    [Fact]
    public void Whole_event_run_maps_expandable_event_and_context_parameters()
    {
        var package = PackageFrom(Compile(WholeEventRunSource, enableInterceptors: true));
        var debugInfo = Assert.IsType<KernelDebugInfo>(package.DebugInfo);
        var bindings = debugInfo.VariableBindings;
        var nodes = SandboxNodeMap.Create(package.Module).Nodes.ToDictionary(node => node.Id);

        Assert.All(debugInfo.SequencePoints, point => Assert.Equal("Handle", nodes[point.NodeId].FunctionId));
        Assert.Contains(bindings, binding =>
            binding.FunctionId == "Handle" &&
            binding.SourceName == "e" &&
            binding.TypeName == typeof(ChainAggroEvent).FullName);
        Assert.Contains(bindings, binding => binding.FunctionId == "Handle" && binding.SourceName == "e.MonsterId");
        Assert.Contains(bindings, binding => binding.FunctionId == "Handle" && binding.SourceName == "e.Distance");
        Assert.Contains(bindings, binding => binding.FunctionId == "Handle" && binding.SourceName == "ctx");
        Assert.Contains(bindings, binding => binding.FunctionId == "Handle" && binding.SourceName == "ctx.Messages");
        Assert.Contains(bindings, binding =>
            binding.FunctionId == "Handle" && binding.SourceName == "ctx.CancellationToken");
    }

    [Fact]
    public void Computed_projection_keeps_its_generated_value_binding()
    {
        var package = PackageFrom(Compile(ComputedProjectedRunSource, enableInterceptors: true));
        var bindings = Assert.IsType<KernelDebugInfo>(package.DebugInfo).VariableBindings;

        Assert.Contains(bindings, binding =>
            binding.FunctionId == "Handle" &&
            binding.SlotName.StartsWith("$dotboxd.select.", StringComparison.Ordinal) &&
            binding.SourceName == "monsterId");
    }

    private static void AssertMapped(PluginPackage package, string source, params string[] variableNames)
    {
        var debugInfo = Assert.IsType<KernelDebugInfo>(package.DebugInfo);
        Assert.Equal(2, debugInfo.Documents.Count);
        Assert.All(debugInfo.Documents, document => Assert.True(document.MatchesSource(source)));
        var nodes = SandboxNodeMap.Create(package.Module).Nodes.ToDictionary(node => node.Id);
        var functions = new[] { package.Entrypoints.ShouldHandle, package.Entrypoints.Handle };
        Assert.All(functions, functionId =>
        {
            var locations = debugInfo.SequencePoints
                .Where(point => nodes[point.NodeId].FunctionId == functionId)
                .Select(point => (point.Span.Line, point.Span.Column, point.Span.EndLine, point.Span.EndColumn))
                .Distinct();
            Assert.True(locations.Count() > 1);
        });
        Assert.All(variableNames, sourceName =>
            Assert.Contains(debugInfo.VariableBindings, binding => binding.SourceName == sourceName));
    }
}
