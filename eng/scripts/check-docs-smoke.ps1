param(
    [string] $Configuration = "Release",
    [switch] $SkipLinkValidation
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gameServerExample = Join-Path $root "samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj"
$gamePluginDll = Join-Path $root "samples/GameServer/Examples.GameServer.Plugin/bin/$Configuration/net10.0/Examples.GameServer.Plugin.dll"

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

function Test-IsRepoPath([string] $Path) {
    $normalized = $Path.Replace('\', '/')
    if ($normalized.StartsWith('./')) {
        $normalized = $normalized.Substring(2)
    }
    return $normalized -in @('DotBoxD.slnx', 'DotBoxD.Packages.slnx') -or
        $normalized.StartsWith('eng/') -or
        $normalized.StartsWith('samples/') -or
        $normalized.StartsWith('tests/') -or
        $normalized.StartsWith('tools/')
}

function Test-DocumentCommands([string] $Path) {
    $lines = Get-Content -LiteralPath $Path
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i].Trim()
        if ($line -match '^dotnet\s+(restore|build|test|pack)\s+(?<target>\S+)') {
            if (Test-IsRepoPath $matches["target"]) {
                Assert-ExistingPath $Path ($i + 1) $matches["target"]
            }
            continue
        }

        if ($line -match '^dotnet\s+run\b.*\s--project\s+(?<project>\S+)') {
            if (Test-IsRepoPath $matches["project"]) {
                Assert-ExistingPath $Path ($i + 1) $matches["project"]
            }
            continue
        }

        if ($line -match '^\.(?<script>\\scripts\\\S+\.ps1)') {
            Assert-ExistingPath $Path ($i + 1) ("." + $matches["script"])
        }
    }
}

function Assert-DocsDoNotContain([string] $Pattern, [string] $Description) {
    Assert-DocumentsDoNotContain (Get-ChildItem -LiteralPath (Join-Path $root "docs/Specs") -Recurse -File -Filter "*.md") $Pattern $Description
}

