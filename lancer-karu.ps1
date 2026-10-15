# Lance Karu (build Release + cwd correct pour LibVLC).
# Usage : powershell -File lancer-karu.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Get-Process Karu -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

dotnet build "$root\src\Karu.App\Karu.App.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$out = Join-Path $root "src\Karu.App\bin\Release\net8.0-windows10.0.19041.0"
$exe = Join-Path $out "Karu.exe"
if (-not (Test-Path $exe)) { throw "Exe introuvable: $exe" }

$lib = Join-Path $out "libvlc\win-x64"
if (-not (Test-Path $lib)) {
    Write-Warning "libvlc\win-x64 manquant - restore NuGet VideoLAN.LibVLC.Windows"
}

Write-Host "Lancement: $exe"
Start-Process -FilePath $exe -WorkingDirectory $out
