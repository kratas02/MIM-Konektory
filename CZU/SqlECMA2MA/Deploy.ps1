#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Zkompiluje a pripravi SqlECMA2MA konektor k nasazeni na MIM Sync Service.
.PARAMETER Build
    Pouze zkompiluje, dll nekopiruje.
.EXAMPLE
    .\Deploy.ps1 -Build
#>
param(
    [switch] $Build
)

$ErrorActionPreference = "Stop"
$ProjectDir = Join-Path $PSScriptRoot "SqlECMA2MA"
$StageDir   = Join-Path $PSScriptRoot "Deploy"

Write-Host "=== Build SqlECMA2MA ===" -ForegroundColor Cyan
dotnet build "$ProjectDir\SqlECMA2MA.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build selhal." }

if ($Build) {
    Write-Host "Build OK - nasazeni preskoceno (-Build switch)." -ForegroundColor Yellow
    return
}

if (-not (Test-Path $StageDir)) {
    New-Item -ItemType Directory -Path $StageDir | Out-Null
}

$src = Join-Path $ProjectDir "bin\Release\net462\SqlECMA2MA.dll"
if (-not (Test-Path $src)) { throw "DLL nenalezena: $src" }

Copy-Item $src $StageDir -Force
Write-Host "Kopirovano: $StageDir\SqlECMA2MA.dll" -ForegroundColor Green
Write-Host "Zkopiruj dll do Synchronization Service\Extensions a restartuj FIMSynchronizationService." -ForegroundColor Yellow
