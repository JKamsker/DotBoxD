param(
    [string] $CoverageDirectory = "artifacts/coverage",
    [double] $MinimumLineCoverage = -1,
    [double] $MinimumBranchCoverage = -1,
    [string] $SummaryPath = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
$coverageRoot = Join-Path $root $CoverageDirectory
$configPath = Join-Path $root ".config/code-enforcer/coverage.json"
if (-not (Test-Path -LiteralPath $configPath)) {
    throw ".config/code-enforcer/coverage.json is missing."
}

$config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json

function Get-RequiredDouble($Object, [string] $PropertyName, [string] $Context) {
    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string] $property.Value)) {
        throw "$Context must define '$PropertyName'."
    }
    return [double] $property.Value
}

function Get-RequiredStringArray($Object, [string] $PropertyName, [string] $Context) {
    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        throw "$Context must define '$PropertyName'."
    }
    $values = @($property.Value | ForEach-Object { [string] $_ } | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    })
    if ($values.Count -eq 0) {
        throw "$Context must define at least one '$PropertyName' entry."
    }
    return [string[]] $values
}

function New-CoverageBucket(
    [string] $Name,
    [string] $Kind,
    [string[]] $PackagePatterns,
    [double] $LineFloor,
    [double] $BranchFloor) {
    [pscustomobject] @{
        Name = $Name
        Kind = $Kind
        PackagePatterns = $PackagePatterns
        LineFloor = $LineFloor
        BranchFloor = $BranchFloor
        ValidLines = @{}
        CoveredLines = @{}
        ValidBranches = @{}
        CoveredBranches = @{}
    }
}

