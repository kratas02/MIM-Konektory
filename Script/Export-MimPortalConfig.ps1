# Export konfigurace MIM portalu ve stejnem rozsahu jako UZSVMSchema.xml / UZSVMPolicy.xml.
# Knihovny FIMAutomation se nactou z adresare vedle skriptu, takze export muze bezet
# z jineho pocitace proti vzdalenemu MIM Service. Ucet musi mit pravo cist schema a politiku.
#Requires -Version 5.1

param(
    [string] $Uri = "http://smd1mgidm02p:5725",
    [string] $OutputDir = "C:\Temp",
    [switch] $IncludePortalUi = $true
)

$ErrorActionPreference = "Stop"

$fimModule = Join-Path $PSScriptRoot "FIMAutomation\FIMAutomation.psd1"
if (-not (Test-Path -LiteralPath $fimModule)) {
    throw "Nenalezen modul FIMAutomation: $fimModule"
}
Import-Module $fimModule -ErrorAction Stop

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$schemaFile = Join-Path $OutputDir "Schema.xml"
$policyFile = Join-Path $OutputDir "Policy.xml"

Write-Host "Export schema -> $schemaFile"
$schema = Export-FIMConfig -Uri $Uri -schemaConfig -customConfig "/SynchronizationFilter"
if ($null -eq $schema) {
    throw "Export-FIMConfig -schemaConfig nevratil zadna data."
}
$schema | ConvertFrom-FIMResource -File $schemaFile

# -policyConfig uz SynchronizationFilter obsahuje. Stejny objekt v -customConfig
# cmdlet vlozi do slovniku podruhe a skonci chybou "An item with the same key has already been added".
Write-Host "Export policy -> $policyFile"
$policy = Export-FIMConfig -Uri $Uri -policyConfig
if ($null -eq $policy) {
    throw "Export-FIMConfig -policyConfig nevratil zadna data."
}
$policy = @($policy)

if ($IncludePortalUi) {
    $seen = @{}
    foreach ($item in $policy) {
        $seen[$item.ResourceManagementObject.ObjectIdentifier] = $true
    }

    $uiFilters = @(
        "/ObjectVisualizationConfiguration",
        "/NavigationBarConfiguration",
        "/SearchScopeConfiguration",
        "/HomepageConfiguration",
        "/PortalUIConfiguration",
        "/Configuration"
    )
    foreach ($xpath in $uiFilters) {
        Write-Host "Export $xpath"
        $extra = Export-FIMConfig -Uri $Uri -customConfig $xpath
        if ($null -eq $extra) {
            continue
        }
        foreach ($item in @($extra)) {
            $id = $item.ResourceManagementObject.ObjectIdentifier
            if ($seen.ContainsKey($id)) {
                continue
            }
            $seen[$id] = $true
            $policy += $item
        }
    }
}

$policy | ConvertFrom-FIMResource -File $policyFile

Write-Host "Hotovo."
Write-Host "  $schemaFile"
Write-Host "  $policyFile"
