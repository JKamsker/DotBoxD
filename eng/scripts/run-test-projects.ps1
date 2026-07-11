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
    $passed = $false
    $failedTestsBeforeRetry = @()

    foreach ($attempt in 1..$Attempts) {
        $trxFileName = "$projectName-$safeSuiteName-attempt$attempt.trx"
        $filterDescription = if ([string]::IsNullOrWhiteSpace($Filter)) { "all tests" } else { $Filter }
        Write-Host "::group::dotnet test $projectName ($SuiteName; $filterDescription; attempt $attempt)"

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

        if (-not [string]::IsNullOrWhiteSpace($Filter)) {
            $arguments += @("--filter", $Filter)
        }

        & dotnet @arguments
        $exitCode = $LASTEXITCODE
        Write-Host "::endgroup::"

        if ($exitCode -eq 0) {
            Assert-TrxHasExecutedTests (Join-Path $resultsPath $trxFileName) $projectName
            foreach ($testName in @($failedTestsBeforeRetry | Sort-Object -Unique)) {
                Write-Host "::warning title=Flaky test::$testName failed before $projectName passed on retry in suite $SuiteName."
            }
            $passed = $true
            break
        }

        if ($attempt -lt $Attempts) {
            $failedTestsBeforeRetry += Get-FailedTrxTests (Join-Path $resultsPath $trxFileName)
            Write-Host "::warning::$projectName failed in suite $SuiteName on attempt $attempt (exit $exitCode); retrying once."
        }
    }

    if (-not $passed) {
        $failed = $true
    }
}

if ($failed) {
    throw "One or more test projects failed after $Attempts attempt(s)."
}
