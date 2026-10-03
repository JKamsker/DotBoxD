$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $root
try {
    $json = & dotnet list DotBoxD.slnx package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw "NuGet vulnerability query failed; releases fail closed when advisory sources are unavailable." }
    $report = ($json -join "`n") | ConvertFrom-Json
    if (-not $report.projects -or $report.version -ne 1) { throw 'Missing or unsupported NuGet audit report.' }
    if ($report.problems.Count -gt 0) { throw ($report.problems | ConvertTo-Json -Depth 10) }
    foreach ($project in $report.projects) {
        foreach ($framework in $project.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($null -ne $package -and $package.vulnerabilities.Count -gt 0) {
                    throw "Vulnerable dependency: $($package.id) $($package.resolvedVersion) in $($project.path)."
                }
            }
        }
    }
    Write-Host 'All direct and transitive NuGet dependencies passed vulnerability auditing.'
} finally { Pop-Location }
