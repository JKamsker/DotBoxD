using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.TestFixtures.MergeableIr;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class LoweredPipelineDebugCompositionTests
{
    [Fact]
    public void Generated_where_and_select_steps_compose_with_local_source_maps()
    {
        var steps = MergeableIrPipelineFixture.ConfigureSteps();

        var result = LoweredPipelineDebugComposer.Compose(
            new LoweredPipelineComposition("generated-pipeline", steps, SandboxType.String));

        Assert.All(steps, step => Assert.NotNull(step.DebugInfo));
        var debugInfo = Assert.IsType<KernelDebugInfo>(result.DebugInfo);
        Assert.Equal(2, debugInfo.Documents.Count);
        Assert.NotEmpty(debugInfo.SequencePoints);
        Assert.Contains(debugInfo.VariableBindings, binding => binding.SourceName == "e");
        var nodes = SandboxNodeMap.Create(result.Module).Nodes.ToDictionary(node => node.Id);
        Assert.All(
            new[] { "ShouldHandle", "Handle" },
            functionId => Assert.True(debugInfo.SequencePoints
                .Where(point => nodes[point.NodeId].FunctionId == functionId)
                .Select(point => (point.Span.Line, point.Span.Column, point.Span.EndLine, point.Span.EndColumn))
                .Distinct()
                .Count() > 1));
    }

    [Fact]
    public void Debug_composer_preserves_documents_duplicated_stage_maps_and_source_variables()
    {
        var source = "items.Select(item => item + 1).Where(item => item > 0);";
        var document = KernelDebugDocument.FromSource("pipeline", "/mapped/Pipeline.cs", source);
        var projectionSpan = new SourceSpan(1, 21, document.Id, 1, 29);
        var filterSpan = new SourceSpan(1, 45, document.Id, 1, 53);
        var projection = Step(
            LoweredPipelineStepKind.Projection,
            "int",
            new BinaryExpression(Current(projectionSpan), "+", Literal(1, projectionSpan), projectionSpan),
            document,
            "item");
        var filter = Step(
            LoweredPipelineStepKind.Filter,
            "bool",
            new BinaryExpression(Current(filterSpan), ">", Literal(0, filterSpan), filterSpan),
            document,
            "item");
        var composition = new LoweredPipelineComposition("pipeline", [projection, filter], SandboxType.I32);

        var result = LoweredPipelineDebugComposer.Compose(composition);

        var debugInfo = Assert.IsType<KernelDebugInfo>(result.DebugInfo);
        Assert.Equal(document, Assert.Single(debugInfo.Documents));
        var nodesById = SandboxNodeMap.Create(result.Module).Nodes.ToDictionary(node => node.Id);
        var mappedFunctions = debugInfo.SequencePoints
            .Select(point => nodesById[point.NodeId].FunctionId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(composition.ShouldHandleFunctionId, mappedFunctions);
        Assert.Contains(composition.HandleFunctionId, mappedFunctions);
        Assert.Contains(debugInfo.VariableBindings, binding =>
            binding.FunctionId == composition.ShouldHandleFunctionId &&
            binding.SlotName == "current0" &&
            binding.SourceName == "item");
        Assert.Contains(debugInfo.VariableBindings, binding =>
            binding.FunctionId == composition.HandleFunctionId &&
            binding.SlotName == "current0" &&
            binding.SourceName == "item");
    }

    [Fact]
    public void Debug_composer_rejects_conflicting_document_definitions()
    {
        var first = KernelDebugDocument.FromSource("pipeline", "/mapped/One.cs", "one");
        var second = KernelDebugDocument.FromSource("pipeline", "/mapped/Two.cs", "two");
        var span = new SourceSpan(1, 1, first.Id);
        var steps =
            new[]
            {
                Step(LoweredPipelineStepKind.Projection, "int", Current(span), first, "item"),
                Step(LoweredPipelineStepKind.Filter, "bool", new BinaryExpression(Current(span), ">", Literal(0, span), span), second, "item")
            };

        Assert.Throws<ArgumentException>(() => LoweredPipelineDebugComposer.Compose(
            new LoweredPipelineComposition("conflict", steps, SandboxType.I32)));
    }

    private static LoweredPipelineStep Step(
        LoweredPipelineStepKind kind,
        string outputType,
        Expression value,
        KernelDebugDocument document,
        string sourceName)
        => new(
            kind,
            "int",
            outputType,
            [new Parameter("$dotboxd.current", SandboxType.I32)],
            [],
            value,
            [],
            [])
        {
            DebugInfo = new LoweredPipelineDebugInfo([document], sourceName)
        };

    private static VariableExpression Current(SourceSpan span) => new("$dotboxd.current", span);

    private static LiteralExpression Literal(int value, SourceSpan span)
        => new(SandboxValue.FromInt32(value), span);
}
