[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$Repo = "JawadYzbk/rust-genetics-lab",
    [string]$TargetDir = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Determine target directory (defaults to ../dist relative to script)
if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $TargetDir = Join-Path (Split-Path -Parent $scriptDir) "dist"
}

Write-Host "Target dist directory: $TargetDir"

$downloadUrl = if ($Version -eq "latest") {
    "https://github.com/$Repo/releases/latest/download/genetics-lab-dist.zip"
} else {
    $tag = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
    "https://github.com/$Repo/releases/download/$tag/genetics-lab-dist-$tag.zip"
}

$tempZip = Join-Path ([System.IO.Path]::GetTempPath()) "genetics-lab-dist-$([System.Guid]::NewGuid().ToString('N')).zip"
$tempExtract = Join-Path ([System.IO.Path]::GetTempPath()) "genetics-lab-extract-$([System.Guid]::NewGuid().ToString('N'))"

try {
    Write-Host "Downloading Genetics Lab assets from: $downloadUrl"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing

    Write-Host "Extracting downloaded assets..."
    Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force

    if (-not (Test-Path $TargetDir)) {
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    } else {
        Write-Host "Cleaning existing dist directory contents..."
        Get-ChildItem -Path $TargetDir -Recurse | Remove-Item -Recurse -Force
    }

    Write-Host "Deploying new dist assets into $TargetDir..."
    Copy-Item -Path "$tempExtract\*" -Destination $TargetDir -Recurse -Force

    Write-Host "Successfully updated Genetics Lab dist assets to $Version!" -ForegroundColor Green
}
finally {
    if (Test-Path $tempZip) { Remove-Item -Force $tempZip -ErrorAction SilentlyContinue }
    if (Test-Path $tempExtract) { Remove-Item -Recurse -Force $tempExtract -ErrorAction SilentlyContinue }
}
