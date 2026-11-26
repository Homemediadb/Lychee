[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Invoke-Step([string]$name, [scriptblock]$step) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $step
    if ($LASTEXITCODE -ne 0) { throw "$name failed" }
}

Invoke-Step 'Build' { dotnet build Lychee.sln -c Release -p:Platform=x64 --nologo -v q }
Invoke-Step 'Checks' { dotnet run --project tests/Lychee.Checks -c Release -p:Platform=x64 --no-build }
Invoke-Step 'Restore tools' { dotnet tool restore }
Invoke-Step 'C# formatting' { dotnet csharpier check . }
Invoke-Step 'XAML formatting' { dotnet xstyler -p -f ((git ls-files '*.xaml') -join ',') }

if (Get-Command Invoke-ScriptAnalyzer -ErrorAction Ignore) {
    Write-Host '==> PowerShell lint' -ForegroundColor Cyan
    $issues = git ls-files '*.ps1' | ForEach-Object { Invoke-ScriptAnalyzer $_ -Severity Error, Warning -ExcludeRule PSAvoidUsingWriteHost }
    if ($issues) {
        $issues | Format-Table -AutoSize
        throw 'PowerShell lint failed'
    }
}
else { Write-Warning 'PSScriptAnalyzer is not installed (Install-Module PSScriptAnalyzer -Scope CurrentUser), so the PowerShell lint was skipped' }

Write-Host 'All checks passed' -ForegroundColor Green
