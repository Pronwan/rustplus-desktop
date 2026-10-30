param([switch]$Force)

# Downloads the RustHelp item icons the Base Builder belt and inventory show, so they ship with the
# app instead of being fetched at runtime. Builder items without one keep their original SVG.
$ErrorActionPreference = 'Stop'
$projectDir = Join-Path $PSScriptRoot '..\src\RustPlusDesktop'
$builderDir = Join-Path $PSScriptRoot '..\vendor\MapParser\Rust_Assets\building'
$buildingData = Get-Content -Raw (Join-Path $builderDir 'building_data.json') | ConvertFrom-Json
$builderIcons = Get-Content -Raw (Join-Path $builderDir 'icons.json') | ConvertFrom-Json
$targetDir = Join-Path $projectDir 'Assets\icons\builder-items'
# 128px keeps the inventory cards sharp; the 512px originals would be twenty times the size.
$iconUrl = 'https://cdn.rusthelp.com/cdn-cgi/image/width=128,format=png/images/public/{0}.png'

$shortnames = [System.Collections.Generic.SortedSet[string]]::new()
foreach ($name in $builderIcons.items.PSObject.Properties.Name) { [void]$shortnames.Add($name) }
foreach ($piece in $buildingData.pieces.PSObject.Properties.Value) {
    if ($piece.kind -ne 'block' -and $piece.item.shortName) { [void]$shortnames.Add([string]$piece.item.shortName) }
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
$http = [System.Net.Http.HttpClient]::new()
$http.DefaultRequestHeaders.UserAgent.ParseAdd('RustPlusDesktop/1.0')
$downloaded = 0
$skipped = 0
$missing = @()
try {
    foreach ($shortname in $shortnames) {
        $destination = Join-Path $targetDir "$shortname.png"
        if (-not $Force -and (Test-Path -LiteralPath $destination)) {
            $skipped++
            continue
        }

        $response = $http.GetAsync(($iconUrl -f [Uri]::EscapeDataString($shortname))).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            $missing += "$shortname ($([int]$response.StatusCode))"
            continue
        }

        [System.IO.File]::WriteAllBytes($destination, $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
        $downloaded++
        Write-Progress -Activity 'Downloading builder item icons' -Status $shortname -PercentComplete (($downloaded + $skipped + $missing.Count) * 100 / $shortnames.Count)
    }
}
finally {
    $http.Dispose()
    Write-Progress -Activity 'Downloading builder item icons' -Completed
}

Write-Host "Builder item icons ready: $($shortnames.Count) total, $downloaded downloaded, $skipped already present."
if ($missing.Count) { Write-Host "Not on RustHelp (SVG fallback): $($missing -join ', ')" }
