#Requires -Version 5.1
<#
.SYNOPSIS
    Po importu presune MIM log (Create a log file) do archivu s datumem
    a orizne historii: 7 dni vse, 4 tydny 1x tydenne, 1 rok 1x mesicne.

.DESCRIPTION
    MIM run profil neumi datum v nazvu logu. Skript se vola az po dobehnuti
    importu. Zdrojovy soubor presune do ArchiveDirectory a zabali ho
    (CompressionLevel Optimal) jako <prefix>_yyyyMMdd-HHmmss<ext>.zip.
    Puvodni cesta zustane volna pro dalsi beh.

    Retence (GFS) plati jen na soubory se stejnym prefixem v archivu:
    - stari do 7 dni: nechat vse
    - stari 8-28 dni: jeden nejnovejsi soubor za ISO tyden
    - stari 29-365 dni: jeden nejnovejsi soubor za mesic
    - starsi nez 1 rok: smazat

.PARAMETER Path
    Plna cesta k logu z run profilu (Create a log file). Muze byt vic souboru.

.PARAMETER ArchiveDirectory
    Cilova slozka archivu.

.PARAMETER Prefix
    Prefix nazvu v archivu. Bez nej se vezme jmeno souboru bez pripony.

.EXAMPLE
    .\Archive-MaImportLog.ps1 -Path "C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\MaData\SAP_BAPI\import.xml"

.EXAMPLE
    Start-ManagementAgent -Name SAP_BAPI -RunProfile "DI"
    .\Archive-MaImportLog.ps1 -Path "D:\MALogs\SAP_BAPI.xml" -ArchiveDirectory "D:\MALogs\Archive" -Prefix SAP_BAPI
#>
param(
    [Parameter(Mandatory = $true)]
    [string[]] $Path,

    [string] $ArchiveDirectory = "C:\Batch\Logs\MAImport",

    [string] $Prefix
)

$ErrorActionPreference = "Stop"

function Get-IsoWeekKey([datetime] $When) {
    $cal = [Globalization.CultureInfo]::InvariantCulture.Calendar
    $week = $cal.GetWeekOfYear($When, [Globalization.CalendarWeekRule]::FirstFourDayWeek, [DayOfWeek]::Monday)
    return "{0:0000}-W{1:00}" -f $When.Year, $week
}

function Get-MonthKey([datetime] $When) {
    return "{0:yyyy-MM}" -f $When
}

function Invoke-LogRetention {
    param(
        [string] $Directory,
        [string] $NamePrefix,
        [string] $Extension
    )

    $now = Get-Date
    $pattern = $NamePrefix + "_*" + $Extension
    $files = @(Get-ChildItem -LiteralPath $Directory -File -Filter $pattern -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return }

    $keep = New-Object "System.Collections.Generic.HashSet[string]"
    $weekly = @{}
    $monthly = @{}

    foreach ($file in $files) {
        $age = $now - $file.LastWriteTime
        if ($age.TotalDays -le 7) {
            [void]$keep.Add($file.FullName)
        }
        elseif ($age.TotalDays -le 28) {
            $key = Get-IsoWeekKey $file.LastWriteTime
            if (-not $weekly.ContainsKey($key) -or $file.LastWriteTime -gt $weekly[$key].LastWriteTime) {
                $weekly[$key] = $file
            }
        }
        elseif ($age.TotalDays -le 365) {
            $key = Get-MonthKey $file.LastWriteTime
            if (-not $monthly.ContainsKey($key) -or $file.LastWriteTime -gt $monthly[$key].LastWriteTime) {
                $monthly[$key] = $file
            }
        }
    }

    foreach ($file in $weekly.Values) { [void]$keep.Add($file.FullName) }
    foreach ($file in $monthly.Values) { [void]$keep.Add($file.FullName) }

    foreach ($file in $files) {
        if ($keep.Contains($file.FullName)) { continue }
        Remove-Item -LiteralPath $file.FullName -Force
        Write-Host ("Smazano (retence): {0}" -f $file.Name)
    }
}

if (-not (Test-Path -LiteralPath $ArchiveDirectory)) {
    New-Item -ItemType Directory -Force -Path $ArchiveDirectory | Out-Null
}

foreach ($source in $Path) {
    if (-not (Test-Path -LiteralPath $source)) {
        Write-Host ("Log neni (import ho nevytvoril?): {0}" -f $source)
        continue
    }

    $item = Get-Item -LiteralPath $source
    $namePrefix = if ($Prefix) { $Prefix } else { $item.BaseName }
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $target = Join-Path $ArchiveDirectory ("{0}_{1}{2}" -f $namePrefix, $stamp, $item.Extension)

    $moved = $false
    foreach ($try in 1..6) {
        try {
            Move-Item -LiteralPath $item.FullName -Destination $target -Force
            $moved = $true
            break
        }
        catch {
            if ($try -eq 6) { throw }
            Start-Sleep -Seconds 2
        }
    }

    if ($moved) {
        $zipPath = $target + ".zip"
        Compress-Archive -LiteralPath $target -DestinationPath $zipPath -CompressionLevel Optimal -Force
        Remove-Item -LiteralPath $target -Force
        Write-Host ("Archivovano: {0} -> {1}" -f $item.Name, $zipPath)
        Invoke-LogRetention -Directory $ArchiveDirectory -NamePrefix $namePrefix -Extension ($item.Extension + ".zip")
        Invoke-LogRetention -Directory $ArchiveDirectory -NamePrefix $namePrefix -Extension $item.Extension
    }
}
