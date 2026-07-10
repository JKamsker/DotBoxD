param()

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$referencePath = Join-Path $root "docs-site/src/content/docs/reference/diagnostics.md"
$sourcePaths = @(
    "src/CodeGeneration/DotBoxD.Services.SourceGenerator/EntryPoint/DotBoxDRpcGenerator.cs",
    "src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginAnalyzer.cs",
    "src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginAnalyzerDiagnostics.cs",
    "src/Hosting/DotBoxD.Plugins/Runtime/Diagnostics/PluginDiagnosticCodes.cs"
)

$reference = Get-Content -LiteralPath $referencePath -Raw
$codes = @(
    foreach ($relativePath in $sourcePaths) {
        $source = Get-Content -LiteralPath (Join-Path $root $relativePath) -Raw
        foreach ($match in [regex]::Matches($source, '"(?<code>DBX[SK][0-9]{3})"')) {
            $match.Groups["code"].Value
        }
    }
) | Sort-Object -Unique

if ($codes.Count -eq 0) {
    throw "No production diagnostic codes were discovered. The diagnostic docs gate is misconfigured."
}

$missing = @()
foreach ($code in $codes) {
    $anchor = '<a id="' + $code.ToLowerInvariant() + '"></a>`' + $code + '`'
    if (-not $reference.Contains($anchor, [System.StringComparison]::Ordinal)) {
        $missing += $code
    }
}

if ($missing.Count -gt 0) {
    throw "Diagnostics reference is missing stable actionable anchors for: $($missing -join ', ')"
}

$requiredColumns = @(
    "Cause",
    "Bad example → correction",
    "Alternative or fallback",
    "Suppression policy"
)
foreach ($column in $requiredColumns) {
    if (-not $reference.Contains($column, [System.StringComparison]::Ordinal)) {
        throw "Diagnostics reference is missing the required '$column' field."
    }
}

Write-Host "Diagnostics reference covers all $($codes.Count) production DBXS/DBXK codes."
