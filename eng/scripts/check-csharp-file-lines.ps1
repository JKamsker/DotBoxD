param(
    [int] $WarnAt = 300,
    [int] $FailAt = 350,
    [int] $MaxFilesPerFolder = 15,
    [int] $MaxFilesInProjectFolder = 5,
    # Ratcheting budget for soft-limit (CE0002, > $WarnAt lines) files. -1 reads
    # `maxSoftLimitViolations` from .config/code-enforcer/code-enforcer.json.
    [int] $MaxSoftLimitViolations = -1
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, "../.."))
$justificationPath = [System.IO.Path]::Combine($root, ".config/code-enforcer/justifications.json")
$violations = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Normalize-PathText([string] $path) {
    return $path.Replace("\", "/").Trim("/")
}

function Join-RepoPath([string] $relativePath) {
    return [System.IO.Path]::Combine($root, $relativePath)
}

function Add-NormalizedPath($set, [string] $path) {
    if (-not [string]::IsNullOrWhiteSpace($path)) {
        [void] $set.Add((Normalize-PathText $path))
    }
}

function Add-JsonJustifications($set, [System.Text.Json.JsonElement] $entries, [string[]] $propertyNames) {
    if ($entries.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
        return
    }

    foreach ($entry in $entries.EnumerateArray()) {
        if ($entry.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
            Add-NormalizedPath $set $entry.GetString()
            continue
        }

        if ($entry.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            continue
        }

        foreach ($propertyName in $propertyNames) {
            $property = [System.Text.Json.JsonElement]::new()
            if ($entry.TryGetProperty($propertyName, [ref] $property) -and
                $property.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
                Add-NormalizedPath $set $property.GetString()
                break
            }
        }
    }
}

function Add-JustificationsFromFile(
    [string] $path,
    $justifiedFiles,
    $justifiedFolders,
    $justifiedRootFolders) {
    if (-not [System.IO.File]::Exists($path)) {
        return
    }

    $json = [System.IO.File]::ReadAllText($path)
    if ([string]::IsNullOrWhiteSpace($json)) {
        return
    }

    $document = [System.Text.Json.JsonDocument]::Parse($json)
    try {
        $rootElement = $document.RootElement
        $entries = [System.Text.Json.JsonElement]::new()
        if ($rootElement.TryGetProperty("files", [ref] $entries)) {
            Add-JsonJustifications $justifiedFiles $entries @("path", "file")
        }

        if ($rootElement.TryGetProperty("folders", [ref] $entries)) {
            Add-JsonJustifications $justifiedFolders $entries @("path", "folder")
        }

        if ($rootElement.TryGetProperty("rootFolders", [ref] $entries)) {
            Add-JsonJustifications $justifiedRootFolders $entries @("path", "folder", "rootFolder")
        }
    }
    finally {
        $document.Dispose()
    }
}

function Get-RepositoryCSharpFiles {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new("git")
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    [void] $startInfo.ArgumentList.Add("-C")
    [void] $startInfo.ArgumentList.Add($root)
    [void] $startInfo.ArgumentList.Add("ls-files")
    [void] $startInfo.ArgumentList.Add("--cached")
    [void] $startInfo.ArgumentList.Add("--others")
    [void] $startInfo.ArgumentList.Add("--exclude-standard")
    [void] $startInfo.ArgumentList.Add("--")
    [void] $startInfo.ArgumentList.Add("*.cs")
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $gitOutput = $process.StandardOutput.ReadToEnd()
        [void] $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitOutput)) {
            $gitFiles = $gitOutput -split "\r?\n"
            $files = [System.Collections.Generic.List[string]]::new()
            foreach ($file in $gitFiles) {
                if (-not [string]::IsNullOrWhiteSpace($file)) {
                    $files.Add((Normalize-PathText $file))
                }
            }

            return $files.ToArray()
        }
    }
    finally {
        $process.Dispose()
    }

    $fallback = [System.Collections.Generic.List[string]]::new()
    foreach ($file in [System.IO.Directory]::EnumerateFiles($root, "*.cs", [System.IO.SearchOption]::AllDirectories)) {
        $relative = [System.IO.Path]::GetRelativePath($root, $file)
        $normalized = Normalize-PathText $relative
        if ($normalized.Contains("/bin/") -or $normalized.Contains("/obj/") -or $normalized.Contains("/.git/")) {
            continue
        }

        $fallback.Add($normalized)
    }

    return $fallback.ToArray()
}

function Test-GeneratedFile([string] $relativePath) {
    $name = [System.IO.Path]::GetFileName($relativePath)
    if ($name -match "\.(g|generated|designer)\.cs$") {
        return $true
    }

    $fullPath = Join-RepoPath $relativePath
    if (-not [System.IO.File]::Exists($fullPath)) {
        return $true
    }

    $reader = [System.IO.File]::OpenText($fullPath)
    try {
        for ($i = 0; $i -lt 20; $i++) {
            $line = $reader.ReadLine()
            if ($null -eq $line) {
                break
            }

            if ($line.Contains("<auto-generated", [System.StringComparison]::Ordinal)) {
                return $true
            }
        }
    }
    finally {
        $reader.Dispose()
    }

    return $false
}

function Get-LineCount([string] $relativePath) {
    $count = 0
    foreach ($line in [System.IO.File]::ReadLines((Join-RepoPath $relativePath))) {
        $count++
    }

    return $count
}

