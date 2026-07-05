param(
    [string] $PolicyPath = ".config/code-enforcer/banned-apis.json",
    [string] $RootPath = ""
)

$ErrorActionPreference = "Stop"

$root = if ([string]::IsNullOrWhiteSpace($RootPath)) {
    Resolve-Path (Join-Path $PSScriptRoot "../..")
} else {
    Resolve-Path $RootPath
}

$policyFile = if ([System.IO.Path]::IsPathRooted($PolicyPath)) {
    $PolicyPath
} else {
    Join-Path $root $PolicyPath
}

if (-not (Test-Path -LiteralPath $policyFile)) {
    throw "Banned API policy does not exist: $policyFile"
}

$policy = Get-Content -Raw -LiteralPath $policyFile | ConvertFrom-Json

function Normalize-RelativePath([string] $path) {
    return $path.Replace('\', '/').TrimStart('/')
}

function Convert-GlobToRegex([string] $glob) {
    $normalized = Normalize-RelativePath $glob
    $escaped = [regex]::Escape($normalized)
    $escaped = $escaped.Replace('\*\*', '.*')
    $escaped = $escaped.Replace('\*', '[^/]*')
    $escaped = $escaped.Replace('\?', '[^/]')
    return '^' + $escaped + '$'
}

function Test-GlobMatch([string] $relativePath, [string[]] $patterns) {
    foreach ($pattern in $patterns) {
        if ($relativePath -match (Convert-GlobToRegex $pattern)) {
            return $true
        }
    }

    return $false
}

function Get-RequiredText($Object, [string] $PropertyName, [string] $Context) {
    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string] $property.Value)) {
        throw "$Context must define '$PropertyName'."
    }

    return [string] $property.Value
}

function Get-RequiredStringArray($Object, [string] $PropertyName, [string] $Context) {
    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        throw "$Context must define '$PropertyName'."
    }

    $values = @($property.Value | ForEach-Object { [string] $_ } | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    })
    if ($values.Count -eq 0) {
        throw "$Context must define at least one '$PropertyName' entry."
    }

    return [string[]] $values
}

function Get-Allowlist($Rule, [string] $Context) {
    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in @($Rule.allowedIn)) {
        if ($entry -is [string]) {
            throw "$Context allowlist entry '$entry' must include a reason."
        }

        $path = Get-RequiredText $entry "path" "$Context allowlist entry"
        $reason = Get-RequiredText $entry "reason" "$Context allowlist entry '$path'"
        $entries.Add([pscustomobject] @{
            Path = Normalize-RelativePath $path
            Reason = $reason
        })
    }

    return $entries
}

function Test-Allowlisted([string] $relativePath, $allowlist) {
    foreach ($entry in $allowlist) {
        if ($relativePath -match (Convert-GlobToRegex $entry.Path)) {
            return $true
        }
    }

    return $false
}

function Test-SkippedDirectory([string] $path) {
    $normalized = Normalize-RelativePath ([System.IO.Path]::GetRelativePath($root, $path))
    return $normalized -match '(^|/)(\.git|bin|obj|artifacts|StrykerOutput)(/|$)'
}

if ($null -eq $policy.rules) {
    throw "Banned API policy must define a 'rules' array."
}

$rules = [System.Collections.Generic.List[object]]::new()
foreach ($rule in @($policy.rules)) {
    $name = Get-RequiredText $rule "name" "Banned API rule"
    $context = "Banned API rule '$name'"
    $forbiddenIn = Get-RequiredStringArray $rule "forbiddenIn" $context
    $reason = Get-RequiredText $rule "reason" $context
    $remediation = Get-RequiredText $rule "remediation" $context
    $allowlist = Get-Allowlist $rule $context

    $symbols = [System.Collections.Generic.List[object]]::new()
    foreach ($symbol in @($rule.symbols)) {
        $symbolName = Get-RequiredText $symbol "name" "$context symbol"
        $pattern = Get-RequiredText $symbol "pattern" "$context symbol '$symbolName'"
        try {
            $regex = [regex]::new($pattern)
        } catch {
            throw "$context symbol '$symbolName' has an invalid regex pattern: $($_.Exception.Message)"
        }

        $symbols.Add([pscustomobject] @{
            Name = $symbolName
            Regex = $regex
        })
    }

    if ($symbols.Count -eq 0) {
        throw "$context must define at least one symbol."
    }

    $rules.Add([pscustomobject] @{
        Name = $name
        ForbiddenIn = $forbiddenIn
        Allowlist = $allowlist
        Symbols = $symbols
        Reason = $reason
        Remediation = $remediation
    })
}

$sourceFiles = Get-ChildItem -Path $root -Recurse -Filter "*.cs" -File | Where-Object {
    -not (Test-SkippedDirectory $_.DirectoryName)
}

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($file in $sourceFiles) {
    $relativePath = Normalize-RelativePath ([System.IO.Path]::GetRelativePath($root, $file.FullName))
    foreach ($rule in $rules) {
        if (-not (Test-GlobMatch $relativePath $rule.ForbiddenIn)) {
            continue
        }

        if (Test-Allowlisted $relativePath $rule.Allowlist) {
            continue
        }

        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
            $lineNumber++
            $trimmed = $line.TrimStart()
            if ($trimmed.StartsWith("//", [System.StringComparison]::Ordinal) -or
                $trimmed.StartsWith("///", [System.StringComparison]::Ordinal) -or
                $trimmed.StartsWith("*", [System.StringComparison]::Ordinal)) {
                continue
            }

            foreach ($symbol in $rule.Symbols) {
                if ($symbol.Regex.IsMatch($line)) {
                    $violations.Add((
                        "{0}:{1}: {2} matched {3}. Reason: {4} Remediation: {5}" -f `
                            $relativePath,
                            $lineNumber,
                            $rule.Name,
                            $symbol.Name,
                            $rule.Reason,
                            $rule.Remediation))
                }
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Banned API policy found $($violations.Count) violation(s):"
    foreach ($violation in $violations) {
        Write-Host $violation
    }
    throw "Banned API policy failed."
}

Write-Host "Banned API policy passed."
