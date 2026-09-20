$ErrorActionPreference = "Stop"
$destDir = Join-Path $PSScriptRoot "ffmpeg"
$exe = Join-Path $destDir "ffmpeg.exe"
if (Test-Path $exe) {
    Write-Output "FFmpeg already present: $exe"
    exit 0
}

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$zip = Join-Path $PSScriptRoot "ffmpeg-win64.zip"
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
Write-Output "Downloading $url"
curl.exe -L --retry 3 --retry-delay 2 -o $zip $url
if (-not (Test-Path $zip) -or (Get-Item $zip).Length -lt 1MB) {
    throw "Téléchargement FFmpeg échoué."
}
$extract = Join-Path $PSScriptRoot "extracted"
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $extract -Force
$ffmpeg = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
if (-not $ffmpeg) { throw "ffmpeg.exe introuvable dans l'archive." }
Copy-Item $ffmpeg.FullName $exe -Force
Remove-Item $zip -Force
Remove-Item $extract -Recurse -Force
Write-Output "Installed $exe"
