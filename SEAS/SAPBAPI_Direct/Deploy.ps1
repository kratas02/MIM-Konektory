#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Zkompiluje a nasadi SAPBAPI_Direct do Extensions slozky MIM Sync Service.
.PARAMETER ExtensionsDir
    Cilova Extensions slozka. Vychozi je lokalni instalace MIM.
.PARAMETER Build
    Pouze zkompiluje, nic nekopiruje.
.PARAMETER RestartService
    Po nasazeni restartuje FIMSynchronizationService.
.EXAMPLE
    .\Deploy.ps1 -Build
.EXAMPLE
    .\Deploy.ps1 -RestartService
#>
param(
    [string] $ExtensionsDir = "C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\Extensions",
    [switch] $Build,
    [switch] $RestartService
)

$ErrorActionPreference = "Stop"
$ProjectDir = "$PSScriptRoot\SAPBAPI_Direct"
$OutputDir  = "$ProjectDir\bin\Release\net48"

Write-Host "=== Build SAPBAPI_Direct ===" -ForegroundColor Cyan
dotnet build "$ProjectDir\SAPBAPI_Direct.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build selhal." }

if ($Build) {
    Write-Host "Build OK - nasazeni preskoceno (-Build)." -ForegroundColor Yellow
    return
}

if (-not (Test-Path $ExtensionsDir)) {
    throw "Extensions slozka nenalezena: $ExtensionsDir"
}

Write-Host "`n=== Deploy do $ExtensionsDir ===" -ForegroundColor Cyan

$src = Join-Path $OutputDir "SAPBAPI_Direct.dll"
if (-not (Test-Path $src)) { throw "Nenalezen vystup buildu: $src" }

Copy-Item $src $ExtensionsDir -Force
Write-Host "  Kopirovano: SAPBAPI_Direct.dll" -ForegroundColor Green

foreach ($dep in @("sapnco.dll", "sapnco_utils.dll")) {
    $depSrc = Join-Path $OutputDir $dep
    if (-not (Test-Path $depSrc)) { throw "Nenalezena zavislost: $depSrc" }
    Copy-Item $depSrc $ExtensionsDir -Force
    Write-Host "  Kopirovano: $dep" -ForegroundColor Green
}

if ($RestartService) {
    Write-Host "`nRestartuji FIMSynchronizationService..." -ForegroundColor Cyan
    Restart-Service FIMSynchronizationService -Force
    Write-Host "  Sluzba restartovana." -ForegroundColor Green
}
else {
    Write-Host "`nDeploy dokoncen. Restartuj synchronizacni sluzbu:" -ForegroundColor Green
    Write-Host "  Restart-Service FIMSynchronizationService -Force" -ForegroundColor Yellow
}
