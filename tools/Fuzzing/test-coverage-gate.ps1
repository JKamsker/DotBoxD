$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
$fixture = Join-Path ([IO.Path]::GetTempPath()) ("dotboxd-coverage-gate-" + [Guid]::NewGuid().ToString("N"))

function Invoke-Gate([bool] $ShouldPass) {
    $info = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in @("-NoProfile", "-File", (Join-Path $fixture "eng/scripts/check-coverage.ps1"))) {
        [void] $info.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $message = $output.GetAwaiter().GetResult() + $errorOutput.GetAwaiter().GetResult()
        if (($process.ExitCode -eq 0) -ne $ShouldPass) {
            throw "Unexpected gate exit $($process.ExitCode): $message"
        }
    } finally {
        $process.Dispose()
    }
}

try {
    foreach ($path in @("eng/scripts", ".config/code-enforcer", "artifacts/coverage")) {
        New-Item -ItemType Directory -Path (Join-Path $fixture $path) -Force | Out-Null
    }
    Copy-Item (Join-Path $root "eng/scripts/check-coverage.ps1") (Join-Path $fixture "eng/scripts/check-coverage.ps1")
    $configPath = Join-Path $fixture ".config/code-enforcer/coverage.json"
    $reportPath = Join-Path $fixture "artifacts/coverage/fixture.cobertura.xml"
    $bucket = @{
        name = "target"; packagePatterns = @("DotBoxD.Sample"); filePatterns = @("*/Target.cs")
        minimumLineCoverage = 95; minimumBranchCoverage = 95
    }
    $config = @{ minimumLineCoverage = 0; minimumBranchCoverage = 0; areas = @(); criticalAreas = @($bucket) }
    $config | ConvertTo-Json -Depth 5 | Set-Content $configPath
    # The Windows-style source path also checks slash normalization on Unix runners.
    $report = @'
<coverage><packages><package name="DotBoxD.Sample"><classes>
<class name="Target" filename="src\Sample\Target.cs"><lines>
<line number="1" hits="1" branch="True" condition-coverage="100% (2/2)" />
</lines></class>
<class name="Other" filename="src/Sample/Other.cs"><lines><line number="1" hits="0" /></lines></class>
</classes></package></packages></coverage>
'@
    Set-Content $reportPath $report
    Invoke-Gate $true
    Set-Content $reportPath ($report.Replace('100% (2/2)', '50% (1/2)'))
    Invoke-Gate $false
    Set-Content $reportPath ($report.Replace('hits="1"', 'hits="0"'))
    Invoke-Gate $false
    Set-Content $reportPath $report
    $bucket.filePatterns = @("*/Missing.cs")
    $config | ConvertTo-Json -Depth 5 | Set-Content $configPath
    Invoke-Gate $false
    $bucket.filePatterns = @()
    $config | ConvertTo-Json -Depth 5 | Set-Content $configPath
    Invoke-Gate $false
    $bucket.Remove("filePatterns")
    $config | ConvertTo-Json -Depth 5 | Set-Content $configPath
    Invoke-Gate $false # No file filter preserves package-wide coverage behavior.
    Set-Content $reportPath ($report.Replace('hits="0"', 'hits="1"'))
    Invoke-Gate $true
    Write-Host "Coverage file-filter gate tests passed."
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
