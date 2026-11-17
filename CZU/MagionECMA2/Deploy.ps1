#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Zkompiluje a nasadi OracleECMA2MA konektor na MIM Sync Service.
.PARAMETER MimServer
    Nazev nebo IP MIM serveru. Vychozi: MIM
.PARAMETER Build
    Pouze zkompiluje, nenahraje na server.
.EXAMPLE
    .\Deploy.ps1
.EXAMPLE
    .\Deploy.ps1 -MimServer MIM -Build
#>
param(
    [string] $MimServer = "MIM",
    [switch] $Build
)

$ErrorActionPreference = "Stop"
$ProjectDir    = "$PSScriptRoot\OracleECMA2MA"
$ExtensionsDir = "C:\Projects\IDM\OracleECMA2MA\Deploy"

Write-Host "=== Build OracleECMA2MA ===" -ForegroundColor Cyan
dotnet build "$ProjectDir\OracleECMA2MA.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build selhal." }

if ($Build) {
    Write-Host "Build OK - nasazeni preskoceno (-Build switch)." -ForegroundColor Yellow
    return
}

Write-Host "`n=== Deploy na $MimServer ===" -ForegroundColor Cyan

if (-not (Test-Path $ExtensionsDir)) {
    throw "Extensions slozka nenalezena: $ExtensionsDir"
}

$OutputDir = "$ProjectDir\bin\Release\net462"
$Files = @(
    "OracleECMA2MA.dll",
    "Oracle.ManagedDataAccess.dll"
)

foreach ($file in $Files) {
    $src = Join-Path $OutputDir $file
    if (Test-Path $src) {
        Copy-Item $src $ExtensionsDir -Force
        Write-Host "  Kopirovano: $file" -ForegroundColor Green
    } else {
        Write-Warning "  Soubor nenalezen (preskoceno): $file"
    }
}

Write-Host "`nDeploy dokoncen. Restartuj Forefront Identity Manager Synchronization Service." -ForegroundColor Green
Write-Host "  Restart-Service FIMSynchronizationService -Force" -ForegroundColor Yellow
