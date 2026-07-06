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

function ConvertTo-RelativePath([string] $path) {
    return $path.Replace('\', '/').TrimStart('/')
}

function Convert-GlobToRegex([string] $glob) {
    $normalized = ConvertTo-RelativePath -path $glob
    $escaped = [regex]::Escape($normalized)
    $escaped = $escaped.Replace('\*\*', '.*')
    $escaped = $escaped.Replace('\*', '[^/]*')
    $escaped = $escaped.Replace('\?', '[^/]')
    return '^' + $escaped + '$'
}

function Test-GlobMatch([string] $relativePath, [string[]] $patterns) {
    foreach ($pattern in $patterns) {
        if ($relativePath -match (Convert-GlobToRegex -glob $pattern)) {
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

        $path = Get-RequiredText -Object $entry -PropertyName "path" -Context "$Context allowlist entry"
        $reason = Get-RequiredText -Object $entry -PropertyName "reason" -Context "$Context allowlist entry '$path'"
        $entries.Add([pscustomobject] @{
            Path = ConvertTo-RelativePath -path $path
            Reason = $reason
        })
    }

    return $entries
}

function Test-Allowlisted([string] $relativePath, $allowlist) {
    foreach ($entry in $allowlist) {
        if ($relativePath -match (Convert-GlobToRegex -glob $entry.Path)) {
            return $true
        }
    }

    return $false
}

function Test-SkippedDirectory([string] $path) {
    $normalized = ConvertTo-RelativePath -path ([System.IO.Path]::GetRelativePath($root, $path))
    return $normalized -match '(^|/)(\.git|bin|obj|artifacts|StrykerOutput)(/|$)'
}

function Remove-CommentText([string] $line, [ref] $inBlockComment) {
    $remaining = $line
    $result = [System.Text.StringBuilder]::new()

    while ($remaining.Length -gt 0) {
        if ($inBlockComment.Value) {
            $blockEnd = $remaining.IndexOf("*/", [System.StringComparison]::Ordinal)
            if ($blockEnd -lt 0) {
                return $result.ToString()
            }

            $remaining = $remaining.Substring($blockEnd + 2)
            $inBlockComment.Value = $false
            continue
        }

        $lineComment = $remaining.IndexOf("//", [System.StringComparison]::Ordinal)
        $blockStart = $remaining.IndexOf("/*", [System.StringComparison]::Ordinal)
        if ($lineComment -ge 0 -and ($blockStart -lt 0 -or $lineComment -lt $blockStart)) {
            [void] $result.Append($remaining.Substring(0, $lineComment))
            return $result.ToString()
        }

        if ($blockStart -lt 0) {
            [void] $result.Append($remaining)
            return $result.ToString()
        }

        [void] $result.Append($remaining.Substring(0, $blockStart))
        $remaining = $remaining.Substring($blockStart + 2)
        $inBlockComment.Value = $true
    }

    return $result.ToString()
}

if ($null -eq $policy.rules) {
    throw "Banned API policy must define a 'rules' array."
}

$rules = [System.Collections.Generic.List[object]]::new()
foreach ($rule in @($policy.rules)) {
    $name = Get-RequiredText -Object $rule -PropertyName "name" -Context "Banned API rule"
    $context = "Banned API rule '$name'"
    $forbiddenIn = Get-RequiredStringArray -Object $rule -PropertyName "forbiddenIn" -Context $context
    $reason = Get-RequiredText -Object $rule -PropertyName "reason" -Context $context
    $remediation = Get-RequiredText -Object $rule -PropertyName "remediation" -Context $context
    $allowlist = Get-Allowlist -Rule $rule -Context $context

    $symbols = [System.Collections.Generic.List[object]]::new()
    foreach ($symbol in @($rule.symbols)) {
        $symbolName = Get-RequiredText -Object $symbol -PropertyName "name" -Context "$context symbol"
        $pattern = Get-RequiredText -Object $symbol -PropertyName "pattern" -Context "$context symbol '$symbolName'"
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
    -not (Test-SkippedDirectory -path $_.DirectoryName)
}

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($file in $sourceFiles) {
    $relativePath = ConvertTo-RelativePath -path ([System.IO.Path]::GetRelativePath($root, $file.FullName))
    $activeRules = @($rules | Where-Object {
        (Test-GlobMatch -relativePath $relativePath -patterns $_.ForbiddenIn) -and
            -not (Test-Allowlisted -relativePath $relativePath -allowlist $_.Allowlist)
    })
    if ($activeRules.Count -eq 0) {
        continue
    }

    $inBlockComment = $false
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
        $lineNumber++
        $scannableLine = Remove-CommentText -line $line -inBlockComment ([ref] $inBlockComment)
        if ([string]::IsNullOrWhiteSpace($scannableLine)) {
            continue
        }

        foreach ($rule in $activeRules) {
            foreach ($symbol in $rule.Symbols) {
                if (-not $symbol.Regex.IsMatch($scannableLine)) {
                    continue
                }

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

if ($violations.Count -gt 0) {
    Write-Host "Banned API policy found $($violations.Count) violation(s):"
    foreach ($violation in $violations) {
        Write-Host $violation
    }
    throw "Banned API policy failed."
}

Write-Host "Banned API policy passed."
