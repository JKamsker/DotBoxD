using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Debugging;

public sealed class StructuralNodeIdTests
{
    [Fact]
    public void Node_ids_survive_json_round_trip()
    {
        var module = Module(new SourceSpan(4, 2));

        var imported = JsonImporter.Import(JsonExporter.Export(module));

        Assert.Equal(Snapshot(module), Snapshot(imported));
    }

    [Fact]
    public void Node_ids_ignore_source_locations_and_module_identity()
    {
        var first = Module(new SourceSpan(1, 1));
        var second = Module(new SourceSpan(900, 42)) with
        {
            Id = "renamed-module",
            Version = new SemVersion(9, 8, 7),
            Metadata = new Dictionary<string, string> { ["build"] = "different" }
        };

        Assert.Equal(Snapshot(first), Snapshot(second));
    }

    [Fact]
    public void Every_function_statement_and_expression_has_a_unique_versioned_id()
    {
        var module = Module(new SourceSpan(1, 1));

        var map = SandboxNodeMap.Create(module);

        Assert.Equal(13, map.Nodes.Count);
        Assert.Equal(map.Nodes.Count, map.Nodes.Select(node => node.Id).Distinct().Count());
        Assert.All(map.Nodes, node => Assert.Equal(SandboxNodeId.CurrentVersion, node.Id.Version));
        Assert.All(map.Nodes, node => Assert.StartsWith("v1:", node.Id.Value, StringComparison.Ordinal));
    }

    [Fact]
    public void Public_lookup_returns_the_descriptor_for_original_model_nodes()
    {
        var module = Module(new SourceSpan(1, 1));
        var function = Assert.Single(module.Functions);
        var statement = Assert.IsType<AssignmentStatement>(function.Body[0]);
        var expression = statement.Value;
        var map = SandboxNodeMap.Create(module);

        Assert.Equal(SandboxNodeKind.Function, map.GetDescriptor(function).Kind);
        Assert.Equal(SandboxNodeKind.Statement, map.GetDescriptor(statement).Kind);
        Assert.Equal(SandboxNodeKind.Expression, map.GetDescriptor(expression).Kind);
        Assert.Equal("main", map.GetDescriptor(expression).FunctionId);
    }

    private static IReadOnlyList<string> Snapshot(SandboxModule module)
        => SandboxNodeMap.Create(module).Nodes
            .OrderBy(node => node.FunctionId, StringComparer.Ordinal)
            .ThenBy(node => node.StructuralPath, StringComparer.Ordinal)
            .Select(node => $"{node.FunctionId}|{node.StructuralPath}|{node.Id.Value}")
            .ToArray();

    private static SandboxModule Module(SourceSpan span)
    {
        var body = new Statement[]
        {
            new AssignmentStatement("x", Literal(1), span),
            new WhileStatement(
                new BinaryExpression(Variable(), "<", Literal(2), span),
                [new AssignmentStatement("x", new BinaryExpression(Variable(), "+", Literal(1), span), span)],
                span),
            new ReturnStatement(Variable(), span)
        };

        return new SandboxModule(
            "node-map",
            new SemVersion(1, 0, 0),
            new SemVersion(1, 0, 0),
            [],
            [new SandboxFunction("main", true, [], SandboxType.I32, body)],
            new Dictionary<string, string>());

        LiteralExpression Literal(int value) => new(SandboxValue.FromInt32(value), span);
        VariableExpression Variable() => new("x", span);
    }
}
