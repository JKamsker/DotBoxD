using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Debugging;

public sealed record KernelSequencePoint
{
    public KernelSequencePoint(SandboxNodeId nodeId, SourceSpan span)
    {
        NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
        Span = span ?? throw new ArgumentNullException(nameof(span));
    }

    public SandboxNodeId NodeId { get; }

    public SourceSpan Span { get; }
}

