#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Zkompiluje UisECMA2MA. Nasazeni na MIM je rucni kopie DLL do Extensions.
.EXAMPLE
    .\Deploy.ps1
#>
$ErrorActionPreference = "Stop"
$Project = Join-Path $PSScriptRoot "UisECMA2MA\UisECMA2MA.csproj"

Write-Host "=== Build UisECMA2MA ===" -ForegroundColor Cyan
dotnet build $Project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build selhal." }

$dll = Join-Path $PSScriptRoot "UisECMA2MA\bin\Release\net462\UisECMA2MA.dll"
Write-Host "DLL: $dll" -ForegroundColor Green
Write-Host "Zkopiruj UisECMA2MA.dll a Oracle.ManagedDataAccess.dll do Extensions a restartuj FIMSynchronizationService." -ForegroundColor Yellow
Write-Host "DataSource je v C:\ProgramData\UisECMA2MA\DataSource.txt (soubor se zalozi pri prvnim pripojeni)." -ForegroundColor Yellow
