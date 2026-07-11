param(
    [string] $PackageDirectory = "artifacts/packages",
    [Parameter(Mandatory = $true)]
    [string] $ExpectedVersion,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packageRoot = if ([System.IO.Path]::IsPathRooted($PackageDirectory)) {
    [System.IO.Path]::GetFullPath($PackageDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $root $PackageDirectory))
}
if (-not (Test-Path -LiteralPath $packageRoot)) {
    throw "Template smoke package directory does not exist: $packageRoot"
}

$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts"))
$workRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "template-smoke"))
if (-not $workRoot.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean template smoke directory outside artifacts: $workRoot"
}

Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $workRoot | Out-Null
$hive = Join-Path $workRoot ".template-engine"
$nugetConfig = Join-Path $workRoot "NuGet.Config"
$escapedPackageRoot = [System.Security.SecurityElement]::Escape($packageRoot)
[System.IO.File]::WriteAllText($nugetConfig, @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="dotboxd-local" value="$escapedPackageRoot" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@)

$templates = @(
    @{ ShortName = "dotboxd-service"; Path = "templates/service-only"; ProjectName = "ServiceSmoke" },
    @{ ShortName = "dotboxd-sidecar"; Path = "templates/named-pipe-sidecar"; ProjectName = "SidecarSmoke" },
    @{ ShortName = "dotboxd-kernel-host"; Path = "templates/kernel-host"; ProjectName = "KernelSmoke" }
)

foreach ($template in $templates) {
    $templatePath = Join-Path $root $template.Path
    & dotnet new --debug:custom-hive $hive install $templatePath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install template $($template.ShortName)."
    }

    $output = Join-Path $workRoot $template.ProjectName
    & dotnet new --debug:custom-hive $hive $template.ShortName -n $template.ProjectName -o $output
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to instantiate template $($template.ShortName)."
    }

    $projects = @(Get-ChildItem -LiteralPath $output -Filter "*.csproj" -File)
    if ($projects.Count -ne 1) {
        throw "Generated template $($template.ShortName) produced $($projects.Count) project files; expected one."
    }
    $project = $projects[0]
    $projectText = Get-Content -LiteralPath $project.FullName -Raw
    $projectText = $projectText.Replace('Version="0.1.0-*"', "Version=`"$ExpectedVersion`"")
    # The smoke output lives under the repository's artifacts directory and would otherwise inherit
    # the repository's central package management. A generated consumer is a standalone project.
    $projectText = $projectText.Replace(
        "<PropertyGroup>",
        "<PropertyGroup>`n    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>")
    [System.IO.File]::WriteAllText($project.FullName, $projectText)

    & dotnet restore $project.FullName --configfile $nugetConfig `
        -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restore generated template $($template.ShortName)."
    }

    & dotnet build $project.FullName -c $Configuration --no-restore `
        -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build generated template $($template.ShortName)."
    }
}

Write-Host "All $($templates.Count) dotnet new templates compile against packed version $ExpectedVersion."
