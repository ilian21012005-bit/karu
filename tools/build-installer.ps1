#Requires -Version 5.1
param(
    [string]$Version = "1.0.0",
    [switch]$SkipFfmpegDownload,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishDir = Join-Path $root "artifacts\publish"
$installerOut = Join-Path $root "artifacts\installer"
$iss = Join-Path $root "installer\Karu.iss"
$appProj = Join-Path $root "src\ClipBuffer.App\ClipBuffer.App.csproj"

Write-Host "==> Karu installer build v$Version" -ForegroundColor Cyan

if (-not $SkipFfmpegDownload) {
    & (Join-Path $PSScriptRoot "download-ffmpeg.ps1")
}

$ffmpeg = Join-Path $PSScriptRoot "ffmpeg\ffmpeg.exe"
if (-not (Test-Path $ffmpeg)) {
    throw "ffmpeg.exe missing. Run tools/download-ffmpeg.ps1"
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerOut | Out-Null

$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
Write-Host "==> dotnet publish (self-contained=$selfContained)" -ForegroundColor Cyan

$asmVersion = "$Version.0"
dotnet publish $appProj `
    -c Release `
    -r win-x64 `
    --self-contained $selfContained `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -p:AssemblyVersion=$asmVersion `
    -p:FileVersion=$asmVersion `
    -p:InformationalVersion=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (-not (Test-Path (Join-Path $publishDir "Karu.exe"))) {
    throw "Karu.exe not found in $publishDir"
}

$publishedFfmpeg = Join-Path $publishDir "ffmpeg\ffmpeg.exe"
if (-not (Test-Path $publishedFfmpeg)) {
    New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "ffmpeg") | Out-Null
    Copy-Item $ffmpeg $publishedFfmpeg -Force
    $lic = Join-Path $root "third_party\ffmpeg\LICENSE.txt"
    if (Test-Path $lic) {
        Copy-Item $lic (Join-Path $publishDir "ffmpeg\LICENSE.txt") -Force
    }
}

function Get-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        (Join-Path $root "tools\innosetup\ISCC.exe")
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

$iscc = Get-Iscc
if (-not $iscc) {
    Write-Host "==> Downloading Inno Setup 6" -ForegroundColor Cyan
    $innoUrl = "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe"
    $innoSetup = Join-Path $env:TEMP "innosetup-karu-install.exe"
    $innoDir = Join-Path $root "tools\innosetup"
    curl.exe -L --retry 3 -o $innoSetup $innoUrl
    if (-not (Test-Path $innoSetup) -or (Get-Item $innoSetup).Length -lt 1MB) {
        throw "Inno Setup download failed"
    }
    New-Item -ItemType Directory -Force -Path $innoDir | Out-Null
    $p = Start-Process -FilePath $innoSetup -ArgumentList @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/DIR=`"$innoDir`""
    ) -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        Write-Warning "Inno installer exit $($p.ExitCode)"
    }
    $iscc = Get-Iscc
    if (-not $iscc) {
        throw "ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
    }
}

Write-Host "==> Compiling Inno Setup" -ForegroundColor Cyan
& $iscc `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$installerOut" `
    $iss

if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Get-ChildItem $installerOut -Filter "Karu-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $setup) { throw "Setup exe not found in $installerOut" }

$sizeMb = [math]::Round($setup.Length / 1MB, 1)
Write-Host ""
Write-Host "OK: $($setup.FullName)" -ForegroundColor Green
Write-Host "Size: $sizeMb MB"
