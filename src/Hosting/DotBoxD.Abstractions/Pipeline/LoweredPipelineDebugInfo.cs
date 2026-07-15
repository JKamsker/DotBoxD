using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Abstractions;

/// <summary>Client-only source metadata attached to one mergeable IR fragment.</summary>
public sealed class LoweredPipelineDebugInfo
{
    public LoweredPipelineDebugInfo(
        IReadOnlyList<KernelDebugDocument> documents,
        string? inputSourceName = null,
        IReadOnlyList<SourceSpan>? sequenceSpans = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var copy = documents.ToArray();
        if (copy.Any(document => document is null))
        {
            throw new ArgumentException("Debug document collections cannot contain null entries.", nameof(documents));
        }

        if (inputSourceName is not null && string.IsNullOrWhiteSpace(inputSourceName))
        {
            throw new ArgumentException("Input source names cannot be blank.", nameof(inputSourceName));
        }

        Documents = Array.AsReadOnly(copy);
        InputSourceName = inputSourceName;
        var spanCopy = sequenceSpans?.ToArray() ?? [];
        if (spanCopy.Any(span => span is null))
        {
            throw new ArgumentException("Sequence span collections cannot contain null entries.", nameof(sequenceSpans));
        }

        SequenceSpans = Array.AsReadOnly(spanCopy);
    }

    public IReadOnlyList<KernelDebugDocument> Documents { get; }

    public string? InputSourceName { get; }

    /// <summary>Ordered authored locations for the fragment's lowered expression tree.</summary>
    public IReadOnlyList<SourceSpan> SequenceSpans { get; }
}
