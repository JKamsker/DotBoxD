using System.Collections.ObjectModel;

namespace DotBoxD.Kernels.Debugging;

/// <summary>Client-only source documents, sequence points, and source-variable bindings for one kernel.</summary>
public sealed class KernelDebugInfo
{
    private readonly IReadOnlyDictionary<string, KernelDebugDocument> _documentsById;
    private readonly IReadOnlyDictionary<SandboxNodeId, KernelSequencePoint> _sequencePointsByNode;

    public KernelDebugInfo(
        IReadOnlyList<KernelDebugDocument> documents,
        IReadOnlyList<KernelSequencePoint> sequencePoints,
        IReadOnlyList<KernelDebugVariableBinding>? variableBindings = null)
    {
        Documents = Snapshot(documents, nameof(documents));
        SequencePoints = Snapshot(sequencePoints, nameof(sequencePoints));
        VariableBindings = Snapshot(variableBindings ?? [], nameof(variableBindings));
        _documentsById = IndexDocuments(Documents);
        _sequencePointsByNode = IndexSequencePoints(SequencePoints, _documentsById);
    }

    public IReadOnlyList<KernelDebugDocument> Documents { get; }

    public IReadOnlyList<KernelSequencePoint> SequencePoints { get; }

    public IReadOnlyList<KernelDebugVariableBinding> VariableBindings { get; }

    public static KernelDebugInfo Create(
        SandboxModule module,
        IReadOnlyList<KernelDebugDocument> documents,
        IReadOnlyList<KernelDebugVariableBinding>? variableBindings = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var sequencePoints = SandboxNodeMap.Create(module).Nodes
            .Where(node => node.SourceSpan?.DocumentId is not null)
            .Select(node => new KernelSequencePoint(node.Id, node.SourceSpan!))
            .ToArray();
        return new KernelDebugInfo(documents, sequencePoints, variableBindings);
    }

    public bool TryGetDocument(string documentId, out KernelDebugDocument? document)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        return _documentsById.TryGetValue(documentId, out document);
    }

    public bool TryGetSequencePoint(SandboxNodeId nodeId, out KernelSequencePoint? sequencePoint)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        return _sequencePointsByNode.TryGetValue(nodeId, out sequencePoint);
    }

    private static IReadOnlyDictionary<string, KernelDebugDocument> IndexDocuments(
        IReadOnlyList<KernelDebugDocument> documents)
    {
        var index = new Dictionary<string, KernelDebugDocument>(documents.Count, StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (!index.TryAdd(document.Id, document))
            {
                throw new ArgumentException($"Duplicate debug document ID '{document.Id}'.", nameof(documents));
            }
        }

        return new ReadOnlyDictionary<string, KernelDebugDocument>(index);
    }

    private static IReadOnlyDictionary<SandboxNodeId, KernelSequencePoint> IndexSequencePoints(
        IReadOnlyList<KernelSequencePoint> sequencePoints,
        IReadOnlyDictionary<string, KernelDebugDocument> documents)
    {
        var index = new Dictionary<SandboxNodeId, KernelSequencePoint>(sequencePoints.Count);
        foreach (var sequencePoint in sequencePoints)
        {
            var documentId = sequencePoint.Span.DocumentId;
            if (documentId is not null && !documents.ContainsKey(documentId))
            {
                throw new ArgumentException(
                    $"Sequence point references unknown debug document '{documentId}'.",
                    nameof(sequencePoints));
            }

            if (!index.TryAdd(sequencePoint.NodeId, sequencePoint))
            {
                throw new ArgumentException(
                    $"Duplicate sequence point for node '{sequencePoint.NodeId}'.",
                    nameof(sequencePoints));
            }
        }

        return new ReadOnlyDictionary<SandboxNodeId, KernelSequencePoint>(index);
    }

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> values, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException("Debug metadata collections cannot contain null entries.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }
}
