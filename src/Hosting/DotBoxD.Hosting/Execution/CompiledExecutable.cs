using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Hosting.Execution;

internal readonly record struct CompiledExecutable(
    CompiledArtifact Artifact,
    string MaterializationStatus,
    bool SupportsReturnValidationProof = false);
