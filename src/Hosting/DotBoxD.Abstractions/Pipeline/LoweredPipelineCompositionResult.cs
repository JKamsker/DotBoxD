using DotBoxD.Kernels;
using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Abstractions;

/// <summary>A composed sandbox module and its optional client-only source mapping.</summary>
public sealed record LoweredPipelineCompositionResult
{
    public LoweredPipelineCompositionResult(SandboxModule module, KernelDebugInfo? debugInfo)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        DebugInfo = debugInfo;
    }

    public SandboxModule Module { get; }

    public KernelDebugInfo? DebugInfo { get; }
}
