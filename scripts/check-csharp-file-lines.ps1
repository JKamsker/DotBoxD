param(
    [int] $WarnAt,
    [int] $FailAt,
    [int] $MaxFilesPerFolder,
    [int] $MaxFilesInProjectFolder
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "tools/CodeEnforcer/src/CodeEnforcer/CodeEnforcer.csproj"
$targetFramework = "net10.0"
$configuration = "Release"
$assembly = Join-Path $root "tools/CodeEnforcer/src/CodeEnforcer/bin/$configuration/$targetFramework/CodeEnforcer.dll"
$arguments = @(
    "--root", $root
)

if ($PSBoundParameters.ContainsKey("WarnAt")) {
    $arguments += @("--soft-line-limit", $WarnAt)
}

if ($PSBoundParameters.ContainsKey("FailAt")) {
    $arguments += @("--hard-line-limit", $FailAt)
}

if ($PSBoundParameters.ContainsKey("MaxFilesPerFolder")) {
    $arguments += @("--max-files-per-folder", $MaxFilesPerFolder)
}

if ($PSBoundParameters.ContainsKey("MaxFilesInProjectFolder")) {
    $arguments += @("--max-files-per-root-dir", $MaxFilesInProjectFolder)
}

if (-not (Test-Path -LiteralPath $assembly)) {
    Write-Host "CodeEnforcer is not compiled. Building $configuration..."
    & dotnet build $project --configuration $configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Push-Location $root
try {
    & dotnet $assembly @arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
