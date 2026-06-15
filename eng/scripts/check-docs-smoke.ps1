param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gameServerExample = Join-Path $root "samples/Kernels/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj"
$gamePluginDll = Join-Path $root "samples/Kernels/GameServer/Examples.GameServer.Plugin/bin/$Configuration/net10.0/Examples.GameServer.Plugin.dll"

function Resolve-RepoPath([string] $Path) {
    $normalized = $Path.Trim().Trim('"').Replace('\', [System.IO.Path]::DirectorySeparatorChar)
    return Join-Path $root $normalized
}

function Assert-ExistingPath([string] $Document, [int] $LineNumber, [string] $Path) {
    $resolved = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $resolved)) {
        throw "$Document line $LineNumber references missing path: $Path"
    }
}

function Test-DocumentCommands([string] $Path) {
    $lines = Get-Content -LiteralPath $Path
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i].Trim()
        if ($line -match '^dotnet\s+(restore|build|test|pack)\s+(?<target>\S+)') {
            Assert-ExistingPath $Path ($i + 1) $matches["target"]
            continue
        }

        if ($line -match '^dotnet\s+run\b.*\s--project\s+(?<project>\S+)') {
            Assert-ExistingPath $Path ($i + 1) $matches["project"]
            continue
        }

        if ($line -match '^\.(?<script>\\scripts\\\S+\.ps1)') {
            Assert-ExistingPath $Path ($i + 1) ("." + $matches["script"])
        }
    }
}

function Assert-DocsDoNotContain([string] $Pattern, [string] $Description) {
    $documents = Get-ChildItem -LiteralPath (Join-Path $root "docs/Specs") -Recurse -File -Filter "*.md"
    $matches = @($documents | Select-String -Pattern $Pattern)
    if ($matches.Count -gt 0) {
        $first = $matches[0]
        throw "Documentation contains stale text ($Description): $($first.Path):$($first.LineNumber)"
    }
}

function Read-TextIfExists([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    return (Get-Content -LiteralPath $Path -Raw)
}

function Write-CapturedOutput([string] $Description, [string] $OutputPath, [string] $ErrorPath) {
    $output = Read-TextIfExists $OutputPath
    $errorOutput = Read-TextIfExists $ErrorPath

    if (-not [string]::IsNullOrWhiteSpace($output)) {
        Write-Host $output.TrimEnd()
    }

    if (-not [string]::IsNullOrWhiteSpace($errorOutput)) {
        Write-Warning "$Description stderr:`n$($errorOutput.TrimEnd())"
    }
}

function Stop-ProcessTree([System.Diagnostics.Process] $Process) {
    if ($Process.HasExited) {
        return
    }

    try {
        $Process.Kill($true)
    } catch {
        $Process.Kill()
    }

    $Process.WaitForExit()
}

function Invoke-GameServer([string] $ServerProject, [string] $HostDll) {
    $outputPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-game-" + [Guid]::NewGuid().ToString("N") + ".out")
    $errorPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-game-" + [Guid]::NewGuid().ToString("N") + ".err")
    $arguments = @(
        "run", "--project", $ServerProject,
        "--configuration", $Configuration,
        "--no-build")
    $parameters = @{
        FilePath = "dotnet"
        ArgumentList = $arguments
        RedirectStandardOutput = $outputPath
        RedirectStandardError = $errorPath
        PassThru = $true
    }

    if ($IsWindows) {
        $parameters.WindowStyle = "Hidden"
    }

    $previousHostDll = $env:SAFEIR_GAME_PLUGIN_DLL
    $env:SAFEIR_GAME_PLUGIN_DLL = $HostDll
    try {
        $process = Start-Process @parameters
    } finally {
        $env:SAFEIR_GAME_PLUGIN_DLL = $previousHostDll
    }

    try {
        if (-not $process.WaitForExit(60000)) {
            Stop-ProcessTree $process
            Write-CapturedOutput "GameServer example smoke test" $outputPath $errorPath
            throw "GameServer example smoke test timed out after 60 seconds."
        }

        Write-CapturedOutput "GameServer example smoke test" $outputPath $errorPath
        if ($process.ExitCode -ne 0) {
            throw "GameServer example smoke test failed with exit code $($process.ExitCode)."
        }
    } finally {
        $process.Dispose()
        Remove-Item -LiteralPath $outputPath, $errorPath -Force -ErrorAction SilentlyContinue
    }
}

Test-DocumentCommands (Join-Path $root "README.md")
Test-DocumentCommands (Join-Path $root "CONTRIBUTING.md")
Test-DocumentCommands (Join-Path $root "docs/getting-started/README.md")
Test-DocumentCommands (Join-Path $root "docs/Specs/Addendum/Examples.md")

Assert-DocsDoNotContain "Sandbox\.Parse" "JSON IR import is Sandbox.ImportJson"
Assert-DocsDoNotContain "tenant://123/config" "file grants use canonical filesystem roots"
Assert-DocsDoNotContain "Proposed Public C# API" "public API document is no longer proposed"
Assert-DocsDoNotContain "Proposed C# API surface" "public API index is no longer proposed"
Assert-DocsDoNotContain "Add compiler/cache after the core model is proven" "compiled mode is implemented"

if (-not $IsWindows) {
    Write-Host "Skipping GameServer runtime smoke on non-Windows runners."
    Write-Host "Docs/example smoke checks passed."
    return
}

if (-not (Test-Path -LiteralPath $gamePluginDll)) {
    throw "GameServer smoke prerequisite missing: $gamePluginDll (build the solution first)."
}

Invoke-GameServer $gameServerExample $gamePluginDll

Write-Host "Docs/example smoke checks passed."
