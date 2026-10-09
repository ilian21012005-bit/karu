$ErrorActionPreference = "Stop"
$destDir = Join-Path $PSScriptRoot "ffmpeg"
$exe = Join-Path $destDir "ffmpeg.exe"
$licenseDest = Join-Path $destDir "LICENSE.txt"
$noticeSrc = Join-Path $PSScriptRoot "..\third_party\ffmpeg\LICENSE.txt"

New-Item -ItemType Directory -Force -Path $destDir | Out-Null

if (-not (Test-Path $exe)) {
    $zip = Join-Path $PSScriptRoot "ffmpeg-win64.zip"
    # Build GPL (BtbN). Redistribute LICENSE/COPYING with the binary.
    $url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
    Write-Output "Downloading $url"
    curl.exe -L --retry 3 --retry-delay 2 -o $zip $url
    if (-not (Test-Path $zip) -or (Get-Item $zip).Length -lt 1MB) {
        throw "Telechargement FFmpeg echoue."
    }
    $extract = Join-Path $PSScriptRoot "extracted"
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $ffmpeg = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
    if (-not $ffmpeg) { throw "ffmpeg.exe introuvable dans l'archive." }
    Copy-Item $ffmpeg.FullName $exe -Force

    $upstreamLicenses = @("LICENSE", "LICENSE.md", "COPYING", "COPYING.LGPLv2.1", "COPYING.LGPLv3", "LICENSE.txt")
    foreach ($name in $upstreamLicenses) {
        $found = Get-ChildItem $extract -Recurse -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            Copy-Item $found.FullName (Join-Path $destDir $found.Name) -Force
        }
    }

    Remove-Item $zip -Force
    Remove-Item $extract -Recurse -Force
    Write-Output "Installed $exe"
}
else {
    Write-Output "FFmpeg already present: $exe"
}

if (Test-Path $noticeSrc) {
    Copy-Item $noticeSrc $licenseDest -Force
    Write-Output "Copied Karu FFmpeg notice -> $licenseDest"
}
