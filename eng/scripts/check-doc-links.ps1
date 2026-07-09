$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$siteRoot = Join-Path $root "docs-site/src/content/docs"
$documents = @(
    Get-Item -LiteralPath (Join-Path $root "README.md"), (Join-Path $root "CONTRIBUTING.md")
    Get-ChildItem -LiteralPath $siteRoot -Recurse -File -Include "*.md", "*.mdx"
)
$anchorCache = @{}

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

function Get-DocumentAnchorSet([string] $Path) {
    if ($anchorCache.ContainsKey($Path)) { return ,$anchorCache[$Path] }
    $anchors = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $slugCounts = @{}
    $inFence = $false
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line.TrimStart() -match '^(`{3,}|~{3,})') {
            $inFence = -not $inFence
            continue
        }
        if ($inFence) { continue }

        foreach ($match in [regex]::Matches($line, '(?:id|name)=["''](?<id>[^"'']+)["'']')) {
            [void] $anchors.Add($match.Groups['id'].Value)
        }
        if ($line -notmatch '^\s{0,3}#{1,6}\s+(?<heading>.+?)\s*#*\s*$') { continue }

        $slug = $matches['heading'].ToLowerInvariant()
        $slug = [regex]::Replace($slug, '<[^>]+>|[`*_~]', '')
        $slug = [regex]::Replace($slug, '[^\p{L}\p{Nd}\s-]', '')
        $slug = [regex]::Replace($slug.Trim(), '\s', '-')
        if ([string]::IsNullOrWhiteSpace($slug)) { continue }
        $count = if ($slugCounts.ContainsKey($slug)) { [int] $slugCounts[$slug] + 1 } else { 0 }
        $slugCounts[$slug] = $count
        [void] $anchors.Add($(if ($count -eq 0) { $slug } else { "$slug-$count" }))
    }
    $anchorCache[$Path] = $anchors
    return ,$anchors
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($document in $documents) {
    $lineNumber = 0
    $inFence = $false
    foreach ($line in Get-Content -LiteralPath $document.FullName) {
        $lineNumber++
        if ($line.TrimStart() -match '^(`{3,}|~{3,})') {
            $inFence = -not $inFence
            continue
        }
        if ($inFence) { continue }

        foreach ($match in [regex]::Matches($line, '!?(?<!\!)\[[^\]]*\]\((?<target>[^\s\)]+)')) {
            $target = [uri]::UnescapeDataString($match.Groups["target"].Value.Trim('<', '>'))
            $targetParts = $target.Split('#', 2)
            $path = $targetParts[0].Split('?', 2)[0]
            $fragment = if ($targetParts.Count -eq 2) { $targetParts[1] } else { '' }
            if ($path -match '^(https?|mailto|tel):' -or
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
