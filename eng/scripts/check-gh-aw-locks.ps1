[CmdletBinding()]
param(
    [string] $WorkflowDirectory = (Join-Path $PSScriptRoot '../../.github/workflows')
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI ('gh') is required to validate agentic workflow locks."
}

$workflowDirectoryPath = (Resolve-Path $WorkflowDirectory).Path
$sourceFiles = @(
    Get-ChildItem -LiteralPath $workflowDirectoryPath -Filter '*.md' -File |
        Where-Object { Test-Path -LiteralPath ($_.FullName -replace '\.md$', '.lock.yml') }
)

if ($sourceFiles.Count -eq 0) {
    throw "No agentic workflow source/lock pairs were found in '$workflowDirectoryPath'."
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($sourceFile in $sourceFiles) {
    $lockPath = $sourceFile.FullName -replace '\.md$', '.lock.yml'
    $metadataLine = [System.IO.File]::ReadLines($lockPath) | Select-Object -First 1
    $metadataPrefix = '# gh-aw-metadata: '

    if (-not $metadataLine.StartsWith($metadataPrefix, [System.StringComparison]::Ordinal)) {
        $failures.Add("$($sourceFile.Name): lock file has no gh-aw metadata header")
        continue
    }

    try {
        $metadata = $metadataLine.Substring($metadataPrefix.Length) | ConvertFrom-Json
    }
    catch {
        $failures.Add("$($sourceFile.Name): lock metadata is not valid JSON")
        continue
    }

    $actualHash = (& gh aw hash-frontmatter $sourceFile.FullName | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "gh aw could not hash '$($sourceFile.FullName)' (exit $LASTEXITCODE)."
    }

    if ($metadata.frontmatter_hash -ne $actualHash) {
        $failures.Add(
            "$($sourceFile.Name): stored hash '$($metadata.frontmatter_hash)' does not match '$actualHash'"
        )
        continue
    }

    Write-Host "Valid gh-aw lock: $($sourceFile.Name)"
}

if ($failures.Count -gt 0) {
    $details = $failures -join [Environment]::NewLine
    throw "Agentic workflow locks are stale. Run the pinned 'gh aw compile' command.$([Environment]::NewLine)$details"
}

Write-Host "Validated $($sourceFiles.Count) agentic workflow lock(s)."
