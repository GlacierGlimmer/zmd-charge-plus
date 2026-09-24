$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

$CatalogPath = Join-Path $Root 'Customization\VariableCatalog.cs'
$ProducerPaths = @(
    (Join-Path $Root 'Customization\VariableHub.cs'),
    (Join-Path $Root 'Customization\AdvancedVariableProvider.cs'),
    (Join-Path $Root 'Customization\HudProfileRenderer.cs')
)

foreach ($path in @($CatalogPath) + $ProducerPaths) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Variable coverage audit source file not found: $path"
    }
}

$Catalog = Get-Content -LiteralPath $CatalogPath -Raw
$ProducerText = ($ProducerPaths | ForEach-Object {
    Get-Content -LiteralPath $_ -Raw
}) -join "`n"

# Literal catalog variables: V("cpu.usage", ...)
$LiteralKeys = @(
    [regex]::Matches($Catalog, 'V\("([a-z][a-z0-9_.]+)"') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique
)

# A built-in counts as implemented only when it has an actual producer statement:
#   v["key"] = ...
#   result["key"] = ...
#   PutIfNumber(v, "key", ...)
#   PutText(v, "key", ...)
#   PutWorld(v, "key", ...)
$ProducedLiteralKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

$AssignmentMatches = [regex]::Matches(
    $ProducerText,
    '(?:v|vars|result)\["([a-z][a-z0-9_.]+)"\]\s*='
)
foreach ($m in $AssignmentMatches) {
    [void]$ProducedLiteralKeys.Add($m.Groups[1].Value)
}

$HelperMatches = [regex]::Matches(
    $ProducerText,
    '(?:PutIfNumber|PutText|PutWorld)\(\s*(?:v|vars|result)\s*,\s*"([a-z][a-z0-9_.]+)"'
)
foreach ($m in $HelperMatches) {
    [void]$ProducedLiteralKeys.Add($m.Groups[1].Value)
}

$MissingLiteral = @(
    $LiteralKeys | Where-Object { -not $ProducedLiteralKeys.Contains($_) }
)

# Dynamic fixed-drive variables are generated as disk.{letter}.<suffix>.
$DynamicDriveSuffixes = @(
    [regex]::Matches($Catalog, 'V\(\$"disk\.\{letter\}\.([a-z0-9_]+)"') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique
)

$MissingDynamic = New-Object 'System.Collections.Generic.List[string]'
foreach ($suffix in $DynamicDriveSuffixes) {
    $escaped = [regex]::Escape($suffix)

    # Core/advanced producer forms:
    #   v[$"disk.{letter}.suffix"] = ...
    #   v[p + "suffix"] = ...
    #   PutIfNumber(v, p + "suffix", ...)
    $hasDirectProducer = [regex]::IsMatch(
        $ProducerText,
        'v\[\$"disk\.\{letter\}\.' + $escaped + '"\]\s*='
    )

    $hasPrefixAssignment = [regex]::IsMatch(
        $ProducerText,
        'v\[p\s*\+\s*"' + $escaped + '"\]\s*='
    )

    $hasPrefixHelper = [regex]::IsMatch(
        $ProducerText,
        '(?:PutIfNumber|PutText)\(\s*v\s*,\s*p\s*\+\s*"' + $escaped + '"'
    )

    if (-not ($hasDirectProducer -or $hasPrefixAssignment -or $hasPrefixHelper)) {
        [void]$MissingDynamic.Add("disk.{letter}.$suffix")
    }
}

if ($MissingLiteral.Count -gt 0 -or $MissingDynamic.Count -gt 0) {
    Write-Host ''
    Write-Host 'Variable coverage audit FAILED.' -ForegroundColor Red

    if ($MissingLiteral.Count -gt 0) {
        Write-Host 'Built-in catalog variables without producer code:' -ForegroundColor Red
        $MissingLiteral | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    }

    if ($MissingDynamic.Count -gt 0) {
        Write-Host 'Dynamic drive variable templates without producer code:' -ForegroundColor Red
        $MissingDynamic | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    }

    throw 'Variable catalog contains variable(s) with no runtime producer implementation.'
}

Write-Host ("Variable coverage audit passed: {0} literal built-ins + {1} dynamic drive templates all have producer code." -f $LiteralKeys.Count, $DynamicDriveSuffixes.Count) -ForegroundColor Green
