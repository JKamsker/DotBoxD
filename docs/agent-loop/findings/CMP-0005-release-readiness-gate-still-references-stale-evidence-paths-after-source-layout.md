---
id: CMP-0005
area: completeness
status: fixed_pending_verification
priority: high
title: Release readiness gate still references stale evidence paths after source layout changes
dedup_key: release-readiness/checklist-evidence/remaining-stale-source-and-test-paths
created_at: 2026-06-12T20:59:52.9918294+00:00
created_by: completeness-producer
created_commit: 
updated_at: 2026-06-12T21:02:08.9566897+00:00
claimed_by: implementer
claimed_at: 2026-06-12T21:00:18.3696357+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T21:02:08.9566897+00:00
fixed_commit: working-tree
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# CMP-0005: Release readiness gate still references stale evidence paths after source layout changes

## Claim

`check-release-readiness.ps1` still maps many completed release-readiness and security-review checklist items to stale source/test paths after the source and test layout changed. CMP-0001 fixed the `CanonicalModuleHasher` entry, but the next completed item already fails on `src/DotBoxD.Kernels/BindingRegistryValidator.cs`, and a mechanical path check shows many other completed evidence entries also point at files that no longer exist.

## Why this matters

The README local verification flow and the release/tag gate both run `scripts/check-release-readiness.ps1`. Because the checklist marks these items complete, stale evidence paths make release readiness fail before it can report the real remaining inventory or enforce package readiness.

## Evidence

`docs/Specs/Initial/dotboxd-sandbox-spec/checklists/release-readiness.md` marks the MVP and compiled-mode checklist items complete, including `Binding registry validation implemented.`, file/security tests, verifier tests, cache tests, and fallback documentation.

`scripts/check-release-readiness.ps1` maps those completed items to paths that do not exist in this worktree, for example:

```text
src/DotBoxD.Kernels/BindingRegistryValidator.cs
src/DotBoxD.Kernels/Resources.cs
src/DotBoxD.Kernels/Audit.cs
src/DotBoxD.Kernels/SandboxError.cs
src/DotBoxD.Kernels.Compiler/ReflectionEmitSandboxCompiler.cs
src/DotBoxD.Kernels.Compiler/MethodEmitter.cs
src/DotBoxD.Kernels.Verifier/GeneratedAssemblyVerifier.cs
src/DotBoxD.Kernels.Verifier/VerificationModels.cs
tests/DotBoxD.Kernels.Tests/SafeFileSystemTests.cs
tests/DotBoxD.Kernels.Tests/BindingRegistryHardeningTests.cs
tests/DotBoxD.Kernels.Tests/VerifierAttackMatrixTests.cs
tests/DotBoxD.Kernels.Tests/DifferentialFuzzTests.cs
tests/DotBoxD.Kernels.Tests/CompiledCacheMetadataTests.cs
tests/DotBoxD.Kernels.Tests/CompiledMaterializationCacheTests.cs
```

A focused search shows the evidence was mostly moved rather than removed, for example:

```text
src/DotBoxD.Kernels/Bindings/BindingRegistryValidator.cs
src/DotBoxD.Kernels/Model/Resources.cs
src/DotBoxD.Kernels/Bindings/Audit.cs
src/DotBoxD.Kernels/Model/Diagnostics.cs
tests/DotBoxD.Kernels.Tests/Misc01/BindingRegistryHardeningTests.cs
tests/DotBoxD.Kernels.Tests/Misc02/DifferentialFuzzTests.cs
tests/DotBoxD.Kernels.Tests/Compiled/Core/CompiledCacheMetadataTests.cs
tests/DotBoxD.Kernels.Tests/Compiled/Generated/CompiledRuntimeQuotaTests.cs
```

CMP-0001 verification also recorded the first remaining failure after the canonical-hasher fix:

```text
Release checklist item 'Binding registry validation implemented.' is marked complete but evidence is missing: src/DotBoxD.Kernels/BindingRegistryValidator.cs
```

## Suggested test or benchmark

Add a Pester/script test, or extend `check-release-readiness.ps1`, so every evidence `Path` entry is validated against the current repository layout even when earlier entries fail. The acceptance test should run:

```powershell
./scripts/check-release-readiness.ps1
./scripts/check-release-readiness.ps1 -RequireComplete
```

The script should progress past all completed-item evidence checks; `-RequireComplete` may still fail only for genuinely open required checklist items.

## Suggested fix direction

Update the release-readiness evidence map to current source and test paths, or make the map resilient to the current category/subfolder layout. Prefer one pass that fixes all stale entries instead of discovering them one failure at a time.

## Scope boundaries

Do not change production behavior, checklist completion status, or security semantics as part of this fix. This finding is only about the release-readiness evidence gate proving completed checklist items against the current repository layout.

## Deduplication key

`release-readiness/checklist-evidence/remaining-stale-source-and-test-paths`

## Verification checklist

- [ ] Reproduction or test exists where practical.
- [ ] Fix addresses root cause.
- [ ] Relevant tests pass.
- [ ] Perf/allocation evidence exists where practical.
- [ ] No unrelated behavior changed.
