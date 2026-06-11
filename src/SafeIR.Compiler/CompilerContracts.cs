namespace SafeIR.Compiler;

using SafeIR;
using SafeIR.Verifier;

public delegate SandboxValue SandboxCompiledEntrypoint(SandboxContext context, SandboxValue input);

public sealed record CompileOptions(string Entrypoint, bool Optimize = false);

public sealed record CompiledArtifact(
    byte[] AssemblyBytes,
    string AssemblyHash,
    ArtifactManifest Manifest,
    VerificationResult Verification,
    SandboxCompiledEntrypoint Entrypoint);

public interface ISandboxCompiler
{
    ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken);
}
