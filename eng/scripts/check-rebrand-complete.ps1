#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Rebrand-completeness gate. Fails if any legacy ShaRPC/SafeIR brand token (or a
    legacy SGP#### diagnostic id) survives in the active source tree.

.DESCRIPTION
    The repository was rebranded from ShaRPC / SafeIR to DotBoxd. This gate keeps the
    rebrand from silently regressing. It scans the active project surface
    (src tests benchmarks samples eng schemas) for a case-SENSITIVE set of legacy
    brand spellings and prints the offending file:line so the leak is easy to find.

    Intentionally excluded (these legitimately retain history / legal text):
      - docs/legacy/**   (archived legacy material)
      - CHANGELOG.md     (records the rename itself)
      - LICENSE          (legal text)
      - any bin/ or obj/ build output

.NOTES
    Lives in eng/scripts/. Repo root is two levels up.
#>

$ErrorActionPreference = "Stop"

# Repo root: this script lives in eng/scripts/, so walk up twice.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# Case-sensitive legacy-brand pattern. \bSGP[0-9] catches legacy diagnostic ids.
$pattern = 'ShaRPC|ShaRpc|sharpc|SHARPC|SafeIR|SafeIr|safe-ir|safeir|\bSGP[0-9]'
$regex = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::None)

# Only these top-level areas make up the active project surface.
$scanRoots = @("src", "tests", "benchmarks", "samples", "eng", "schemas")

# File extensions that count as source/config we care about.
$includeExtensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($ext in @(".cs", ".csproj", ".props", ".targets", ".json", ".slnx", ".editorconfig", ".md")) {
    [void] $includeExtensions.Add($ext)
}

function Test-ExcludedPath([string] $relativePath) {
    $normalized = $relativePath.Replace("\", "/")

    # Build output is never source.
    if ($normalized -match "(^|/)(bin|obj)/") {
        return $true
    }

    # Archived legacy material keeps the old names on purpose.
    if ($normalized -like "docs/legacy/*") {
        return $true
    }

    # CHANGELOG records the rename; LICENSE is legal text.
    $fileName = [System.IO.Path]::GetFileName($normalized)
    if ($fileName -ieq "CHANGELOG.md" -or $fileName -ieq "LICENSE") {
        return $true
    }

    return $false
}

$violations = [System.Collections.Generic.List[string]]::new()

foreach ($scanRoot in $scanRoots) {
    $scanPath = Join-Path $root $scanRoot
    if (-not (Test-Path -LiteralPath $scanPath)) {
        continue
    }

    $files = Get-ChildItem -LiteralPath $scanPath -Recurse -File | Where-Object {
        $includeExtensions.Contains($_.Extension)
    }

    foreach ($file in $files) {
        $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace("\", "/")
        if (Test-ExcludedPath $relative) {
            continue
        }

        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
            $lineNumber++
            $match = $regex.Match($line)
            if ($match.Success) {
                $violations.Add("$relative`:$lineNumber matched '$($match.Value)': $($line.Trim())")
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Rebrand-completeness check FAILED. Legacy brand tokens found:" -ForegroundColor Red
    foreach ($violation in $violations) {
        Write-Error $violation -ErrorAction Continue
    }

    throw "check-rebrand-complete found $($violations.Count) legacy brand token(s). The rebrand must stay complete."
}

Write-Host "Rebrand-completeness check passed. No legacy ShaRPC/SafeIR/SGP tokens in the active source tree."
