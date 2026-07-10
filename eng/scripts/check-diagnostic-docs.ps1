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

$reference = Get-Content -LiteralPath $referencePath -Raw -Encoding utf8
$codes = @(
    foreach ($relativePath in $sourcePaths) {
        $source = Get-Content -LiteralPath (Join-Path $root $relativePath) -Raw -Encoding utf8
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
    if ($reference.IndexOf($anchor, [System.StringComparison]::Ordinal) -lt 0) {
        $missing += $code
    }
}

if ($missing.Count -gt 0) {
    throw "Diagnostics reference is missing stable actionable anchors for: $($missing -join ', ')"
}

$arrow = [char]0x2192
$requiredColumns = @(
    "Cause",
    "Bad example $arrow correction",
    "Alternative or fallback",
    "Suppression policy"
)
foreach ($column in $requiredColumns) {
    if ($reference.IndexOf($column, [System.StringComparison]::Ordinal) -lt 0) {
        throw "Diagnostics reference is missing the required '$column' field."
    }
}

Write-Host "Diagnostics reference covers all $($codes.Count) production DBXS/DBXK codes."