function Add-ConfiguredBucket($Target, [string] $Kind, [string] $ContextPrefix) {
    foreach ($area in @($Target)) {
        $name = [string] $area.name
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "Each $ContextPrefix must define 'name'."
        }
        $context = "$ContextPrefix '$name'"
        $buckets.Add((New-CoverageBucket `
            $name `
            $Kind `
            (Get-RequiredStringArray $area "packagePatterns" $context) `
            (Get-RequiredDouble $area "minimumLineCoverage" $context) `
            (Get-RequiredDouble $area "minimumBranchCoverage" $context)))
    }
}

$globalLineFloor = if ($MinimumLineCoverage -ge 0) {
    $MinimumLineCoverage
} else {
    Get-RequiredDouble $config "minimumLineCoverage" "Coverage config"
}
$globalBranchFloor = if ($MinimumBranchCoverage -ge 0) {
    $MinimumBranchCoverage
} else {
    Get-RequiredDouble $config "minimumBranchCoverage" "Coverage config"
}

$buckets = [System.Collections.Generic.List[object]]::new()
$buckets.Add((New-CoverageBucket "Global shipping assemblies" "global" @("DotBoxD*") $globalLineFloor $globalBranchFloor))
Add-ConfiguredBucket $config.areas "area" "coverage area"
Add-ConfiguredBucket $config.criticalAreas "critical" "critical coverage area"

$reports = @(Get-ChildItem -Path $coverageRoot -Recurse -Filter "*.cobertura.xml" -ErrorAction SilentlyContinue)
if ($reports.Count -eq 0) {
    throw "No Cobertura reports (*.cobertura.xml) found under $coverageRoot."
}

function Test-ShippingPackage([string] $name) {
    return $name.StartsWith("DotBoxD", [System.StringComparison]::Ordinal) -and
        -not $name.EndsWith(".Tests", [System.StringComparison]::Ordinal) -and
        -not ($name -like "*.Benchmarks")
}

function Test-ShippingSourceFile([string] $file) {
    if ([string]::IsNullOrEmpty($file)) {
        return $false
    }

    $normalized = $file.Replace('\', '/')
    return $normalized -match '(^|/)src/' -or
        $normalized -match '(^|/)(Channels|CodeGeneration|Hosting|Kernels|Meta|Pushdown|Services)/'
}

function Test-PackagePattern([string] $name, [string[]] $patterns) {
    foreach ($pattern in $patterns) {
        if ($name -like $pattern) {
            return $true
        }
    }
    return $false
}

function Get-BranchCount($line) {
    $conditionCoverage = [string] $line."condition-coverage"
    if ($conditionCoverage -match '\((?<covered>\d+)\/(?<valid>\d+)\)') {
        return [pscustomobject] @{ Covered = [int] $Matches["covered"]; Valid = [int] $Matches["valid"] }
    }
    return [pscustomobject] @{ Covered = 0; Valid = 0 }
}

function Initialize-FileBucket($bucket, [string] $file) {
    if ($bucket.ValidLines.ContainsKey($file)) {
        return
    }
    $bucket.ValidLines[$file] = [System.Collections.Generic.HashSet[int]]::new()
    $bucket.CoveredLines[$file] = [System.Collections.Generic.HashSet[int]]::new()
    $bucket.ValidBranches[$file] = @{}
    $bucket.CoveredBranches[$file] = @{}
}

function Add-LineCoverage($bucket, [string] $file, [int] $lineNumber, [bool] $covered) {
    Initialize-FileBucket $bucket $file
    [void] $bucket.ValidLines[$file].Add($lineNumber)
    if ($covered) {
        [void] $bucket.CoveredLines[$file].Add($lineNumber)
    }
}

function Add-BranchCoverage($bucket, [string] $file, [int] $lineNumber, [int] $valid, [int] $covered) {
    if ($valid -le 0) {
        return
    }

    # Cobertura does not expose stable branch identities across independent test-project reports.
    # The best observed count per source line is a conservative lower bound; summing would double
    # count repeated reports for the same branch point.
    $existingValid = if ($bucket.ValidBranches[$file].ContainsKey($lineNumber)) {
        $bucket.ValidBranches[$file][$lineNumber]
    } else {
        0
    }
    $existingCovered = if ($bucket.CoveredBranches[$file].ContainsKey($lineNumber)) {
        $bucket.CoveredBranches[$file][$lineNumber]
    } else {
        0
    }
    $bucket.ValidBranches[$file][$lineNumber] = [Math]::Max($existingValid, $valid)
    $bucket.CoveredBranches[$file][$lineNumber] = [Math]::Max($existingCovered, $covered)
}

foreach ($report in $reports) {
    [xml] $document = Get-Content -Raw -LiteralPath $report.FullName
    foreach ($package in @($document.coverage.packages.package)) {
        $packageName = [string] $package.name
        if ($null -eq $package -or -not (Test-ShippingPackage $packageName)) {
            continue
        }

        foreach ($class in @($package.classes.class)) {
            $file = ([string] $class.filename).Replace('\', '/')
            if ($null -eq $class -or -not (Test-ShippingSourceFile $file)) {
                continue
            }

            $matchingBuckets = @($buckets | Where-Object {
                Test-PackagePattern $packageName $_.PackagePatterns
            })
            foreach ($line in @($class.lines.line)) {
                if ($null -eq $line) {
                    continue
                }
                $lineNumber = [int] $line.number
                $lineCovered = [int] $line.hits -gt 0
                $branchCounts = if ([string] $line.branch -eq "True" -or [string] $line.branch -eq "true") {
                    Get-BranchCount $line
                } else {
                    [pscustomobject] @{ Covered = 0; Valid = 0 }
                }
                foreach ($bucket in $matchingBuckets) {
                    Add-LineCoverage $bucket $file $lineNumber $lineCovered
                    Add-BranchCoverage $bucket $file $lineNumber $branchCounts.Valid $branchCounts.Covered
                }
            }
        }
    }
}

function Measure-Bucket($bucket) {
    $validLines = 0
    $coveredLines = 0
    $validBranches = 0
    $coveredBranches = 0
    foreach ($file in $bucket.ValidLines.Keys) {
        $validLines += $bucket.ValidLines[$file].Count
        $coveredLines += $bucket.CoveredLines[$file].Count
        foreach ($lineNumber in $bucket.ValidBranches[$file].Keys) {
            $validBranches += $bucket.ValidBranches[$file][$lineNumber]
            $coveredBranches += $bucket.CoveredBranches[$file][$lineNumber]
        }
    }
    if ($validLines -eq 0) {
        if ($bucket.Kind -eq "critical") {
            throw "Critical coverage bucket '$($bucket.Name)' matched no shipping source lines."
        }

        Write-Warning "Coverage bucket '$($bucket.Name)' matched no shipping source lines and is not included in this report."
        return $null
    }
    [pscustomobject] @{
        Name = $bucket.Name
        Kind = $bucket.Kind
        LineRate = [math]::Round(100.0 * $coveredLines / $validLines, 2)
        CoveredLines = $coveredLines
        ValidLines = $validLines
        LineFloor = $bucket.LineFloor
        BranchRate = if ($validBranches -gt 0) { [math]::Round(100.0 * $coveredBranches / $validBranches, 2) } else { 100.0 }
        CoveredBranches = $coveredBranches
        ValidBranches = $validBranches
        BranchFloor = $bucket.BranchFloor
    }
}

$results = @($buckets | ForEach-Object { Measure-Bucket $_ } | Where-Object { $null -ne $_ })
$failures = @($results | Where-Object { $_.LineRate -lt $_.LineFloor -or $_.BranchRate -lt $_.BranchFloor })

if ([string]::IsNullOrWhiteSpace($SummaryPath)) {
    $SummaryPath = Join-Path $coverageRoot "coverage-summary.md"
} elseif (-not [System.IO.Path]::IsPathRooted($SummaryPath)) {
    $SummaryPath = Join-Path $root $SummaryPath
}

$summaryDirectory = Split-Path -Parent $SummaryPath
if (-not [string]::IsNullOrWhiteSpace($summaryDirectory)) {
    New-Item -ItemType Directory -Force -Path $summaryDirectory | Out-Null
}

$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add("# Coverage summary")
$summary.Add("")
$summary.Add("Cobertura reports: $($reports.Count)")
$summary.Add("")
$summary.Add("| Scope | Type | Line coverage | Line floor | Branch coverage | Branch floor |")
$summary.Add("| --- | --- | ---: | ---: | ---: | ---: |")
foreach ($result in $results) {
    $summary.Add((
        "| {0} | {1} | {2}% ({3}/{4}) | {5}% | {6}% ({7}/{8}) | {9}% |" -f `
            $result.Name,
            $result.Kind,
            $result.LineRate,
            $result.CoveredLines,
            $result.ValidLines,
            $result.LineFloor,
            $result.BranchRate,
            $result.CoveredBranches,
            $result.ValidBranches,
            $result.BranchFloor))
}
Set-Content -LiteralPath $SummaryPath -Value $summary -Encoding UTF8
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary -Encoding UTF8
}

foreach ($result in $results) {
    Write-Host ("{0}: line {1}% ({2}/{3}, floor {4}%), branch {5}% ({6}/{7}, floor {8}%)." -f `
        $result.Name,
        $result.LineRate,
        $result.CoveredLines,
        $result.ValidLines,
        $result.LineFloor,
        $result.BranchRate,
        $result.CoveredBranches,
        $result.ValidBranches,
        $result.BranchFloor)
}
Write-Host "Coverage summary written to $SummaryPath."

if ($failures.Count -gt 0) {
    $messages = @($failures | ForEach-Object {
        "{0}: line {1}% < {2}% or branch {3}% < {4}% (report: {5})" -f `
            $_.Name, $_.LineRate, $_.LineFloor, $_.BranchRate, $_.BranchFloor, $SummaryPath
    })
    throw "Coverage gate failed:`n$($messages -join "`n")"
}

Write-Host "Coverage gate passed."
