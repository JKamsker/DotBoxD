namespace DotBoxD.Hosting;

using DotBoxD.Kernels.Compiler;

internal readonly record struct CompiledExecutable(CompiledArtifact Artifact, string MaterializationStatus);
