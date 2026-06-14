---
id: API-0005
area: api_coherence
status: fixed_pending_verification
priority: medium
title: Package metadata uses one generic description and tag set for all packages
dedup_key: api/package-metadata/generic-description-tags-for-all-packages
created_at: 2026-06-12T22:08:12.1827040+00:00
created_by: continuous-completeness-producer
created_commit: 
updated_at: 2026-06-13T06:08:22.7706086+00:00
claimed_by: worker
claimed_at: 2026-06-13T06:01:43.6900294+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T06:08:22.7706086+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0005: Package metadata uses one generic description and tag set for all packages

## Claim

All product packages inherit the same generic NuGet description and tags from `Directory.Build.props`, even though the README presents distinct package roles. The package metadata check only verifies that a description exists, so package-specific metadata can remain incomplete while release validation passes.

## Why this matters

Package consumers see metadata before opening the repository README. A generic description/tag set makes `DotBoxD.Kernels`, `DotBoxD.Hosting`, `DotBoxD.Plugins.Analyzer`, `DotBoxD.Hosting.Http`, and the preview DotBoxD addon look interchangeable in package feeds, which weakens discoverability and makes safe package selection harder.

## Evidence

- `Directory.Build.props:15` sets one repository-wide `<Description>`: `DotBoxD.Kernels sandbox components for validating, interpreting, compiling, hosting, serializing, and transporting restricted .NET workloads.`
- `Directory.Build.props:16` sets one repository-wide `<PackageTags>` list for every package.
- `README.md:5` through `README.md:19` gives package-specific roles for `DotBoxD.Kernels`, `DotBoxD.Kernels.Validation`, `DotBoxD.Kernels.Runtime`, `DotBoxD.Kernels.Serialization.Json`, `DotBoxD.Hosting.Http`, `DotBoxD.Pushdown.Services`, `DotBoxD.Kernels.Interpreter`, `DotBoxD.Kernels.Compiler`, `DotBoxD.Kernels.Verifier`, `DotBoxD.Hosting`, `DotBoxD.Plugins.Analyzer`, and `DotBoxD.Plugins`.
- A targeted metadata scan over `src -g "*.csproj"` found only target-framework/build settings such as `src/DotBoxD.Plugins.Analyzer/DotBoxD.Plugins.Analyzer.csproj:15` and `src/DotBoxD.Pushdown.Services/DotBoxD.Pushdown.Services.csproj:14`; it found no project-specific `<Description>` or `<PackageTags>` overrides.
- `scripts/check-package-metadata.ps1` requires a non-empty `description`, but it does not require package-specific descriptions or tags, so the current generic metadata shape is accepted.

## Suggested acceptance test

Extend `check-package-metadata.ps1` or add a package metadata test with an expected metadata inventory. It should fail if every package has the same description/tags and should assert that each package's description identifies its package-specific role, with explicit preview wording for `DotBoxD.Pushdown.Services`.

## Suggested fix direction

Add per-package descriptions/tags in each product `.csproj` or centralize an explicit package metadata map consumed by the checker. Reuse the README package-role text as the minimum source of truth.

## Scope boundaries

Do not change package IDs, dependencies, or public API. This finding only covers NuGet metadata completeness and release validation for that metadata.

## Deduplication key

`api/package-metadata/generic-description-tags-for-all-packages`