function Get-Folder([string] $relativePath) {
    $folder = [System.IO.Path]::GetDirectoryName($relativePath)
    if ([string]::IsNullOrWhiteSpace($folder)) {
        return "."
    }

    return Normalize-PathText $folder
}

function Get-ProjectFolders {
    $folders = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($project in [System.IO.Directory]::EnumerateFiles($root, "*.csproj", [System.IO.SearchOption]::AllDirectories)) {
        $relative = Normalize-PathText ([System.IO.Path]::GetRelativePath($root, $project))
        if ($relative.Contains("/bin/") -or $relative.Contains("/obj/") -or $relative.Contains("/.git/")) {
            continue
        }

        $folder = [System.IO.Path]::GetDirectoryName($relative)
        if ([string]::IsNullOrWhiteSpace($folder)) {
            $folder = "."
        }

        [void] $folders.Add((Normalize-PathText $folder))
    }

    return $folders
}

function Read-SoftLimitBudget([int] $defaultBudget) {
    if ($defaultBudget -ge 0) {
        return $defaultBudget
    }

    $configPath = Join-RepoPath ".config/code-enforcer/code-enforcer.json"
    if (-not [System.IO.File]::Exists($configPath)) {
        return $defaultBudget
    }

    $document = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($configPath))
    try {
        $field = [System.Text.Json.JsonElement]::new()
        if ($document.RootElement.TryGetProperty("maxSoftLimitViolations", [ref] $field) -and
            $field.ValueKind -eq [System.Text.Json.JsonValueKind]::Number) {
            return $field.GetInt32()
        }
    }
    finally {
        $document.Dispose()
    }

    return $defaultBudget
}

$justifiedFiles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$justifiedFolders = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$justifiedRootFolders = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
Add-JustificationsFromFile $justificationPath $justifiedFiles $justifiedFolders $justifiedRootFolders

$csharpFiles = [System.Collections.Generic.List[string]]::new()
foreach ($file in Get-RepositoryCSharpFiles) {
    if (-not (Test-GeneratedFile $file)) {
        $csharpFiles.Add($file)
    }
}

$filesByFolder = @{}
foreach ($file in $csharpFiles) {
    $lineCount = Get-LineCount $file
    $hasJustification = $justifiedFiles.Contains($file)

    if ($lineCount -gt 500 -and -not $hasJustification) {
        $violations.Add("CE0003 $file has $lineCount lines and exceeds 500 lines without a justification.")
    }
    elseif ($lineCount -gt $FailAt -and -not $hasJustification) {
        $violations.Add("CE0001 $file has $lineCount lines, exceeding the hard limit of $FailAt.")
    }
    elseif ($lineCount -gt $WarnAt) {
        $warnings.Add("CE0002 $file has $lineCount lines, exceeding the soft limit of $WarnAt.")
    }

    $folder = Get-Folder $file
    if (-not $filesByFolder.ContainsKey($folder)) {
        $filesByFolder[$folder] = [System.Collections.Generic.List[string]]::new()
    }

    $filesByFolder[$folder].Add($file)
}

foreach ($folder in $filesByFolder.Keys) {
    $count = $filesByFolder[$folder].Count
    if ($count -gt $MaxFilesPerFolder -and -not $justifiedFolders.Contains($folder)) {
        $violations.Add("CE0004 $folder contains $count C# files, exceeding the folder limit of $MaxFilesPerFolder.")
    }
}

foreach ($projectFolder in Get-ProjectFolders) {
    $count = if ($filesByFolder.ContainsKey($projectFolder)) { $filesByFolder[$projectFolder].Count } else { 0 }
    if ($count -gt $MaxFilesInProjectFolder -and -not $justifiedRootFolders.Contains($projectFolder)) {
        $violations.Add("CE0005 $projectFolder contains a .csproj and $count C# files, exceeding the project-folder limit of $MaxFilesInProjectFolder.")
    }
}

foreach ($warning in $warnings) {
    [Console]::Error.WriteLine("WARNING: $warning")
}

# Soft-limit budget: the count of files over the soft cap (CE0002) is a ratcheting
# budget. It may shrink but never grow, so a new oversized file forces a split or a
# deliberate budget bump instead of silently accumulating soft-limit debt.
$softLimitCount = $warnings.Count
$budget = Read-SoftLimitBudget $MaxSoftLimitViolations
if ($budget -ge 0) {
    if ($softLimitCount -gt $budget) {
        $violations.Add("CE0006 soft-limit budget exceeded: $softLimitCount file(s) over $WarnAt lines, budget is $budget. Split a file under $WarnAt lines, or deliberately raise maxSoftLimitViolations in .config/code-enforcer/code-enforcer.json.")
    }
    elseif ($softLimitCount -lt $budget) {
        [Console]::Out.WriteLine("CodeEnforcer: soft-limit count ($softLimitCount) is below the budget ($budget). Lower maxSoftLimitViolations in .config/code-enforcer/code-enforcer.json to lock in the improvement.")
    }
}

if ($violations.Count -gt 0) {
    foreach ($violation in $violations) {
        [Console]::Error.WriteLine($violation)
    }

    throw "CodeEnforcer found $($violations.Count) violation(s)."
}

[Console]::Out.WriteLine("CodeEnforcer passed.")
