param(
    [TimeSpan] $StartupTimeout = [TimeSpan]::FromMinutes(2),
    [TimeSpan] $StopTimeout = [TimeSpan]::FromSeconds(30),
    [string] $VsixPath,
    [switch] $InstallVsixForAllUsers
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifacts = Join-Path $root 'artifacts/vs26-e2e'
$activityLog = Join-Path $artifacts 'ActivityLog.xml'
$launcherLog = Join-Path $artifacts 'launcher.log'
$resultLog = Join-Path $artifacts 'result.txt'
$continuousStartGate = Join-Path $artifacts 'continuous-start.gate'
$guardian = Join-Path $root 'samples/GameServer/Examples.GameServer.Plugin/Kernels/GuardianKernel.cs'
$managedProgram = Join-Path $root 'samples/GameServer/Examples.GameServer.Server/ContinuousSimulation.cs'
$serverProject = 'samples\GameServer\Examples.GameServer.Server\Examples.GameServer.Server.csproj'
$pluginProject = 'samples\GameServer\Examples.GameServer.Plugin\Examples.GameServer.Plugin.csproj'
$debugProfiles = @(
    [PSCustomObject]@{ Project = $serverProject; Profile = 'GameServer - Wait for Plugin (Debug)' },
    [PSCustomObject]@{ Project = $pluginProject; Profile = 'GameServer Plugin (Debug)' }
)
$profileBackups = @{}
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
    $result = @(& $windowsPowerShell -NoProfile -NonInteractive -Command @"
`$ErrorActionPreference = 'Stop'
`$dte = [Runtime.InteropServices.Marshal]::GetActiveObject('VisualStudio.DTE.18.0')
$Script
"@ 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Visual Studio automation command failed: $($result -join [Environment]::NewLine)"
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

function Set-ProjectDebugProfiles {
    foreach ($item in $debugProfiles) {
        $userFile = Join-Path $root ($item.Project + '.user')
        $backup = $null
        if (Test-Path $userFile) {
            $backup = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N') + '.csproj.user')
            Copy-Item -LiteralPath $userFile -Destination $backup
        }
        $profileBackups[$userFile] = $backup
        Set-Content -LiteralPath $userFile -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <ActiveDebugProfile>$($item.Profile)</ActiveDebugProfile>
  </PropertyGroup>
</Project>
"@
    }
}

function Restore-ProjectDebugProfiles {
    foreach ($entry in $profileBackups.GetEnumerator()) {
        if ($null -ne $entry.Value) {
            Copy-Item -LiteralPath $entry.Value -Destination $entry.Key -Force
            Remove-Item -LiteralPath $entry.Value -Force
        }
        else {
            Remove-Item -LiteralPath $entry.Key -Force -ErrorAction SilentlyContinue
        }
    }
}

function Wait-ForDte {
    $deadline = [DateTime]::UtcNow + $StartupTimeout
    $stableSamples = 0
    do {
        Start-Sleep -Milliseconds 500
        try {
            $ready = Invoke-Dte @'
if ($dte.Solution.IsOpen -and
    $null -ne $dte.Debugger -and
    $null -ne $dte.Debugger.Breakpoints -and
    $null -ne $dte.Solution.SolutionBuild -and
    $dte.Commands.Item('Debug.Start').IsAvailable) {
    'ready'
}
'@ 2>$null
        }
        catch {
            $ready = $null
        }
        $stableSamples = if ($ready -contains 'ready') { $stableSamples + 1 } else { 0 }
    } until ($stableSamples -ge 2 -or [DateTime]::UtcNow -ge $deadline)
    if ($stableSamples -lt 2) {
        throw "Visual Studio did not open DotBoxD within $StartupTimeout."
    }
}

function Invoke-DteWhenReady([string] $Script, [string] $Operation) {
    $deadline = [DateTime]::UtcNow + $StartupTimeout
    $lastError = $null
    do {
        try {
            return Invoke-Dte $Script
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds 500
        }
    } until ([DateTime]::UtcNow -ge $deadline)

    throw "$Operation did not become ready within $StartupTimeout. Last error: $lastError"
}

function Wait-ForSolutionLoad {
    $deadline = [DateTime]::UtcNow + $StartupTimeout
    do {
        Start-Sleep -Milliseconds 500
        $loaded = (Test-Path $activityLog) -and
            (Select-String $activityLog -Pattern 'End execution cost summary for SolutionLoad scenario' -SimpleMatch -Quiet)
    } until ($loaded -or [DateTime]::UtcNow -ge $deadline)
    if (-not $loaded) {
        throw "Visual Studio did not finish loading DotBoxD within $StartupTimeout."
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
    } until (($stop -and [string]::Equals($stop.File, $managedProgram, [StringComparison]::OrdinalIgnoreCase)) -or
        [DateTime]::UtcNow -ge $deadline)
    if (-not $stop -or -not [string]::Equals($stop.File, $managedProgram, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Expected a managed server Program.cs stop. Last stop: $($stop | ConvertTo-Json -Compress)."
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

function Wait-ForCompanionProcesses {
    $expected = @('Examples.GameServer.Server', 'Examples.GameServer.Plugin', 'DotBoxD.DebugAdapter')
    $deadline = [DateTime]::UtcNow + $StopTimeout
    do {
        Start-Sleep -Milliseconds 200
        $processes = @(Get-CimInstance Win32_Process | ForEach-Object {
            if ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -and
                $_.Name -in @('Examples.GameServer.Server.exe', 'Examples.GameServer.Plugin.exe')) {
                [PSCustomObject]@{
                    Name = [IO.Path]::GetFileNameWithoutExtension($_.Name)
                    ProcessId = $_.ProcessId
                }
            }
            elseif ($_.CommandLine -and $_.CommandLine.Contains('DotBoxD.DebugAdapter.dll', [StringComparison]::OrdinalIgnoreCase)) {
                [PSCustomObject]@{ Name = 'DotBoxD.DebugAdapter'; ProcessId = $_.ProcessId }
            }
        })
        $found = @($processes.Name | Select-Object -Unique)
    } until (($expected | Where-Object { $_ -notin $found }).Count -eq 0 -or [DateTime]::UtcNow -ge $deadline)
    $missing = @($expected | Where-Object { $_ -notin $found })
    if ($missing.Count -gt 0) {
        throw "Visual Studio did not launch companion process(es): $($missing -join ', '). Found: $($found -join ', ')."
    }
    return $processes | Where-Object Name -In $expected
}

function Get-KernelIdeState {
    $json = Invoke-DteWhenReady @'
$frame = $dte.Debugger.CurrentStackFrame
if ($null -eq $frame) { throw 'Visual Studio did not expose the stopped kernel stack frame.' }
$document = $dte.ActiveDocument
if ($null -eq $document) { throw 'Visual Studio did not activate the stopped kernel document.' }
$breakpoints = @($dte.Debugger.Breakpoints | ForEach-Object {
    [PSCustomObject]@{ File = $_.File; Line = $_.FileLine; Enabled = $_.Enabled }
})
[PSCustomObject]@{
    Function = $frame.FunctionName
    File = $document.FullName
    Line = $document.Selection.CurrentLine
    Breakpoints = $breakpoints
} | ConvertTo-Json -Depth 3 -Compress
'@ 'Visual Studio kernel source state'
    return ($json | Where-Object { $_ } | Select-Object -Last 1) | ConvertFrom-Json
}

function Assert-KernelIdeState($State, [string] $Function, [int] $Line) {
    if (-not [string]::Equals($State.File, $guardian, [StringComparison]::OrdinalIgnoreCase) -or
        $State.Line -ne $Line -or $State.Function -ne $Function) {
        throw "Visual Studio exposed an unexpected kernel frame: $($State | ConvertTo-Json -Depth 3 -Compress)."
    }
    $breakpoints = @($State.Breakpoints)
    $lines = @($breakpoints | Where-Object Enabled | ForEach-Object Line | Sort-Object)
    if ($breakpoints.Count -ne 2 -or ($lines -join ',') -ne '35,44' -or
        @($breakpoints | Where-Object { -not [string]::Equals($_.File, $guardian, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw "Visual Studio did not retain both enabled kernel breakpoints: $($breakpoints | ConvertTo-Json -Compress)."
    }
}

function Wait-ForCompanionExit([int[]] $ProcessIds) {
    $deadline = [DateTime]::UtcNow + $StopTimeout
    do {
        Start-Sleep -Milliseconds 200
        $remaining = @($ProcessIds | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    } until ($remaining.Count -eq 0 -or [DateTime]::UtcNow -ge $deadline)
    if ($remaining.Count -gt 0) {
        throw "Visual Studio left companion process(es) running after Stop: $($remaining -join ', ')."
    }
}

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
Remove-Item $activityLog, $launcherLog, $resultLog, $continuousStartGate -Force -ErrorAction SilentlyContinue
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
        if ($InstallVsixForAllUsers) {
            $extensionsRoot = [IO.Path]::GetFullPath((Join-Path $installation 'Common7/IDE/Extensions'))
            $extensionDirectory = [IO.Path]::GetFullPath((Join-Path $extensionsRoot 'DotBoxD/KernelDebug'))
            $expectedPrefix = $extensionsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $extensionDirectory.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to stage the VSIX outside $extensionsRoot."
            }

            Remove-Item -LiteralPath $extensionDirectory -Recurse -Force -ErrorAction SilentlyContinue
            [IO.Compression.ZipFile]::ExtractToDirectory($resolvedVsix, $extensionDirectory)
            Invoke-Checked (Join-Path $installation 'Common7/IDE/devenv.com') @('/UpdateConfiguration')
        }
        else {
            $vsixInstaller = Join-Path $installation 'Common7/IDE/VSIXInstaller.exe'
            if (-not (Test-Path $vsixInstaller)) {
                throw "VSIXInstaller was not found at $vsixInstaller."
            }
            Invoke-Checked $vsixInstaller @('/quiet', '/force', $resolvedVsix)
        }
    }

    Set-ProjectDebugProfiles

    Push-Location $root
    Invoke-Checked dotnet @('build', 'samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj', '-c', 'Debug', '--nologo')
    Invoke-Checked dotnet @('build', 'samples/GameServer/Examples.GameServer.Plugin/Examples.GameServer.Plugin.csproj', '-c', 'Debug', '--nologo')
    Pop-Location

    $env:DOTBOXD_VSIX_DIAGNOSTIC_LOG = $launcherLog
    $env:DOTBOXD_E2E_CONTINUOUS_START_GATE = $continuousStartGate
    Start-Process $devenv -ArgumentList @((Join-Path $root 'DotBoxD.slnx'), '/log', $activityLog) -WorkingDirectory $root | Out-Null
    Wait-ForDte
    Wait-ForSolutionLoad
    $env:DOTBOXD_E2E_GUARDIAN = $guardian
    $env:DOTBOXD_E2E_MANAGED_PROGRAM = $managedProgram
    $env:DOTBOXD_E2E_SERVER_PROJECT = $serverProject
    $env:DOTBOXD_E2E_PLUGIN_PROJECT = $pluginProject
    Invoke-DteWhenReady @'
$solutionBuild = $dte.Solution.SolutionBuild
if ($null -eq $solutionBuild) { throw 'Visual Studio did not expose SolutionBuild after startup.' }
$breakpoints = $dte.Debugger.Breakpoints
if ($null -eq $breakpoints) { throw 'Visual Studio did not expose the debugger breakpoint collection after startup.' }
$contexts = @($solutionBuild.ActiveConfiguration.SolutionContexts)
$serverContext = $contexts | Where-Object {
    [string]::Equals($_.ProjectName, $env:DOTBOXD_E2E_SERVER_PROJECT, [StringComparison]::OrdinalIgnoreCase)
} | Select-Object -First 1
$pluginContext = $contexts | Where-Object {
    [string]::Equals($_.ProjectName, $env:DOTBOXD_E2E_PLUGIN_PROJECT, [StringComparison]::OrdinalIgnoreCase)
} | Select-Object -First 1
if ($null -eq $serverContext -or $null -eq $pluginContext) {
    throw 'Visual Studio has not configured both GameServer startup projects.'
}
$solutionBuild.StartupProjects = [object[]]@(
    $serverContext.ProjectName,
    $pluginContext.ProjectName)
$startupProjects = @($solutionBuild.StartupProjects)
if ($startupProjects.Count -ne 2) {
    throw "Visual Studio accepted $($startupProjects.Count) GameServer startup projects instead of 2."
}
while ($breakpoints.Count -gt 0) { $breakpoints.Item(1).Delete() }
$dte.ExecuteCommand('Debug.Start')
'@ 'Visual Studio debugger startup' | Out-Null
    Wait-ForKernelAttach
    Invoke-DteWhenReady @'
while ($dte.Debugger.Breakpoints.Count -gt 0) { $dte.Debugger.Breakpoints.Item(1).Delete() }
$null = $dte.Debugger.Breakpoints.Add('', $env:DOTBOXD_E2E_MANAGED_PROGRAM, 36)
'@ 'Visual Studio managed breakpoint setup' | Out-Null
    Set-Content $continuousStartGate 'ready'
    $managedStop = Wait-ForManagedStop
    $companionProcesses = @(Wait-ForCompanionProcesses)
    Invoke-DteWhenReady @'
while ($dte.Debugger.Breakpoints.Count -gt 0) { $dte.Debugger.Breakpoints.Item(1).Delete() }
$null = $dte.Debugger.Breakpoints.Add('', $env:DOTBOXD_E2E_GUARDIAN, 35)
$null = $dte.Debugger.Breakpoints.Add('', $env:DOTBOXD_E2E_GUARDIAN, 44)
$dte.Debugger.Go($false)
'@ 'Visual Studio kernel breakpoint setup' | Out-Null

    $predicate1 = Wait-ForStop 35
    $predicateIdeState = Get-KernelIdeState
    Assert-KernelIdeState $predicateIdeState $predicate1.Function 35
    Invoke-DteWhenReady '$dte.Debugger.StepOver($false)' 'Visual Studio Step Over' | Out-Null
    $step = Wait-ForNextKernelStop
    Invoke-DteWhenReady '$dte.Debugger.Go($false)' 'Visual Studio Continue' | Out-Null
    $handle1 = Wait-ForStop 44
    Invoke-DteWhenReady '$dte.Debugger.Go($false)' 'Visual Studio Continue' | Out-Null
    $predicate2 = Wait-ForStop 35
    Invoke-DteWhenReady '$dte.Debugger.Go($false)' 'Visual Studio Continue' | Out-Null
    $handle2 = Wait-ForStop 44
    $handleIdeState = Get-KernelIdeState
    Assert-KernelIdeState $handleIdeState $handle2.Function 44

    if ($predicate1.Function -eq $handle1.Function -or $predicate2.Function -eq $handle2.Function) {
        throw 'Where and Run stops did not preserve distinct kernel stack identities.'
    }
    Invoke-DteWhenReady '$dte.Debugger.Stop($true)' 'Visual Studio debugger stop' | Out-Null
    Wait-ForCompanionExit @($companionProcesses.ProcessId)
    Set-Content $resultLog "PASS: managed Program.cs:$($managedStop.Line); processes server/plugin/adapter; DTE frames and breakpoints at kernel 35/44; kernel step to $($step.Line); clean debugger shutdown."
    Write-Host "Visual Studio 2026 kernel debugger E2E passed: verified DTE frames/breakpoints, stepped to $($step.Line), and stopped all companion processes."
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
    Restore-ProjectDebugProfiles
    Remove-Item Env:DOTBOXD_E2E_CONTINUOUS_START_GATE -ErrorAction SilentlyContinue
    Remove-Item $continuousStartGate -Force -ErrorAction SilentlyContinue
}