function Assert-DocumentsDoNotContain([System.IO.FileInfo[]] $Documents, [string] $Pattern, [string] $Description) {
    $documents = @($Documents | Where-Object { $_ -ne $null })
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

function Assert-TextContains([string] $Text, [string] $Expected, [string] $Description) {
    if (-not $Text.Contains($Expected, [System.StringComparison]::Ordinal)) {
        throw "GameServer example smoke did not show $Description. Missing text: $Expected"
    }
}

function Assert-TextMatches([string] $Text, [string] $Pattern, [string] $Description) {
    if ($Text -notmatch $Pattern) {
        throw "GameServer example smoke did not show $Description. Missing pattern: $Pattern"
    }
}

function Read-GameServerMetric([string] $Output, [string] $Label) {
    $pattern = "(?m)^" + [regex]::Escape($Label) + ":\s+(?<value>[0-9]+(?:\.[0-9]+)?)\s*$"
    if ($Output -notmatch $pattern) {
        throw "GameServer example smoke did not report '$Label'."
    }

    return [double]::Parse($matches["value"], [System.Globalization.CultureInfo]::InvariantCulture)
}

function Assert-GameServerOutput([string] $Output) {
    Assert-TextContains $Output "=== DotBoxD.Kernels Game Server (golden example) ===" "the GameServer banner"
    Assert-TextContains $Output "--- BASELINE (no plugins) ---" "the no-plugin baseline phase"
    Assert-TextContains $Output "--- WITH PLUGINS (guardian calms, retaliation taunts) ---" "the with-plugin phase"
    Assert-TextContains $Output "[server] plugin connected; event kernels and server extension are installed and live." "plugin readiness"

    Assert-TextContains $Output "[plugin] Monsters.Get(monster-4).KillAsync() => True." "a scoped server-extension call"
    Assert-TextContains $Output "[plugin] Monsters.KillMonstersAsync(...) => 3 results." "a batch server-extension call"
    Assert-TextContains $Output "[plugin] Monsters.KillMonstersInRangeAsync(query, ...) => 3 results." "a query server-extension call"
    Assert-TextContains $Output "[plugin] local hook RunLocal observed 1 attack event." "the plugin-side RunLocal callback"
    Assert-TextContains $Output "[server] registered indexed subscription: AttackEvent AttackerId == player-1" "the indexed remote chain registration"
    Assert-TextContains $Output "[server] registered indexed prefilter: AttackEvent Damage >= 5" "the indexed prefilter registration"
    Assert-TextMatches $Output "(?m)^\s+effect: calm:" "at least one plugin-driven calm effect"
    Assert-TextContains $Output "Plugins reduced bullying: low-level players survive longer than baseline." "the summary behavior claim"
    Assert-TextContains $Output "On disconnect the plugin's kernels were unloaded (installed kernels now: 0)." "kernel unload after plugin disconnect"

    $baseline = Read-GameServerMetric $Output "Baseline damage/tick (no plugin)"
    $withPlugin = Read-GameServerMetric $Output "With-plugin damage/tick"
    if ($baseline -le 0) {
        throw "GameServer example smoke expected baseline damage to be greater than zero, but saw $baseline."
    }

    if ($withPlugin -ge $baseline) {
        throw "GameServer example smoke expected plugin damage/tick ($withPlugin) to be lower than baseline ($baseline)."
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

        Assert-GameServerOutput (Read-TextIfExists $outputPath)
    } finally {
        $process.Dispose()
        Remove-Item -LiteralPath $outputPath, $errorPath -Force -ErrorAction SilentlyContinue
    }
}

$publicDocuments = @(
    Get-Item -LiteralPath (Join-Path $root "README.md"), (Join-Path $root "CONTRIBUTING.md")
    Get-ChildItem -LiteralPath (Join-Path $root "docs-site/src/content/docs") -Recurse -File -Include "*.md", "*.mdx"
)
# The repository's design, legacy, task, and specification trees are engineering
# records, not published user documentation. Dedicated checks below continue to
# validate the current claims and examples that those records are expected to keep.
foreach ($document in $publicDocuments) {
    Test-DocumentCommands $document.FullName
}

if (-not $SkipLinkValidation) {
    & (Join-Path $PSScriptRoot "check-doc-links.ps1")
}

& (Join-Path $PSScriptRoot "check-diagnostic-docs.ps1")

Assert-DocsDoNotContain "Sandbox\.Parse" "JSON IR import is Sandbox.ImportJson"
Assert-DocsDoNotContain "tenant://123/config" "file grants use canonical filesystem roots"
Assert-DocsDoNotContain "Proposed Public C# API" "public API document is no longer proposed"
Assert-DocsDoNotContain "Proposed C# API surface" "public API index is no longer proposed"
Assert-DocsDoNotContain "Add compiler/cache after the core model is proven" "compiled mode is implemented"

$pluginFluentDocs = @(
    "docs/design/plugin-fluent-hooks-api/server-walkthrough.md",
    "docs/design/plugin-fluent-hooks-api/plugin-walkthrough.md",
    "docs/design/plugin-fluent-hooks-api/kernel-binding-model.md",
    "docs/design/remote-plugin-server-builder/interface-driven-plugin-server.md"
) | ForEach-Object { Get-Item -LiteralPath (Resolve-RepoPath $_) }

$currentServerExtensionDocs = @(
    "README.md",
    "docs-site/src/content/docs/overview.md",
    "docs-site/src/content/docs/getting-started.md",
    "docs-site/src/content/docs/concepts/pushdown.md",
    "docs/Specs/Addendum/Examples.md",
    "docs/design/plugin-fluent-hooks-api/followups.md",
    "docs/design/remote-plugin-server-builder/invoke-async.md"
) | ForEach-Object { Get-Item -LiteralPath (Resolve-RepoPath $_) }

foreach ($document in $pluginFluentDocs) {
    Test-DocumentCommands $document.FullName
}

Assert-DocumentsDoNotContain $pluginFluentDocs "GameWorld\.CreateDefault\(server\.Hooks\)" "GameWorld.CreateDefault now receives hooks and subscriptions"
Assert-DocumentsDoNotContain $pluginFluentDocs "DBXK110.*DBXK114" "unsupported hook-chain diagnostics are DBXK111-DBXK116"
Assert-DocumentsDoNotContain $pluginFluentDocs "server\.Events\.On" "server hook surface is server.Hooks"
Assert-DocumentsDoNotContain $pluginFluentDocs "server\.Kernels\.(Register|Get)" "generated server surface no longer uses server.Kernels"
Assert-DocumentsDoNotContain $pluginFluentDocs "InvokeKernel|InvokeLocal" "old kernel invocation terminology is stale"
Assert-DocumentsDoNotContain $pluginFluentDocs "SetValuesAsync" "live-settings API uses generated update flow"

Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcService" "server extensions use ServerExtensionAttribute"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcClientMethod" "server-extension clients use ServerExtensionMethodAttribute"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "RegisterKernelRpcService" "server extensions register through RegisterServerExtensionAsync"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "RegisterRpcServiceAsync" "server extensions register through RegisterServerExtensionAsync"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "RpcService<" "server extensions use ServerExtension<T>"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "server\.KernelRpc" "generated server surface no longer uses server.KernelRpc"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "SetupKernelRpc" "builder docs use SetupServerExtensions"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "RemoteKernelRpcControl" "builder docs use RemoteServerExtensionControl"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcRegistrationAccumulator" "builder docs use ServerExtensionRegistrationAccumulator"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "kernel RPC" "current docs call this server extensions"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcMarshaller" "current server-extension docs avoid legacy KernelRpcMarshaller terminology"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcValue" "current server-extension docs avoid legacy KernelRpcValue terminology"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcBinaryCodec" "current server-extension docs avoid legacy KernelRpcBinaryCodec terminology"

if (-not $IsWindows) {
    Write-Host "Skipping GameServer runtime smoke on non-Windows runners."
    Write-Host "Docs/static smoke checks passed; GameServer runtime smoke was skipped."
    return
}

if (-not (Test-Path -LiteralPath $gamePluginDll)) {
    throw "GameServer smoke prerequisite missing: $gamePluginDll (build the solution first)."
}

Invoke-GameServer $gameServerExample $gamePluginDll

Write-Host "Docs/example smoke checks passed."
