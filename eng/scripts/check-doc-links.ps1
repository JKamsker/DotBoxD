$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$siteRoot = Join-Path $root "docs-site/src/content/docs"
$documents = @(
    Get-Item -LiteralPath (Join-Path $root "README.md"), (Join-Path $root "CONTRIBUTING.md")
    Get-ChildItem -LiteralPath $siteRoot -Recurse -File -Include "*.md", "*.mdx"
)

# docs/design, docs/legacy, docs/Task, and docs/Specs are engineering records rather
# than the published documentation set. They deliberately retain links to historical
# layouts and are covered by their purpose-built manifest and stale-text checks.

function Test-SiteTarget([string] $Target) {
    $slug = $Target.TrimStart('/').TrimEnd('/')
    $candidates = @(
        (Join-Path $siteRoot ($slug + ".md")),
        (Join-Path $siteRoot ($slug + ".mdx")),
        (Join-Path $siteRoot (Join-Path $slug "index.md")),
        (Join-Path $siteRoot (Join-Path $slug "index.mdx"))
    )
    return @($candidates | Where-Object { Test-Path -LiteralPath $_ }).Count -gt 0
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($document in $documents) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $document.FullName) {
        $lineNumber++
        foreach ($match in [regex]::Matches($line, '!?(?<!\!)\[[^\]]*\]\((?<target>[^\s\)]+)')) {
            $target = [uri]::UnescapeDataString($match.Groups["target"].Value.Trim('<', '>'))
            $path = $target.Split('#', 2)[0].Split('?', 2)[0]
            if ([string]::IsNullOrWhiteSpace($path) -or
                $path -match '^(https?|mailto|tel):' -or
                $path.StartsWith('/api/') -or
                $path.Contains('{')) {
                continue
            }

            $exists = if ($path.StartsWith('/')) {
                Test-SiteTarget $path
            } else {
                Test-Path -LiteralPath (Join-Path $document.DirectoryName $path)
            }

            if (-not $exists) {
                $relative = [System.IO.Path]::GetRelativePath($root, $document.FullName)
                $failures.Add("${relative}:$lineNumber -> $target")
            }
        }
    }
}

if ($failures.Count -gt 0) {
    throw "Broken internal documentation links:`n$($failures -join "`n")"
}

Write-Host "Internal documentation links passed for $($documents.Count) documents. External URLs are intentionally excluded from blocking CI to avoid network flakiness."
