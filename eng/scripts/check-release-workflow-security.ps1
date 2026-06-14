#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Security gate for the tag-driven release workflow (.github/workflows/release.yml).

.DESCRIPTION
    Asserts that the package-producing / publishing pipeline keeps its security posture:
      - every action is pinned to a full 40-char commit SHA (no floating tags);
      - the privileged attestation job (OIDC + attestation write) is isolated, depends on
        pack, and only downloads + attests artifacts (no source checkout / build / test);
      - the pack job does NOT carry OIDC/attestation write permissions;
      - publishing to NuGet.org is gated to the canonical repo on a real tag;
      - provenance attestation covers both .nupkg and .snupkg with a pinned action;
      - the line-length guard is not abused to run local dotnet tools.

    Lives in eng/scripts/ (repo root is two levels up).
#>

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowPath = Join-Path $root ".github/workflows/release.yml"
$lineGuardPath = Join-Path $root "eng/scripts/check-csharp-file-lines.ps1"

if (-not (Test-Path -LiteralPath $workflowPath)) {
    throw "Release workflow file does not exist: $workflowPath"
}

if (-not (Test-Path -LiteralPath $lineGuardPath)) {
    throw "Line guard script does not exist: $lineGuardPath"
}

$workflow = Get-Content -Raw -LiteralPath $workflowPath
$lineGuard = Get-Content -Raw -LiteralPath $lineGuardPath

function Get-WorkflowJobBlock([string] $jobId) {
    $escaped = [regex]::Escape($jobId)
    $match = [regex]::Match($workflow, "(?ms)^  ${escaped}:\s*\r?\n.*?(?=^  [A-Za-z0-9_-]+:\s*\r?\n|\z)")
    if (-not $match.Success) {
        throw "Release workflow job '$jobId' does not exist."
    }

    return $match.Value
}

$packJob = Get-WorkflowJobBlock "pack"
$attestJob = Get-WorkflowJobBlock "attest"
$publishJob = Get-WorkflowJobBlock "publish"

# 1. Every action reference must be pinned to a full commit SHA.
$usesMatches = [regex]::Matches($workflow, "(?m)^\s*uses:\s*(?<action>[^@\s]+)@(?<ref>[^\s#]+)")
foreach ($match in $usesMatches) {
    $action = $match.Groups["action"].Value
    $ref = $match.Groups["ref"].Value
    # Local reusable workflow references (./.github/...) have no @ref; the regex won't match
    # them. Any external action that does match must be SHA-pinned.
    if ($action.StartsWith("./", [StringComparison]::Ordinal)) {
        continue
    }
    if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
        throw "Workflow action '$action@$ref' must be pinned to a full 40-character commit SHA."
    }
}

# 2. The pack job must not receive OIDC or attestation write permissions, nor attest itself.
if ($packJob -match "(?m)^\s+(id-token|attestations):\s+write\s*$") {
    throw "Pack job must not receive OIDC or attestation write permissions."
}

if ($packJob -match "actions/attest-build-provenance@") {
    throw "Pack job must not perform release attestation."
}

# 3. The attestation job must be properly scoped and isolated.
if ($attestJob -notmatch "(?m)^\s{4}needs:\s+pack\s*$") {
    throw "Attestation job must depend on successful pack completion (needs: pack)."
}

if ($attestJob -notmatch "(?ms)^\s{4}permissions:\s*\r?\n(?:\s{6}[a-z-]+:\s+\S+\s*\r?\n)*\s{6}id-token:\s+write\s*\r?\n") {
    throw "Attestation job must declare id-token: write."
}

if ($attestJob -notmatch "(?m)^\s{6}attestations:\s+write\s*$") {
    throw "Attestation job must declare attestations: write."
}

# The attestation job must only download artifacts and attest them: no source checkout,
# no SDK setup, no arbitrary run steps that could tamper with the artifacts pre-attestation.
if ($attestJob -match "(?im)^\s+uses:\s+actions/(checkout|setup-dotnet)@") {
    throw "Attestation job must not check out source or set up the SDK; it must only download and attest artifacts."
}

if ($attestJob -match "(?im)^\s+run:\s*") {
    throw "Attestation job must not contain arbitrary run steps; it must only download and attest artifacts."
}

if ($attestJob -notmatch "actions/attest-build-provenance@[0-9a-fA-F]{40}") {
    throw "Release workflow must attest packages with a SHA-pinned attest-build-provenance action."
}

if ($attestJob -notmatch "artifacts/packages/\*\.nupkg") {
    throw "Package attestation must cover every .nupkg in artifacts/packages."
}

if ($attestJob -notmatch "artifacts/packages/\*\.snupkg") {
    throw "Package attestation must cover every .snupkg in artifacts/packages."
}

# 4. Publishing must be gated to the canonical repo on a real tag.
if ($publishJob -notmatch "github\.repository\s*==\s*'JKamsker/DotBoxD'") {
    throw "Publish job must be gated to the canonical repository (github.repository == 'JKamsker/DotBoxD')."
}

if ($publishJob -notmatch "startsWith\(github\.ref,\s*'refs/tags/'\)") {
    throw "Publish job must be gated to tag refs (startsWith(github.ref, 'refs/tags/'))."
}

# 5. The reused CI workflow must gate the release (verify job uses ci.yml).
if ($workflow -notmatch "(?m)^\s+uses:\s+\./\.github/workflows/ci\.yml\s*$") {
    throw "Release workflow must reuse ci.yml as a verification gate (uses: ./.github/workflows/ci.yml)."
}

# 6. The line guard must not install, restore, or execute dotnet local tools.
if ($lineGuard -match "(?im)^\s*&?\s*dotnet\s+(tool\s+(install|restore|run)|new\s+tool-manifest)\b") {
    throw "Release line guard must not install, restore, or execute dotnet local tools."
}

Write-Host "Release workflow security check passed."
