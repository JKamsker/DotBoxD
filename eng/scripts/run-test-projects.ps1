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

function Join-AnyTestFilter([string[]] $Filters) {
    return ($Filters | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "|"
}

function New-TestBatch([string] $Name, [string] $Filter) {
    return [pscustomobject] @{
        Name = $Name
        Filter = $Filter
    }
}

function Get-TestBatches([string] $ProjectName, [string] $SuiteName, [string] $Filter) {
    if ($ProjectName -ne "DotBoxD.Kernels.Tests" -or $SuiteName -ne "kernels-remainder") {
        return @(New-TestBatch "" $Filter)
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

    $batches = @($prefixes | ForEach-Object {
        $batchFilter = "FullyQualifiedName~DotBoxD.Kernels.Tests.$_"
        New-TestBatch $_ (Join-TestFilters $Filter $batchFilter)
    })

    $pluginAnalyzerBatches = @(
        [pscustomobject] @{
            Name = "PluginAnalyzer-Contracts"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Core"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Core"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Defaults"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Defaults"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Detection"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Detection"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Generation"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generation"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-HostBinding"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-KernelMethod"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Runtime"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Weaving"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Weaving"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Generated-HookChains"
            Filter = Join-AnyTestFilter @(
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerHookChain",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChain")
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Generated-PluginServer"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginServer"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Generated-HookResults"
            Filter = Join-AnyTestFilter @(
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResult",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookFireAsync")
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Generated-InvokeAsync"
            Filter = Join-AnyTestFilter @(
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsync",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.LowerToIr")
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Generated-MergeableIr"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.MergeableIr"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Generated-Polymorphic"
            Filter = "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerPolymorphic"
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Generated-Expressions"
            Filter = Join-AnyTestFilter @(
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerString",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerProperty",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerConstant",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerConditional",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerNumeric",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerPattern",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerRange",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerShortCircuit",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerBlockBody",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerValueReceiver")
        },
        [pscustomobject] @{
            Name = "PluginAnalyzer-Generated-Misc"
            Filter = Join-AnyTestFilter @(
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerTests",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerDatabase",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerEventProperty",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerClosedGeneric",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerEnumerable",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerIncrementality",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerInherited",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerLiveSetting",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerNullable",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerPluginId",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginPackage",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.EventPropertyCapability",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.GeneratedPackagePluginId",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.KernelClassIndexMetadata",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerInvocation",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerNegativeLiteral",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerResultLocalHandlerValidator",
                "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.ServerExtensionPackage")
        }
    )

    $batches += @($pluginAnalyzerBatches | ForEach-Object {
        New-TestBatch $_.Name (Join-TestFilters $Filter $_.Filter)
    })

    return $batches
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
