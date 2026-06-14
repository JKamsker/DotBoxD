---
id: COR-0064
area: correctness
status: verified
priority: high
title: Build/test job grants OIDC attestation permissions to every CI trigger
dedup_key: security/release-workflow/oidc-attestation/job-wide-pr-trigger
created_at: 2026-06-13T06:39:11.8155014+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-13T07:24:46.1072051+00:00
claimed_by: implementer
claimed_at: 2026-06-13T07:19:38.7239816+00:00
claim_branch: 
fixed_by: implementer
fixed_at: 2026-06-13T07:21:53.4312678+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-13T07:24:46.1072051+00:00
verified_commit: 72a3ab3
duplicate_of: 
---

# COR-0064: Build/test job grants OIDC attestation permissions to every CI trigger

## Claim

The `build-test-pack` CI job grants `id-token: write` and `attestations: write` at job scope even though the job runs for `pull_request` and executes restore, build, test, repository scripts, package smoke tests, and other untrusted build steps before the release-only attestation step. The attestation step is conditional, but GitHub permissions are available to the whole job and matrix, not only that step.

## Evidence

- `.github/workflows/ci.yml` triggers the workflow on `pull_request` as well as pushes and release tags.
- `.github/workflows/ci.yml` grants `contents: read`, `id-token: write`, and `attestations: write` on the `build-test-pack` job.
- The same job runs dependency restore, build, tests, security boundary tests, repo validation scripts, package creation, package smoke tests, and package inspection before the attestation step.
- The `Attest package artifacts` step is limited to Ubuntu release/tag runs, but the credential-bearing job permissions are not isolated to that release-only step.
- `scripts/check-release-workflow-security.ps1` currently requires the broad `id-token: write` and `attestations: write` permissions on the whole `build-test-pack` job instead of requiring them only on a dedicated release attestation job.

This is not a duplicate of COR-0046, which covered missing explicit permissions and ambient defaults. This finding covers the follow-up over-scope created by putting OIDC and attestation write permissions on the all-purpose build/test job.

## Impact

Any compromised dependency restore, build, test, smoke-test, or repository script running in the job can attempt to use the job's OIDC and attestation authority in contexts unrelated to release provenance, including PR-triggered runs. Cloud-side trust policies may still validate token claims, but the workflow should not expose OIDC or attestation write authority to untrusted build/test code when only the release attestation phase needs it.

## Suggested fix

Split release attestation into a dedicated release/tag-only job that depends on successful build/test/package jobs. Keep normal build/test jobs at `contents: read` only. Grant `id-token: write` and `attestations: write` only to the dedicated attestation job, and update `scripts/check-release-workflow-security.ps1` to reject those write permissions on jobs that run for pull requests or contain general build/test/script steps.
