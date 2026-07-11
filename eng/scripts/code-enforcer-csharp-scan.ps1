function Get-CSharpLineForDeclarationScan(
    [string] $line,
    [ref] $inBlockComment,
    [ref] $inVerbatimString,
    [ref] $rawStringQuoteCount) {
    $builder = [System.Text.StringBuilder]::new($line.Length)
    $index = 0

    while ($index -lt $line.Length) {
        if ($rawStringQuoteCount.Value -gt 0) {
            $delimiter = '"' * $rawStringQuoteCount.Value
            $end = $line.IndexOf($delimiter, $index, [StringComparison]::Ordinal)
            if ($end -lt 0) {
                return $builder.ToString()
            }

            $index = $end + $rawStringQuoteCount.Value
            $rawStringQuoteCount.Value = 0
            continue
        }

        if ($inVerbatimString.Value) {
            if ($line[$index] -eq '"') {
                if ($index + 1 -lt $line.Length -and $line[$index + 1] -eq '"') {
                    $index += 2
                    continue
                }

                $inVerbatimString.Value = $false
            }

            $index++
            continue
        }

        if ($inBlockComment.Value) {
            $blockEnd = $line.IndexOf("*/", $index, [StringComparison]::Ordinal)
            if ($blockEnd -lt 0) {
                return $builder.ToString()
            }

            $index = $blockEnd + 2
            $inBlockComment.Value = $false
            continue
        }

        $current = $line[$index]
        $next = if ($index + 1 -lt $line.Length) { $line[$index + 1] } else { [char] 0 }

        if ($current -eq "/" -and $next -eq "/") {
            break
        }

        if ($current -eq "/" -and $next -eq "*") {
            $inBlockComment.Value = $true
            $index += 2
            continue
        }

        if ($current -eq '$' -or $current -eq '"') {
            $rawStringStart = Get-RawStringStart $line $index
            if ($rawStringStart.QuoteCount -gt 0) {
                $index = $rawStringStart.ContentStart
                $rawStringQuoteCount.Value = $rawStringStart.QuoteCount
                continue
            }
        }

        if ($current -eq '@' -or $current -eq '$' -or $current -eq '"') {
            $stringStart = Get-StringStart $line $index
            if ($stringStart.Kind -eq "Verbatim") {
                $index = $stringStart.ContentStart
                $inVerbatimString.Value = $true
                continue
            }

            if ($stringStart.Kind -eq "Normal") {
                $index = Skip-NormalLiteral $line $stringStart.ContentStart '"'
                continue
            }
        }

        if ($current -eq "'") {
            $index = Skip-NormalLiteral $line ($index + 1) "'"
            continue
        }

        [void] $builder.Append($current)
        $index++
    }

    return $builder.ToString()
}

function Get-RawStringStart([string] $line, [int] $start) {
    $quoteIndex = $start
    while ($quoteIndex -lt $line.Length -and $line[$quoteIndex] -eq "$") {
        $quoteIndex++
    }

    if ($quoteIndex -ge $line.Length -or $line[$quoteIndex] -ne '"') {
        return [pscustomobject]@{ QuoteCount = 0; ContentStart = $start }
    }

    $quoteCount = 0
    while ($quoteIndex + $quoteCount -lt $line.Length -and $line[$quoteIndex + $quoteCount] -eq '"') {
        $quoteCount++
    }

    if ($quoteCount -lt 3) {
        return [pscustomobject]@{ QuoteCount = 0; ContentStart = $start }
    }

    return [pscustomobject]@{
        QuoteCount = $quoteCount
        ContentStart = $quoteIndex + $quoteCount
    }
}

function Get-StringStart([string] $line, [int] $start) {
    $current = $line[$start]
    $next = if ($start + 1 -lt $line.Length) { $line[$start + 1] } else { [char] 0 }
    $third = if ($start + 2 -lt $line.Length) { $line[$start + 2] } else { [char] 0 }

    if ($current -eq '@' -and $next -eq '"') {
        return [pscustomobject]@{ Kind = "Verbatim"; ContentStart = $start + 2 }
    }

    if ($current -eq '@' -and $next -eq '$' -and $third -eq '"') {
        return [pscustomobject]@{ Kind = "Verbatim"; ContentStart = $start + 3 }
    }

    if ($current -eq '$') {
        $index = $start
        while ($index -lt $line.Length -and $line[$index] -eq '$') {
            $index++
        }

        if ($index + 1 -lt $line.Length -and $line[$index] -eq '@' -and $line[$index + 1] -eq '"') {
            return [pscustomobject]@{ Kind = "Verbatim"; ContentStart = $index + 2 }
        }

        if ($index -lt $line.Length -and $line[$index] -eq '"') {
            return [pscustomobject]@{ Kind = "Normal"; ContentStart = $index + 1 }
        }
    }

    if ($current -eq '"') {
        return [pscustomobject]@{ Kind = "Normal"; ContentStart = $start + 1 }
    }

    return [pscustomobject]@{ Kind = ""; ContentStart = $start }
}

function Skip-NormalLiteral([string] $line, [int] $start, [string] $quote) {
    $index = $start
    while ($index -lt $line.Length) {
        if ($line[$index] -eq "\") {
            $index += 2
            continue
        }

        if ($line[$index] -eq $quote) {
            return $index + 1
        }

        $index++
    }

    return $index
}

function Join-NamespaceName([string] $parentNamespace, [string] $declaredNamespace) {
    if ([string]::IsNullOrWhiteSpace($parentNamespace)) {
        return $declaredNamespace
    }

    if ([string]::IsNullOrWhiteSpace($declaredNamespace)) {
        return $parentNamespace
    }

    return "$parentNamespace.$declaredNamespace"
}

function Get-ActiveNamespace([System.Collections.Generic.List[object]] $namespaceScopes, [string] $fileScopedNamespace) {
    if ($namespaceScopes.Count -gt 0) {
        return $namespaceScopes[$namespaceScopes.Count - 1].Name
    }

    return $fileScopedNamespace
}

function Pop-ClosedNamespaces([System.Collections.Generic.List[object]] $namespaceScopes, [int] $braceDepth) {
    while ($namespaceScopes.Count -gt 0 -and $braceDepth -lt $namespaceScopes[$namespaceScopes.Count - 1].Depth) {
        $namespaceScopes.RemoveAt($namespaceScopes.Count - 1)
    }
}
