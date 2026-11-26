[CmdletBinding()]
param(
    [switch]$NoInstaller,
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root         = $PSScriptRoot
$Dist         = Join-Path $Root 'dist'
$InstallerDir = Join-Path $Root 'installer'

function Step([string]$m) { Write-Host "`n==> $m" -ForegroundColor Cyan }

function Remove-RepoPath {
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$path)
    $full = [IO.Path]::GetFullPath($path)
    $rootFull = [IO.Path]::GetFullPath($Root)
    if (-not $full.TrimEnd('\').StartsWith($rootFull.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing to delete '$full' (outside the repo)"
    }
    if ((Test-Path -LiteralPath $full) -and $PSCmdlet.ShouldProcess($full, 'Delete')) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()

Step 'Cleaning previous build output'
Remove-RepoPath $Dist
Remove-RepoPath (Join-Path $InstallerDir 'Output')

Step 'Publishing self-contained app (dotnet)'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet not found. Install the .NET 10 SDK (winget install Microsoft.DotNet.SDK.10)."
}
$fileVersion = if (($Version.Split('.')).Count -eq 3) { "$Version.0" } else { $Version }
& dotnet publish (Join-Path $Root 'src\Lychee.Gui') -c Release -p:Platform=x64 -o $Dist `
    -p:Version=$Version -p:AssemblyVersion=$fileVersion -p:FileVersion=$fileVersion --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $Dist 'Lychee.exe'))) { throw "Lychee.exe not found in $Dist." }

Copy-Item (Join-Path $Root 'LICENSE') (Join-Path $Dist 'LICENSE.txt')
Copy-Item (Join-Path $Root 'THIRD-PARTY-NOTICES.md') $Dist
Copy-Item (Join-Path $Root 'licenses') $Dist -Recurse

if ($NoInstaller) {
    Step 'Skipping installer (-NoInstaller)'
} else {
    Step 'Building Windows installer'
    & pwsh -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $InstallerDir 'build-installer.ps1') -AutoInstallInnoSetup -Version $Version
    if ($LASTEXITCODE -ne 0) { throw "installer build failed (exit $LASTEXITCODE)." }
}

$sw.Stop()
Step ('Build complete in {0:n0}s' -f $sw.Elapsed.TotalSeconds)
$distMB = [math]::Round(((Get-ChildItem $Dist -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host ("    dist\        {0} MB (install footprint)" -f $distMB) -ForegroundColor Green
$setup = Get-ChildItem (Join-Path $InstallerDir 'Output') -Filter '*.exe' -ErrorAction SilentlyContinue |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($setup) {
    Write-Host ("    installer    {0} ({1} MB)" -f $setup.FullName, [math]::Round($setup.Length / 1MB, 1)) -ForegroundColor Green
}
