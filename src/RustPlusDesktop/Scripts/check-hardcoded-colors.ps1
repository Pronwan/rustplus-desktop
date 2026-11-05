<#
.SYNOPSIS
    Fluent-UI hardcoded-color gate.

.DESCRIPTION
    Counts hardcoded hex colors (e.g. Background="#1A1D21") across the project's
    XAML and compares the total against a stored baseline. The build/CI fails if
    the count INCREASES, so the ~1,500 existing colors can only go down over time
    as they are migrated to theme tokens (see docs/UI-Review-Fluent-Audit.md).

    When you remove hardcoded colors, lower the baseline with -Update so the new,
    lower number becomes the ceiling.

.EXAMPLE
    pwsh Scripts/check-hardcoded-colors.ps1            # verify (used by CI)
    pwsh Scripts/check-hardcoded-colors.ps1 -Update    # re-baseline after cleanup
    pwsh Scripts/check-hardcoded-colors.ps1 -Report    # list per-file counts
#>
[CmdletBinding()]
param(
    [switch]$Update,
    [switch]$Report
)

$ErrorActionPreference = 'Stop'

# Project root = parent of this script's folder (…/RustPlusDesktop)
$projectRoot  = Split-Path -Parent $PSScriptRoot
$baselineFile = Join-Path $PSScriptRoot 'hardcoded-colors.baseline'

# Any attribute value that is a bare hex color literal: ="#RGB" .. ="#AARRGGBB"
$pattern = '="#[0-9A-Fa-f]{3,8}"'

$xamlFiles = Get-ChildItem -Path $projectRoot -Recurse -Filter *.xaml |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

$total = 0
$perFile = foreach ($f in $xamlFiles) {
    $count = ([regex]::Matches((Get-Content -Raw -LiteralPath $f.FullName), $pattern)).Count
    $total += $count
    if ($count -gt 0) {
        [pscustomobject]@{ Count = $count; File = $f.FullName.Substring($projectRoot.Length + 1) }
    }
}

if ($Report) {
    $perFile | Sort-Object Count -Descending | Format-Table -AutoSize | Out-String | Write-Host
}

Write-Host "Hardcoded hex colors in XAML: $total"

if ($Update) {
    Set-Content -LiteralPath $baselineFile -Value $total -NoNewline
    Write-Host "Baseline updated to $total."
    exit 0
}

if (-not (Test-Path $baselineFile)) {
    Set-Content -LiteralPath $baselineFile -Value $total -NoNewline
    Write-Host "No baseline found; created one at $total."
    exit 0
}

$baseline = [int](Get-Content -Raw -LiteralPath $baselineFile).Trim()

if ($total -gt $baseline) {
    Write-Host "::error::Hardcoded color count increased: $total > baseline $baseline." -ForegroundColor Red
    Write-Host "Use theme tokens (DynamicResource) instead of hex literals, or run -Update if the increase is intentional."
    exit 1
}

if ($total -lt $baseline) {
    Write-Host "Nice - count dropped from $baseline to $total. Run with -Update to lock in the new baseline." -ForegroundColor Green
}
else {
    Write-Host "OK - at baseline ($baseline)."
}
exit 0
