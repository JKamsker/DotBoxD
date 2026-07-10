$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$siteRoot = Join-Path $root "docs-site/src/content/docs"
$documents = @(
    Get-Item -LiteralPath (Join-Path $root "README.md"), (Join-Path $root "CONTRIBUTING.md")
    Get-ChildItem -LiteralPath $siteRoot -Recurse -File -Include "*.md", "*.mdx"
)
$anchorExtractor = Join-Path $root "docs-site/scripts/extract-document-anchors.mjs"
$sluggerModule = Join-Path $root "docs-site/node_modules/github-slugger"
if (-not (Test-Path -LiteralPath $sluggerModule -PathType Container)) {
    throw "Documentation anchor validation requires the docs-site dependencies. Run 'npm ci' in docs-site first."
}
$documentPaths = @($documents.FullName) | ConvertTo-Json -Compress
$anchorJson = $documentPaths | & node $anchorExtractor
if ($LASTEXITCODE -ne 0) { throw "Documentation anchor extraction failed with exit code $LASTEXITCODE." }
$extractedAnchors = $anchorJson | ConvertFrom-Json -AsHashtable
$anchorCache = @{}
foreach ($entry in $extractedAnchors.GetEnumerator()) {
    $anchors = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($anchor in $entry.Value) { [void] $anchors.Add($anchor) }
    $anchorCache[[System.IO.Path]::GetFullPath($entry.Key)] = $anchors
}

# docs/design, docs/legacy, docs/Task, and docs/Specs are engineering records rather
# than the published documentation set. They deliberately retain links to historical
# layouts and are covered by their purpose-built manifest and stale-text checks.

function Resolve-SiteTarget([string] $Target) {
    $slug = $Target.TrimStart('/').TrimEnd('/')
    $candidates = @(
        (Join-Path $siteRoot ($slug + ".md")),
        (Join-Path $siteRoot ($slug + ".mdx")),
        (Join-Path $siteRoot (Join-Path $slug "index.md")),
        (Join-Path $siteRoot (Join-Path $slug "index.mdx"))
    )
    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

function Test-FencedCodeLine(
    [string] $Line,
    [ref] $FenceCharacter,
    [ref] $FenceLength
) {
    $trimmed = $Line.TrimStart()
    if ($null -ne $FenceCharacter.Value) {
        $closingPattern = '^' + [regex]::Escape([string] $FenceCharacter.Value) + '{' + $FenceLength.Value + ',}\s*$'
        if ($trimmed -match $closingPattern) {
            $FenceCharacter.Value = $null
            $FenceLength.Value = 0
        }
        return $true
    }
    if ($trimmed -match '^(?<fence>`{3,}|~{3,})') {
        $FenceCharacter.Value = $matches['fence'][0]
        $FenceLength.Value = $matches['fence'].Length
        return $true
    }
    return $false
}

function Get-DocumentAnchorSet([string] $Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $anchorCache.ContainsKey($fullPath)) { throw "No anchors were extracted for '$fullPath'." }
    return ,$anchorCache[$fullPath]
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($document in $documents) {
    $lineNumber = 0
    $fenceCharacter = $null
    $fenceLength = 0
    foreach ($line in Get-Content -LiteralPath $document.FullName) {
        $lineNumber++
        if (Test-FencedCodeLine -Line $line -FenceCharacter ([ref] $fenceCharacter) -FenceLength ([ref] $fenceLength)) {
            continue
        }

        foreach ($match in [regex]::Matches($line, '!?\[[^\]]*\]\((?<target>[^\s\)]+)')) {
            $target = [uri]::UnescapeDataString($match.Groups["target"].Value.Trim('<', '>'))
            $targetParts = $target.Split('#', 2)
            $path = $targetParts[0].Split('?', 2)[0]
            $fragment = if ($targetParts.Count -eq 2) { $targetParts[1] } else { '' }
            if ($path -match '^[a-z][a-z0-9+.-]*:' -or
                $path.StartsWith('//') -or
                $path.StartsWith('/api/') -or
                $path.Contains('{')) {
                continue
            }

            $targetFile = if ([string]::IsNullOrWhiteSpace($path)) {
                $document.FullName
            } elseif ($path.StartsWith('/')) {
                Resolve-SiteTarget $path
            } else {
                $candidate = Join-Path $document.DirectoryName $path
                if (Test-Path -LiteralPath $candidate) { $candidate } else { $null }
            }

            if ($null -eq $targetFile) {
                $relative = [System.IO.Path]::GetRelativePath($root, $document.FullName)
                $failures.Add("${relative}:$lineNumber -> $target")
                continue
            }
            if (-not [string]::IsNullOrWhiteSpace($fragment) -and
                (Test-Path -LiteralPath $targetFile -PathType Leaf) -and
                [System.IO.Path]::GetExtension($targetFile) -in @('.md', '.mdx') -and
                -not (Get-DocumentAnchorSet $targetFile).Contains($fragment)) {
                $relative = [System.IO.Path]::GetRelativePath($root, $document.FullName)
                $failures.Add("${relative}:$lineNumber -> $target (missing fragment)")
            }
        }
    }
}

if ($failures.Count -gt 0) {
    throw "Broken internal documentation links:`n$($failures -join "`n")"
}

Write-Host "Internal documentation links passed for $($documents.Count) documents. External URLs are intentionally excluded from blocking CI to avoid network flakiness."
