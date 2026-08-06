param([switch]$Force)

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$raidData = Get-Content -Raw (Join-Path $projectDir 'Assets\Data\raid-data.json') | ConvertFrom-Json
$itemData = Get-Content -Raw (Join-Path $projectDir 'Assets\Data\rust-item-list.json') | ConvertFrom-Json
$targetDir = Join-Path $projectDir 'Assets\icons\raid-targets'
$tiers = @{ Twigs = 'twig'; Wood = 'wood'; Stone = 'stone'; Metal = 'metal'; TopTier = 'armored' }
$itemsById = @{}
$itemsByShortname = @{}
$downloads = @{}

foreach ($item in $itemData) {
    $itemsById[[string]$item.id] = $item
    $itemsByShortname[[string]$item.shortName] = $item
}

foreach ($target in $raidData.targets) {
    if ([string]$target.prefabName -like '*/building boat/*') { continue }

    $item = if ($null -ne $target.itemId) { $itemsById[[string]$target.itemId] } else { $null }
    if ($null -eq $item -and $target.itemShortname) { $item = $itemsByShortname[[string]$target.itemShortname] }
    if ($null -ne $item -and $item.iconUrl) {
        $downloads[[string]$item.shortName + '.png'] = [string]$item.iconUrl
        continue
    }

    $tier = $tiers[[string]$target.buildingTier]
    if ($tier -and $target.buildingSlug) {
        $fileName = "$tier-$($target.buildingSlug).webp"
        $downloads[$fileName] = "https://cdn.rusthelp.com/images/256/$fileName"
    }
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
$http = [System.Net.Http.HttpClient]::new()
$http.DefaultRequestHeaders.UserAgent.ParseAdd('RustPlusDesktop/1.0')
$downloaded = 0
$skipped = 0
try {
    foreach ($entry in $downloads.GetEnumerator() | Sort-Object Key) {
        $destination = Join-Path $targetDir $entry.Key
        if (-not $Force -and (Test-Path -LiteralPath $destination)) {
            $skipped++
            continue
        }

        $bytes = $http.GetByteArrayAsync($entry.Value).GetAwaiter().GetResult()
        [System.IO.File]::WriteAllBytes($destination, $bytes)
        $downloaded++
        Write-Progress -Activity 'Downloading raid target icons' -Status $entry.Key -PercentComplete (($downloaded + $skipped) * 100 / $downloads.Count)
    }
}
finally {
    $http.Dispose()
    Write-Progress -Activity 'Downloading raid target icons' -Completed
}

Write-Host "Raid target icons ready: $($downloads.Count) total, $downloaded downloaded, $skipped already present."


