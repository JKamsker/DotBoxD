namespace SafeIR.Hosting;

using System.Runtime.Loader;
using SafeIR.Compiler;

internal sealed class MaterializedCompiledArtifact(CompiledArtifact artifact, AssemblyLoadContext? loadContext) : IDisposable
{
    public CompiledArtifact Artifact { get; } = artifact;

    public void Dispose() => loadContext?.Unload();
}
