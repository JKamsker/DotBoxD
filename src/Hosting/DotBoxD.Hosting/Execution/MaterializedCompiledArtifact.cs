using System.Runtime.Loader;
using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Hosting.Execution;

internal sealed class MaterializedCompiledArtifact(
    CompiledArtifact artifact,
    AssemblyLoadContext? loadContext,
    bool supportsReturnValidationProof = false) : IDisposable
{
    private int _disposed;

    public CompiledArtifact Artifact { get; } = artifact;

    internal bool SupportsReturnValidationProof { get; } = supportsReturnValidationProof;

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            loadContext?.Unload();
        }
    }
}
