[CmdletBinding()]
param(
    [switch]$AutoInstallInnoSetup,

    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root    = $PSScriptRoot
$DistDir = Join-Path (Split-Path $Root -Parent) 'dist'
$IssFile = Join-Path $Root 'Lychee.iss'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
    foreach ($p in $candidates) { if (Test-Path $p) { return $p } }
    return $null
}

Write-Step 'Checking application payload (..\dist)'
if (-not (Test-Path (Join-Path $DistDir 'Lychee.exe'))) {
    throw "dist\ is incomplete - expected Lychee.exe under '$DistDir'. Run build.ps1 first."
}
Write-Host "    payload OK"

Write-Step 'Locating Inno Setup compiler (ISCC.exe)'
$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host "    ISCC.exe not found." -ForegroundColor Yellow
    $doInstall = $AutoInstallInnoSetup -or ((Read-Host "    Install Inno Setup 6 via winget now? [y/N]") -match '^[Yy]')
    if ($doInstall) {
        winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements
        $iscc = Find-Iscc
    }
    if (-not $iscc) { throw "Inno Setup compiler not found. Install it from https://jrsoftware.org/isdl.php (6.3 or newer)." }
}
Write-Host "    using $iscc"

Write-Step 'Compiling installer'
& $iscc "/DMyAppVersion=$Version" $IssFile
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

$out = Get-ChildItem (Join-Path $Root 'Output') -Filter '*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Step "Done"
if ($out) { Write-Host "    installer: $($out.FullName)" -ForegroundColor Green }
