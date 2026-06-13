---
id: API-0008
area: api_coherence
status: fixed_pending_verification
priority: medium
title: Plugin analyzer package lacks public diagnostic reference
dedup_key: api/plugin-analyzer/diagnostic-reference/missing-public-docs
created_at: 2026-06-12T22:15:35.0710165+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-13T07:49:32.7400213+00:00
claimed_by: fixer
claimed_at: 2026-06-13T07:49:32.6031394+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-13T07:49:32.7400213+00:00
fixed_commit: b14fd0a
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0008: Plugin analyzer package lacks public diagnostic reference

## Claim

`SafeIR.PluginAnalyzer` ships as a public SDK package with stable diagnostic IDs, but the public docs/examples do not provide a diagnostic reference or remediation guide for those IDs.

## Evidence

- `src/SafeIR.PluginAnalyzer/Analysis/SafeIrPluginAnalyzer.cs:12` defines `SGP001` for forbidden host APIs in plugin kernels.
- `src/SafeIR.PluginAnalyzer/Analysis/SafeIrPluginAnalyzer.cs:22` defines `SGP020` for unsupported live setting types.
- `src/SafeIR.PluginAnalyzer/Analysis/PluginAnalyzerDiagnostics.cs:7` defines `SGP100` for unsupported plugin kernel generation shapes.
- `src/SafeIR.PluginAnalyzer/AnalyzerReleases.Shipped.md:8` tracks `SGP001` and `SGP020`, and `src/SafeIR.PluginAnalyzer/AnalyzerReleases.Unshipped.md:8` tracks `SGP100`, so the package already treats the IDs as user-facing analyzer rules.
- `README.md:19` only lists `SafeIR.PluginAnalyzer` as a source generator/analyzer package, without showing diagnostic IDs, categories, supported language subset, or fixes.
- `docs/Specs/Addendum/Examples.md:6` says the analyzer provides diagnostics for forbidden File IO and unsupported live setting types, but the walkthrough does not name `SGP001`, `SGP020`, or `SGP100`, and it does not tell plugin authors how to remediate each rule.
- `examples/LocalPlugin/SafeIR.PluginLocal/SafeIR.PluginLocal.csproj:4` and `examples/Addendum/SafeIR.AddendumExamples/SafeIR.AddendumExamples.csproj:4` wire the analyzer through project references, so source-tree examples prove local wiring but do not provide package-consumer diagnostic documentation.

## Impact

Plugin authors consuming the analyzer from NuGet can receive build-breaking `SGP` diagnostics without a public reference that maps each ID to the violated rule, supported alternatives, and whether the issue is a security rule or a generation-subset limitation. That weakens package readiness because IDE/build output becomes the only documentation for authoring failures.

## Better target

Add a public analyzer diagnostics reference, linked from the README and addendum walkthrough, that lists each shipped/unshipped rule ID, category, severity, trigger, supported examples, unsupported examples, and remediation. Include the source-generator subset behind `SGP100` so authors know which kernel shapes are currently complete.

## Test/release gate idea

Add a docs/package readiness check that compares analyzer release-tracking IDs against the public diagnostic reference and fails when a new `SGP` rule ships without documentation.
