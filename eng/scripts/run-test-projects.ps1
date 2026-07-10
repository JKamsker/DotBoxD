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

function Join-TestFilterOr([string[]] $Filters) {
    $parts = @($Filters | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($parts.Count -eq 0) {
        return ""
    }

    if ($parts.Count -eq 1) {
        return $parts[0]
    }

    return "(" + ($parts -join "|") + ")"
}

function Join-TestFilterAnd([string[]] $Filters) {
    $parts = @($Filters | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($parts.Count -eq 0) {
        return ""
    }

    if ($parts.Count -eq 1) {
        return $parts[0]
    }

    return "(" + ($parts -join ")&(") + ")"
}

function New-TestFilterShardFromFilters([string] $Name, [string[]] $ShardFilters, [string] $BaseFilter) {
    $namespaceFilter = Join-TestFilterOr $ShardFilters
    [pscustomobject] @{
        Name = $Name
        Filter = Join-TestFilterAnd @($BaseFilter, $namespaceFilter)
    }
}

function New-TestFilterShard([string] $Name, [string[]] $NamespacePrefixes, [string] $BaseFilter) {
    New-TestFilterShardFromFilters $Name (@($NamespacePrefixes | ForEach-Object {
        "FullyQualifiedName~DotBoxD.Kernels.Tests.$_"
    })) $BaseFilter
}

function Get-TestFilterShards([string] $ProjectName, [string] $SuiteName, [string] $BaseFilter) {
    if ($ProjectName -ne "DotBoxD.Kernels.Tests" -or $SuiteName -ne "kernels-remainder") {
        return @([pscustomobject] @{
            Name = ""
            Filter = $BaseFilter
        })
    }

    # The full Kernels remainder suite can terminate the test host after a large
    # number of Roslyn-heavy tests on small CI runners. Keep the public CI matrix
    # unchanged, but run this one suite in fresh per-namespace-group test hosts.
    return @(
        (New-TestFilterShard "compiled-runtime" @(
            "Compiled",
            "Execution",
            "Fuzz",
            "Runtime",
            "Verifier",
            "Wave9Fixes",
            "Workers"
        ) $BaseFilter),
        (New-TestFilterShardFromFilters "plugin-analyzer-generated-hook-chain" @(
            "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerHookChain"
        ) $BaseFilter),
        (New-TestFilterShardFromFilters "plugin-analyzer-generated-plugin-server" @(
            "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginServer"
        ) $BaseFilter),
        (New-TestFilterShardFromFilters "plugin-analyzer-generated-invoke" @(
            "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsync"
        ) $BaseFilter),
        (New-TestFilterShardFromFilters "plugin-analyzer-generated-hooks" @(
            "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.Hook"
        ) $BaseFilter),
        (New-TestFilterShardFromFilters "plugin-analyzer-generated-core" @(
            "(FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzer)&(FullyQualifiedName!~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.PluginAnalyzerHookChain)",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.GeneratedPackage",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.Kernel",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.ServerExtension"
        ) $BaseFilter),
        (New-TestFilterShard "plugin-analyzer-runtime" @("PluginAnalyzer.Runtime") $BaseFilter),
        (New-TestFilterShard "plugin-analyzer-core" @(
            "PluginAnalyzer.Contracts",
            "PluginAnalyzer.Core",
            "PluginAnalyzer.Defaults",
            "PluginAnalyzer.Detection",
            "PluginAnalyzer.Generation",
            "PluginAnalyzer.HostBinding",
            "PluginAnalyzer.KernelMethod",
            "PluginAnalyzer.Weaving"
        ) $BaseFilter),
        (New-TestFilterShard "plugins-rpc" @("Plugins.Rpc") $BaseFilter),
        (New-TestFilterShard "plugins-regression" @("Plugins.Regression") $BaseFilter),
        (New-TestFilterShardFromFilters "plugins-root" @(
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.CapabilityPolicySplitTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.CompiledPluginMessageBindingTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.EventIndexMatcherTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.KernelPackageRegistryTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginAddendumTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginExecutionObservationTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginHookSignatureTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginInputAllocationTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginMessageBindingTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginOwnershipTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginPackageJsonTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginPackageValidationTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginResultPackageValidationTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.PluginRevocationTests",
            "FullyQualifiedName~DotBoxD.Kernels.Tests.Plugins.RpcIpcAddonTests"
        ) $BaseFilter),
        (New-TestFilterShard "plugins-hooks" @(
            "Plugins.Hooks",
            "Plugins.Runtime"
        ) $BaseFilter),
        (New-TestFilterShard "plugins-support" @(
            "Plugins.AdapterFqn",
            "Plugins.Capability",
            "Plugins.Documentation",
            "Plugins.Indexing",
            "Plugins.Json",
            "Plugins.LiveSettings",
            "Plugins.Messaging",
            "Plugins.Policy",
            "Plugins.Server"
        ) $BaseFilter),
        (New-TestFilterShard "core-hosting" @(
            "Audit",
            "Bindings",
            "Collections",
            "Core",
            "Hosting",
            "Interpreter",
            "Model"
        ) $BaseFilter),
        (New-TestFilterShard "policy-query" @(
            "Policy",
            "Queryable",
            "Resources",
            "Samples",
            "Serialization",
            "Strings",
            "Support",
            "Tooling",
            "Validation"
        ) $BaseFilter)
    )
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
    $filterShards = @(Get-TestFilterShards $projectName $SuiteName $Filter)

    foreach ($shard in $filterShards) {
        $shardPassed = $false
        $safeShardName = if ([string]::IsNullOrWhiteSpace($shard.Name)) { "" } else { "-" + (ConvertTo-SafeFileName $shard.Name) }

        foreach ($attempt in 1..$Attempts) {
            $trxFileName = "$projectName-$safeSuiteName$safeShardName-attempt$attempt.trx"
            $filterDescription = if ([string]::IsNullOrWhiteSpace($shard.Filter)) { "all tests" } else { $shard.Filter }
            Write-Host "::group::dotnet test $projectName ($SuiteName$safeShardName; $filterDescription; attempt $attempt)"

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

            if (-not [string]::IsNullOrWhiteSpace($shard.Filter)) {
                $arguments += @("--filter", $shard.Filter)
            }

            & dotnet @arguments
            $exitCode = $LASTEXITCODE
            Write-Host "::endgroup::"

            if ($exitCode -eq 0) {
                Assert-TrxHasExecutedTests (Join-Path $resultsPath $trxFileName) $projectName
                foreach ($testName in @($failedTestsBeforeRetry | Sort-Object -Unique)) {
                    Write-Host "::warning title=Flaky test::$testName failed before $projectName passed on retry in suite $SuiteName$safeShardName."
                }
                $shardPassed = $true
                break
            }

            if ($attempt -lt $Attempts) {
                $failedTestsBeforeRetry += Get-FailedTrxTests (Join-Path $resultsPath $trxFileName)
                Write-Host "::warning::$projectName failed in suite $SuiteName$safeShardName on attempt $attempt (exit $exitCode); retrying once."
            }
        }

        if (-not $shardPassed) {
            $failed = $true
            break
        }
    }

    if (-not $failed) {
        $passed = $true
    }
}

if ($failed) {
    throw "One or more test projects failed after $Attempts attempt(s)."
}
