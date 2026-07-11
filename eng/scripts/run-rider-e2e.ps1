param(
    [TimeSpan] $StartupTimeout = [TimeSpan]::FromMinutes(10)
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pluginDirectory = Join-Path $repositoryRoot 'ide/rider-dotboxd-debug'
$gradle = Join-Path $pluginDirectory 'gradlew.bat'
$artifactDirectory = Join-Path $repositoryRoot 'artifacts/rider-e2e'
$standardOutput = Join-Path $artifactDirectory 'rider.stdout.log'
$standardError = Join-Path $artifactDirectory 'rider.stderr.log'
$ideaLog = Join-Path $pluginDirectory '.intellijPlatform/sandbox/dotboxd-kernel-debug-rider/RD-2025.2.1/log_runIdeForUiTests/idea.log'
$riderProcess = $null

function Invoke-Checked([string] $FilePath, [string[]] $Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Stop-RiderProcesses([int] $LauncherProcessId) {
    $sandboxMarker = (Join-Path $pluginDirectory '.intellijPlatform/sandbox').Replace('\', '/')
    Get-CimInstance Win32_Process | Where-Object {
        $commandLine = if ($_.CommandLine) { $_.CommandLine.Replace('\', '/') } else { '' }
        $_.ProcessId -eq $LauncherProcessId -or
        ($commandLine.Contains($sandboxMarker, [StringComparison]::OrdinalIgnoreCase) -and
            ($commandLine.Contains('runIdeForUiTests', [StringComparison]::OrdinalIgnoreCase) -or
                $commandLine.Contains('system_runIdeForUiTests', [StringComparison]::OrdinalIgnoreCase)))
    } | Sort-Object ProcessId -Descending | ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Stop-ExampleProcesses {
    Get-CimInstance Win32_Process | Where-Object {
        $_.CommandLine -and
        $_.CommandLine.Contains($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        $_.CommandLine.Contains('Examples.GameServer', [StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
Remove-Item $ideaLog -Force -ErrorAction SilentlyContinue

try {
    Push-Location $repositoryRoot
    Invoke-Checked dotnet @(
        'build', 'samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj',
        '-c', 'Debug', '--nologo'
    )
    Invoke-Checked dotnet @(
        'build', 'samples/GameServer/Examples.GameServer.Plugin/Examples.GameServer.Plugin.csproj',
        '-c', 'Debug', '--nologo'
    )
    Pop-Location

    $riderProcess = Start-Process -FilePath $gradle `
        -ArgumentList @('runIdeForUiTests', '--console=plain', '--no-daemon') `
        -WorkingDirectory $pluginDirectory `
        -RedirectStandardOutput $standardOutput `
        -RedirectStandardError $standardError `
        -WindowStyle Hidden `
        -PassThru

    $deadline = [DateTime]::UtcNow + $StartupTimeout
    do {
        if ($riderProcess.HasExited) {
            throw "Rider exited before its UI test endpoint became ready (exit $($riderProcess.ExitCode))."
        }
        try {
            Invoke-WebRequest 'http://127.0.0.1:8082' -UseBasicParsing -TimeoutSec 2 | Out-Null
            $ready = $true
        }
        catch {
            $ready = $false
            Start-Sleep -Milliseconds 500
        }
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)

    if (-not $ready) {
        throw "Rider did not expose its UI test endpoint within $StartupTimeout."
    }

    Push-Location $pluginDirectory
    Invoke-Checked $gradle @('e2eTest', '--console=plain', '--no-daemon')
    Pop-Location

    $debuggerErrors = Select-String -Path $ideaLog -Pattern @(
        'ResponseErrorException',
        'selected kernel execution is no longer stopped',
        'Remote kernel debugging is disabled'
    ) -ErrorAction SilentlyContinue
    if ($debuggerErrors) {
        throw "Rider logged a kernel-debugger protocol error: $($debuggerErrors[0].Line)"
    }
}
finally {
    while ((Get-Location).Path -ne $repositoryRoot -and (Get-Location).Path.StartsWith($repositoryRoot)) {
        Pop-Location
    }
    if ($null -ne $riderProcess) {
        Stop-RiderProcesses $riderProcess.Id
    }
    Stop-ExampleProcesses

    if (Test-Path $ideaLog) {
        Copy-Item $ideaLog (Join-Path $artifactDirectory 'idea.log') -Force
    }
}
