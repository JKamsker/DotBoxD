param(
    [string] $WorkflowPath = ".github/workflows/codeql.yml"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
$path = if ([System.IO.Path]::IsPathRooted($WorkflowPath)) {
    $WorkflowPath
} else {
    Join-Path $root $WorkflowPath
}

if (-not (Test-Path -LiteralPath $path)) {
    throw "CodeQL workflow does not exist: $path"
}

$workflow = Get-Content -Raw -LiteralPath $path

function Assert-PinnedActions([string] $workflowText) {
    $usesMatches = [regex]::Matches($workflowText, "(?m)^\s*uses:\s*(?<action>[^@\s]+)@(?<ref>[^\s#]+)")
    foreach ($match in $usesMatches) {
        $action = $match.Groups["action"].Value
        $ref = $match.Groups["ref"].Value
        if ($action.StartsWith("./", [System.StringComparison]::Ordinal)) {
            continue
        }

        if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
            throw "CodeQL workflow action '$action@$ref' must be pinned to a full 40-character commit SHA."
        }
    }
}

function Get-WorkflowJobBlock([string] $workflowText, [string] $jobId) {
    $escaped = [regex]::Escape($jobId)
    $match = [regex]::Match($workflowText, "(?ms)^  ${escaped}:\s*\r?\n.*?(?=^  [A-Za-z0-9_-]+:\s*\r?\n|\z)")
    if (-not $match.Success) {
        throw "CodeQL workflow job '$jobId' does not exist."
    }

    return $match.Value
}

function Get-StepBlock([string] $jobBlock, [string] $stepName) {
    $escaped = [regex]::Escape($stepName)
    $match = [regex]::Match($jobBlock, "(?ms)^\s{6}- name:\s+${escaped}\s*\r?\n.*?(?=^\s{6}- name:\s+|\z)")
    if (-not $match.Success) {
        throw "CodeQL workflow step '$stepName' does not exist."
    }

    return $match.Value
}

Assert-PinnedActions $workflow

$analyzeJob = Get-WorkflowJobBlock $workflow "analyze"
if ($analyzeJob -notmatch "(?ms)^\s{4}permissions:\s*\r?\n(?:\s{6}[a-z-]+:\s+\S+\s*\r?\n)*\s{6}security-events:\s+write\s*\r?\n") {
    throw "CodeQL analyze job must declare security-events: write so SARIF uploads reach code scanning."
}

if ($analyzeJob -notmatch "(?m)^\s{6}contents:\s+read\s*$") {
    throw "CodeQL analyze job must keep contents permission to read."
}

$analysisStep = Get-StepBlock $analyzeJob "Perform CodeQL analysis"
if ($analysisStep -notmatch "github/codeql-action/analyze@[0-9a-fA-F]{40}") {
    throw "CodeQL analysis must use a SHA-pinned github/codeql-action/analyze action."
}

if ($analysisStep -notmatch "(?m)^\s{10}upload:\s+always\s*$") {
    throw "CodeQL analysis must explicitly upload SARIF to code scanning with 'upload: always'."
}

if ($analysisStep -match "(?m)^\s{10}upload:\s+never\s*$") {
    throw "CodeQL analysis must not disable code-scanning upload with 'upload: never'."
}

if ($analysisStep -notmatch "(?m)^\s{10}output:\s+codeql-results\s*$") {
    throw "CodeQL analysis must keep output: codeql-results so SARIF artifacts are retained."
}

$artifactStep = Get-StepBlock $analyzeJob "Upload CodeQL SARIF artifact"
if ($artifactStep -notmatch "actions/upload-artifact@[0-9a-fA-F]{40}") {
    throw "CodeQL SARIF artifact upload must use a SHA-pinned actions/upload-artifact action."
}

if ($artifactStep -notmatch "codeql-results/\*\*/\*\.sarif") {
    throw "CodeQL SARIF artifact upload must include codeql-results/**/*.sarif."
}

Write-Host "CodeQL workflow guard passed."
