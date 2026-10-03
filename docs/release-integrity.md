# Release integrity

Both publishing workflows consume artifacts that passed the full build/test, quality and coverage
gates. The repository's required checks are the Linux/Windows aggregate Build & Test checks,
Security & quality gates, Coverage thresholds and Pack NuGet packages. Main protection was enabled and read back on 2026-09-26 using the reviewed declaration in
`eng/repository/main-protection.json`: it disallows force pushes and deletion, requires these checks
on an up-to-date branch, applies to admins, and requires review conversations to be resolved. No broad admin bypass is
needed by the normal PR workflow.

## Dependencies and licenses

`NuGetAudit=true` and `NuGetAuditMode=all` cover direct and transitive dependencies. CI treats advisory
source failures (including NU1900) as errors; local builds may warn when an advisory source is down.
`eng/scripts/check-dependency-audit.ps1` additionally checks the machine-readable vulnerable-package
report and fails on any reported vulnerability or query problem. Release availability never overrides
an unavailable advisory service. Restore remains unlocked for the reasons and revisit trigger in
[restore reproducibility](restore-reproducibility.md).

`eng/supply-chain/release_inventory.py` consumes every shipping project's resolved NuGet assets graph
and exact package archive bytes. Its policy accepts explicitly listed SPDX expressions and rejects
unknown/file/URL licenses unless an exact package/version has a documented reviewed exception.
Changing a dependency version does not inherit an exception. The inventory includes runtime,
reference, analyzer and build dependencies of the shipping solution; it is deliberately broader than
runtime dependencies alone. Development-only test dependencies are audited for vulnerabilities,
but are not represented as shipped package content.

The output is `artifacts/packages/sbom.cdx.json`, CycloneDX 1.6, with package SHA-256 hashes and the
resolved third-party dependency graph. The release artifact components identify exact `.nupkg` bytes.
A release-wide SBOM describes the shipping solution's dependency union, not a claim that every
listed dependency is loaded by every individual package.

## Binary compatibility

`DotBoxDValidatePackages=true` enables SDK package validation with a pinned baseline in
`eng/package-compatibility.props`; it checks assembly metadata and compatible TFMs at pack time.
Source API baselines remain the review-friendly counterpart. No stable release exists yet, so the
baseline is the shipped `0.1.0-ci.5510`. At 1.0, pin the last stable release; do not silently float it.
Breaking changes require intentional review, migration notes and the version policy in the public
documentation. New packages require an explicit baseline exemption until first publication.

## Trusted publishing

The `ci.yml` main publisher and `release.yml` tag publisher use the SHA-pinned official `NuGet/login`
action with NuGet profile `Weirdo`. Only the publishing jobs request `id-token: write`; pack jobs
cannot mint a publishing credential. Forks/PRs/manual workflow dispatch cannot enter these jobs.
The short-lived key is passed through an environment variable, never interpolated into shell code.

NuGet account configuration must authorize **both** policies: repository owner `JKamsker`, repository
`DotBoxD`, workflow filenames `ci.yml` and `release.yml`, package-owner profile `Weirdo`. See the
[official trusted-publishing setup](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
A passing PR validates workflow structure but cannot prove that either account-side policy accepts
a main/tag token. Observe the next legitimate main publication and next legitimate release before
removing the old Bitwarden NuGet credential or repository access secret. Do not create a release solely
to test credentials. Neither workflow retains a standing-key fallback.

## SBOM and provenance verification

The isolated attestation job downloads immutable package artifacts and produces both build provenance
and a CycloneDX SBOM attestation. It does not check out or execute repository code. GitHub releases
include the SBOM alongside packages and symbols.

```sh
gh attestation verify DotBoxD.1.0.0.nupkg --repo JKamsker/DotBoxD
gh attestation verify DotBoxD.1.0.0.nupkg --repo JKamsker/DotBoxD --predicate-type https://cyclonedx.org/bom
```

Use the downloaded package's actual filename/version. Review the attested repository/workflow and
SBOM dependency/license closure; an attestation establishes origin, not absence of vulnerabilities.
