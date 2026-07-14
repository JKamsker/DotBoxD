using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Debugging;

public sealed class KernelDebugInfoTests
{
    [Fact]
    public void Source_span_carries_additive_document_end_and_sequence_point_data()
    {
        var span = new SourceSpan(
            10,
            4,
            "plugin",
            EndLine: 12,
            EndColumn: 8,
            SourceSequencePointKind.Hidden);

        Assert.Equal("plugin", span.DocumentId);
        Assert.Equal(12, span.EndLine);
        Assert.Equal(8, span.EndColumn);
        Assert.Equal(SourceSequencePointKind.Hidden, span.SequencePointKind);
        Assert.Throws<ArgumentOutOfRangeException>(() => span with { EndLine = -1 });
        Assert.Throws<ArgumentException>(() => span with { DocumentId = " " });
        Assert.Throws<ArgumentOutOfRangeException>(() => span with { SequencePointKind = (SourceSequencePointKind)99 });
    }

    [Fact]
    public void Documents_normalize_paths_and_verify_exact_source_checksums()
    {
        var document = KernelDebugDocument.FromSource("plugin", @"C:\src\Plugin.cs", "class Plugin { }\n");
        var uncDocument = KernelDebugDocument.FromSource(
            "unc-plugin",
            @"\\server\share\Plugin.cs",
            "class Plugin { }\n");

        Assert.Equal("C:/src/Plugin.cs", document.Path);
        Assert.Equal("//server/share/Plugin.cs", uncDocument.Path);
        Assert.Equal(64, document.Sha256Checksum.Length);
        Assert.True(document.MatchesSource("class Plugin { }\n"));
        Assert.False(document.MatchesSource("class Plugin { }\r\n"));
    }

    [Fact]
    public void Public_helper_maps_handwritten_ir_nodes_and_variable_bindings()
    {
        var document = KernelDebugDocument.FromSource("plugin", "/mapped/Plugin.cs", "return 7;");
        var module = Module(new SourceSpan(4, 9, document.Id, 4, 17));
        var binding = new KernelDebugVariableBinding("main", "value", "score");

        var debugInfo = KernelDebugInfo.Create(module, [document], [binding]);

        Assert.Equal(2, debugInfo.SequencePoints.Count);
        Assert.All(debugInfo.SequencePoints, point => Assert.Equal(document.Id, point.Span.DocumentId));
        Assert.True(debugInfo.TryGetDocument(document.Id, out var mappedDocument));
        Assert.Same(document, mappedDocument);
        Assert.True(debugInfo.TryGetSequencePoint(debugInfo.SequencePoints[0].NodeId, out var point));
        Assert.NotNull(point);
        Assert.Equal(binding, Assert.Single(debugInfo.VariableBindings));
    }

    [Fact]
    public void Public_sequence_helper_maps_ordered_authored_locations_across_ir_nodes()
    {
        var first = new SourceSpan(10, 3, "plugin", 10, 12);
        var second = new SourceSpan(11, 7, "plugin", 11, 18);
        var mapped = KernelDebugModuleMapper.ApplyFunctionSequenceSpans(
            Module(new SourceSpan(1, 1)),
            new Dictionary<string, IReadOnlyList<SourceSpan>> { ["main"] = [first, second] });

        var nodes = SandboxNodeMap.Create(mapped).Nodes
            .Where(node => node.FunctionId == "main" && node.SourceSpan is not null)
            .ToArray();

        Assert.Equal(first, nodes[0].SourceSpan);
        Assert.Equal(second, nodes[^1].SourceSpan);
    }

    [Fact]
    public void Source_debug_metadata_does_not_change_canonical_module_hash()
    {
        var plain = Module(new SourceSpan(1, 1));
        var mapped = Module(new SourceSpan(
            100,
            50,
            "plugin",
            EndLine: 101,
            EndColumn: 2,
            SourceSequencePointKind.Hidden));

        Assert.Equal(CanonicalModuleHasher.Hash(plain), CanonicalModuleHasher.Hash(mapped));
        Assert.Equal(CanonicalModuleHasher.Serialize(plain), CanonicalModuleHasher.Serialize(mapped));
    }

    [Fact]
    public void Debug_info_rejects_unknown_documents_and_duplicate_node_mappings()
    {
        var node = new SandboxNodeId("v1:" + new string('0', 64));
        var point = new KernelSequencePoint(node, new SourceSpan(1, 1, "missing"));

        Assert.Throws<ArgumentException>(() => new KernelDebugInfo([], [point]));

        var document = KernelDebugDocument.FromSource("missing", "Plugin.cs", "source");
        Assert.Throws<ArgumentException>(() => new KernelDebugInfo([document], [point, point]));
    }

    private static SandboxModule Module(SourceSpan span)
        => new(
            "debug-info",
            new SemVersion(1, 0, 0),
            new SemVersion(1, 0, 0),
            [],
            [new SandboxFunction(
                "main",
                true,
                [],
                SandboxType.I32,
                [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(7), span), span)])],
            new Dictionary<string, string>());
}
