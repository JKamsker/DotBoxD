using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Abstractions;

/// <summary>Client-only source metadata attached to one mergeable IR fragment.</summary>
public sealed class LoweredPipelineDebugInfo
{
    public LoweredPipelineDebugInfo(
        IReadOnlyList<KernelDebugDocument> documents,
        string? inputSourceName = null)
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
    }

    public IReadOnlyList<KernelDebugDocument> Documents { get; }

    public string? InputSourceName { get; }
}

