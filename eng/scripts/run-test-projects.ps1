param(
    [Parameter(Mandatory = $true)]
    [string[]] $Projects,
    [string] $Configuration = "Release",
    [string] $Filter = "",
    [string] $ResultsDirectory = "artifacts/test-results",
    [string] $SuiteName = "tests",
    [int] $Attempts = 2,
    [switch] $NoRestore,
    [switch] $NoBuild,
    [string] $BlameHangTimeout = "5m"
)

$ErrorActionPreference = "Stop"

if ($Attempts -lt 1) {
    throw "Attempts must be at least 1."
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resultsPath = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory
} else {
    Join-Path $root $ResultsDirectory
}

New-Item -ItemType Directory -Force -Path $resultsPath | Out-Null

function Resolve-ProjectPath([string] $Project) {
    $trimmed = $Project.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $null
    }

    $projectPath = if ([System.IO.Path]::IsPathRooted($trimmed)) {
        $trimmed
    } else {
        Join-Path $root $trimmed
    }

    if (-not (Test-Path -LiteralPath $projectPath)) {
        throw "Test project does not exist: $projectPath"
    }

    return $projectPath
}

function ConvertTo-SafeFileName([string] $Value) {
    $safe = $Value -replace "[^A-Za-z0-9_.-]", "-"
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return "tests"
    }

    return $safe.Trim("-")
}

function Assert-TrxHasExecutedTests([string] $TrxPath, [string] $ProjectName) {
    if (-not (Test-Path -LiteralPath $TrxPath)) {
        throw "dotnet test did not produce the expected TRX file for ${ProjectName}: $TrxPath"
    }

    [xml] $trx = Get-Content -Raw -LiteralPath $TrxPath
    $executed = @($trx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object {
        [string] $_.outcome -ne "NotExecuted"
    })

    if ($executed.Count -eq 0) {
        throw "dotnet test matched zero executed tests for $ProjectName."
    }
}

function Get-FailedTrxTests([string] $TrxPath) {
    if (-not (Test-Path -LiteralPath $TrxPath)) {
        return @()
    }

    try {
        [xml] $trx = Get-Content -Raw -LiteralPath $TrxPath
        return @($trx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object {
            [string] $_.outcome -eq "Failed"
        } | ForEach-Object { [string] $_.testName } | Sort-Object -Unique)
    } catch {
        Write-Warning "Could not read failed test names from '$TrxPath': $($_.Exception.Message)"
        return @()
    }
}

function Join-TestFilters([string] $BaseFilter, [string] $BatchFilter) {
    if ([string]::IsNullOrWhiteSpace($BaseFilter)) {
        return $BatchFilter
    }

    if ([string]::IsNullOrWhiteSpace($BatchFilter)) {
        return $BaseFilter
    }

    return "($BaseFilter)&($BatchFilter)"
}

function Get-TestBatches([string] $ProjectName, [string] $SuiteName, [string] $Filter) {
    if ($ProjectName -ne "DotBoxD.Kernels.Tests" -or $SuiteName -ne "kernels-remainder") {
        return @([pscustomobject] @{
            Name = ""
            Filter = $Filter
        })
    }

    # Keep each kernels-remainder testhost below hosted-runner memory limits.
    $prefixes = @(
        "Audit",
        "Bindings",
        "Collections",
        "Compiled",
        "Core",
        "Execution",
        "Fuzz",
        "Hosting",
        "Interpreter",
        "Model",
        "PluginAnalyzer",
        "Plugins",
        "Policy",
        "Queryable",
        "Resources",
        "Runtime",
        "Samples",
        "Serialization",
        "Strings",
        "Tooling",
        "Validation",
        "Verifier",
        "Wave9Fixes",
        "Workers"
    )

    return @($prefixes | ForEach-Object {
        $batchFilter = "FullyQualifiedName~DotBoxD.Kernels.Tests.$_"
        [pscustomobject] @{
            Name = $_
            Filter = Join-TestFilters $Filter $batchFilter
        }
    })
}

$projectPaths = @($Projects | ForEach-Object { Resolve-ProjectPath $_ } | Where-Object {
    $null -ne $_
})
if ($projectPaths.Count -eq 0) {
    throw "At least one test project must be provided."
}

$safeSuiteName = ConvertTo-SafeFileName $SuiteName
$failed = $false
foreach ($projectPath in $projectPaths) {
    $projectName = Split-Path $projectPath -LeafBase
    $projectPassed = $true
    $batches = @(Get-TestBatches $projectName $SuiteName $Filter)

    foreach ($batch in $batches) {
        $passed = $false
        $failedTestsBeforeRetry = @()
        $batchSuffix = if ([string]::IsNullOrWhiteSpace($batch.Name)) { "" } else { "-" + (ConvertTo-SafeFileName $batch.Name) }
        $batchSuiteName = if ([string]::IsNullOrWhiteSpace($batch.Name)) { $SuiteName } else { "$SuiteName/$($batch.Name)" }

        foreach ($attempt in 1..$Attempts) {
            $trxFileName = "$projectName-$safeSuiteName$batchSuffix-attempt$attempt.trx"
            $filterDescription = if ([string]::IsNullOrWhiteSpace($batch.Filter)) { "all tests" } else { $batch.Filter }
            Write-Host "::group::dotnet test $projectName ($batchSuiteName; $filterDescription; attempt $attempt)"

            $arguments = @(
                "test", $projectPath,
                "--configuration", $Configuration,
                "--logger", "trx;LogFileName=$trxFileName",
                "--results-directory", $resultsPath,
                "--blame-hang",
                "--blame-hang-timeout", $BlameHangTimeout,
                "--blame-hang-dump-type", "mini"
            )

            if ($NoRestore) {
                $arguments += "--no-restore"
            }

            if ($NoBuild) {
                $arguments += "--no-build"
            }

            if (-not [string]::IsNullOrWhiteSpace($batch.Filter)) {
                $arguments += @("--filter", $batch.Filter)
            }

            & dotnet @arguments
            $exitCode = $LASTEXITCODE
            Write-Host "::endgroup::"

            if ($exitCode -eq 0) {
                Assert-TrxHasExecutedTests (Join-Path $resultsPath $trxFileName) $projectName
                foreach ($testName in @($failedTestsBeforeRetry | Sort-Object -Unique)) {
                    Write-Host "::warning title=Flaky test::$testName failed before $projectName passed on retry in suite $batchSuiteName."
                }
                $passed = $true
                break
            }

            if ($attempt -lt $Attempts) {
                $failedTestsBeforeRetry += Get-FailedTrxTests (Join-Path $resultsPath $trxFileName)
                Write-Host "::warning::$projectName failed in suite $batchSuiteName on attempt $attempt (exit $exitCode); retrying once."
            }
        }

        if (-not $passed) {
            $projectPassed = $false
        }
    }

    if (-not $projectPassed) {
        $failed = $true
    }
}

if ($failed) {
    throw "One or more test projects failed after $Attempts attempt(s)."
}
