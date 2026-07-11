param(
    [TimeSpan] $StartupTimeout = [TimeSpan]::FromMinutes(2),
    [TimeSpan] $StopTimeout = [TimeSpan]::FromSeconds(30),
    [string] $VsixPath
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifacts = Join-Path $root 'artifacts/vs26-e2e'
$activityLog = Join-Path $artifacts 'ActivityLog.xml'
$launcherLog = Join-Path $artifacts 'launcher.log'
$resultLog = Join-Path $artifacts 'result.txt'
$guardian = Join-Path $root 'samples/GameServer/Examples.GameServer.Plugin/Kernels/GuardianKernel.cs'
$pluginProgram = Join-Path $root 'samples/GameServer/Examples.GameServer.Plugin/Program.cs'
$windowsPowerShell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$devenv = $null
$stackCursor = 0

function Invoke-Checked([string] $FilePath, [string[]] $Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Dte([string] $Script) {
    $result = & $windowsPowerShell -NoProfile -NonInteractive -Command @"
`$ErrorActionPreference = 'Stop'
`$dte = [Runtime.InteropServices.Marshal]::GetActiveObject('VisualStudio.DTE.18.0')
$Script
"@
    if ($LASTEXITCODE -ne 0) {
        throw 'Visual Studio automation command failed.'
    }
    return $result
}

function Stop-ExamplesAndVisualStudio {
    Get-CimInstance Win32_Process | Where-Object {
        $_.ExecutablePath -and
        ($_.Name -eq 'devenv.exe' -or
            ($_.CommandLine -and
                $_.CommandLine.Contains($root, [StringComparison]::OrdinalIgnoreCase) -and
                $_.CommandLine.Contains('Examples.GameServer', [StringComparison]::OrdinalIgnoreCase)))
    } | ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Wait-ForDte {
    $deadline = [DateTime]::UtcNow + $StartupTimeout
    do {
        Start-Sleep -Milliseconds 500
        try {
            $ready = Invoke-Dte "if (`$dte.Solution.IsOpen) { 'ready' }" 2>$null
        }
        catch {
            $ready = $null
        }
    } until ($ready -contains 'ready' -or [DateTime]::UtcNow -ge $deadline)
    if ($ready -notcontains 'ready') {
        throw "Visual Studio did not open DotBoxD within $StartupTimeout."
    }
}

function Wait-ForKernelAttach {
    $deadline = [DateTime]::UtcNow + $StartupTimeout
    do {
        Start-Sleep -Milliseconds 250
        $attached = (Test-Path $activityLog) -and
            (Select-String $activityLog -Pattern 'Automatically enabled kernel debugging' -SimpleMatch -Quiet)
    } until ($attached -or [DateTime]::UtcNow -ge $deadline)
    if (-not $attached) {
        throw "Visual Studio did not automatically enable kernel debugging. See $activityLog."
    }
}

function Wait-ForManagedStop {
    $deadline = [DateTime]::UtcNow + $StopTimeout
    do {
        Start-Sleep -Milliseconds 200
        $json = Invoke-Dte @'
if ([int]$dte.Debugger.CurrentMode -eq 2) {
    [PSCustomObject]@{
        File = $dte.ActiveDocument.FullName
        Line = $dte.ActiveDocument.Selection.CurrentLine
    } | ConvertTo-Json -Compress
}
'@
        $stop = $json | Where-Object { $_ } | Select-Object -Last 1
        if ($stop) { $stop = $stop | ConvertFrom-Json }
    } until (($stop -and $stop.File -like '*Examples.GameServer.Plugin*Program.cs') -or
        [DateTime]::UtcNow -ge $deadline)
    if (-not $stop -or $stop.File -notlike '*Examples.GameServer.Plugin*Program.cs') {
        throw "Expected a managed plugin Program.cs stop. Last stop: $($stop | ConvertTo-Json -Compress)."
    }
    return $stop
}

function Wait-ForStop([int] $ExpectedLine) {
    $deadline = [DateTime]::UtcNow + $StopTimeout
    do {
        Start-Sleep -Milliseconds 200
        $mode = Invoke-Dte '[int]$dte.Debugger.CurrentMode'
        $stackLines = @(Select-String $launcherLog -Pattern ' adapter stack \d+ (.+) line (\d+)$' -ErrorAction SilentlyContinue)
        for ($index = $script:stackCursor; $mode -eq 2 -and $index -lt $stackLines.Count; $index++) {
            $match = [regex]::Match($stackLines[$index].Line, ' adapter stack \d+ (.+) line (\d+)$')
            if ($match.Success -and [int] $match.Groups[2].Value -eq $ExpectedLine) {
                $stop = [PSCustomObject]@{
                    File = $guardian
                    Line = $ExpectedLine
                    Function = $match.Groups[1].Value
                }
                $script:stackCursor = $stackLines.Count
                break
            }
        }
    } until ($stop -or [DateTime]::UtcNow -ge $deadline)
    if (-not $stop) {
        throw "Expected GuardianKernel.cs:$ExpectedLine. Last stop: $($stop | ConvertTo-Json -Compress)."
    }
    return $stop
}

function Wait-ForNextKernelStop {
    $deadline = [DateTime]::UtcNow + $StopTimeout
    do {
        Start-Sleep -Milliseconds 200
        $mode = Invoke-Dte '[int]$dte.Debugger.CurrentMode'
        $stackLines = @(Select-String $launcherLog -Pattern ' adapter stack \d+ (.+) line (\d+)$' -ErrorAction SilentlyContinue)
        for ($index = $script:stackCursor; $mode -eq 2 -and $index -lt $stackLines.Count; $index++) {
            $match = [regex]::Match($stackLines[$index].Line, ' adapter stack \d+ (.+) line (\d+)$')
            if ($match.Success) {
                $stop = [PSCustomObject]@{
                    File = $guardian
                    Line = [int] $match.Groups[2].Value
                    Function = $match.Groups[1].Value
                }
                $script:stackCursor = $stackLines.Count
                break
            }
        }
    } until ($stop -or [DateTime]::UtcNow -ge $deadline)
    if (-not $stop) {
        throw 'Expected a kernel stop after Step Over.'
    }
    return $stop
}

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
Remove-Item $activityLog, $launcherLog, $resultLog -Force -ErrorAction SilentlyContinue
Stop-ExamplesAndVisualStudio

try {
    if (-not (Test-Path $vswhere)) {
        throw "vswhere was not found at $vswhere."
    }
    $installation = & $vswhere -latest -products * `
        -version '[18.0,19.0)' -property installationPath
    if ([string]::IsNullOrWhiteSpace($installation)) {
        throw 'Visual Studio 2026 was not found.'
    }
    $devenv = Join-Path $installation 'Common7/IDE/devenv.exe'

    if (-not [string]::IsNullOrWhiteSpace($VsixPath)) {
        $resolvedVsix = (Resolve-Path $VsixPath).Path
        $vsixInstaller = Join-Path $installation 'Common7/IDE/VSIXInstaller.exe'
        if (-not (Test-Path $vsixInstaller)) {
            throw "VSIXInstaller was not found at $vsixInstaller."
        }
        Invoke-Checked $vsixInstaller @('/quiet', '/force', $resolvedVsix)
    }

    Push-Location $root
    Invoke-Checked dotnet @('build', 'samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj', '-c', 'Debug', '--nologo')
    Invoke-Checked dotnet @('build', 'samples/GameServer/Examples.GameServer.Plugin/Examples.GameServer.Plugin.csproj', '-c', 'Debug', '--nologo')
    Pop-Location

    $env:DOTBOXD_VSIX_DIAGNOSTIC_LOG = $launcherLog
    Start-Process $devenv -ArgumentList @((Join-Path $root 'DotBoxD.slnx'), '/log', $activityLog) -WorkingDirectory $root | Out-Null
    Wait-ForDte
    $env:DOTBOXD_E2E_GUARDIAN = $guardian
    $env:DOTBOXD_E2E_PLUGIN_PROGRAM = $pluginProgram
    Invoke-Dte @'
while ($dte.Debugger.Breakpoints.Count -gt 0) { $dte.Debugger.Breakpoints.Item(1).Delete() }
$null = $dte.Debugger.Breakpoints.Add('', $env:DOTBOXD_E2E_PLUGIN_PROGRAM, 46)
$dte.ExecuteCommand('Debug.Start')
'@ | Out-Null
    $managedStop = Wait-ForManagedStop
    Invoke-Dte @'
while ($dte.Debugger.Breakpoints.Count -gt 0) { $dte.Debugger.Breakpoints.Item(1).Delete() }
$null = $dte.Debugger.Breakpoints.Add('', $env:DOTBOXD_E2E_GUARDIAN, 35)
$null = $dte.Debugger.Breakpoints.Add('', $env:DOTBOXD_E2E_GUARDIAN, 44)
$dte.Debugger.Go($false)
'@ | Out-Null
    Wait-ForKernelAttach

    $predicate1 = Wait-ForStop 35
    Invoke-Dte '$dte.Debugger.StepOver($false)' | Out-Null
    $step = Wait-ForNextKernelStop
    Invoke-Dte '$dte.Debugger.Go($false)' | Out-Null
    $handle1 = Wait-ForStop 44
    Invoke-Dte '$dte.Debugger.Go($false)' | Out-Null
    $predicate2 = Wait-ForStop 35
    Invoke-Dte '$dte.Debugger.Go($false)' | Out-Null
    $handle2 = Wait-ForStop 44

    if ($predicate1.Function -eq $handle1.Function -or $predicate2.Function -eq $handle2.Function) {
        throw 'Where and Run stops did not preserve distinct kernel stack identities.'
    }
    Set-Content $resultLog "PASS: managed Program.cs:$($managedStop.Line), kernel step to $($step.Line), and kernel 35, 44, 35, 44."
    Write-Host "Visual Studio 2026 kernel debugger E2E passed: step to $($step.Line), then 44, 35, 44."
}
catch {
    Set-Content $resultLog ("FAIL: " + $_.Exception)
    throw
}
finally {
    while ((Get-Location).Path -ne $root -and (Get-Location).Path.StartsWith($root)) {
        Pop-Location
    }
    Stop-ExamplesAndVisualStudio
}
