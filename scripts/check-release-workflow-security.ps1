$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $root ".github/workflows/ci.yml"
if (-not (Test-Path -LiteralPath $workflowPath)) {
    throw "Workflow file does not exist: $workflowPath"
}

$workflow = Get-Content -Raw -LiteralPath $workflowPath
$usesMatches = [regex]::Matches($workflow, "(?m)^\s*uses:\s*(?<action>[^@\s]+)@(?<ref>[^\s#]+)")
foreach ($match in $usesMatches) {
    $action = $match.Groups["action"].Value
    $ref = $match.Groups["ref"].Value
    if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
        throw "Workflow action '$action@$ref' must be pinned to a full 40-character commit SHA."
    }
}

if ($workflow -notmatch "(?ms)build-test-pack:.*?permissions:\s*\r?\n\s+contents:\s+read\s*\r?\n\s+id-token:\s+write\s*\r?\n\s+attestations:\s+write") {
    throw "Package-producing job must declare explicit least-privilege permissions for contents read and package attestation."
}

if ($workflow -match "(?m)^\s+contents:\s+write\s*$") {
    throw "Package-producing workflow must not grant contents: write."
}

if ($workflow -notmatch "actions/attest-build-provenance@[0-9a-fA-F]{40}") {
    throw "Release package workflow must attest package artifacts with a pinned attest-build-provenance action."
}

if ($workflow -notmatch "subject-path:\s+artifacts/packages/\*\.nupkg") {
    throw "Package attestation must cover every .nupkg in artifacts/packages."
}

Write-Host "Release workflow security check passed."
