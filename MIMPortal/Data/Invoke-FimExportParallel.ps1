#Requires -Version 5.1
$ErrorActionPreference = "Stop"

. C:\Batch\FIMAutomation\FIMPowerShell.ps1

$OutputDirectory = "C:\Temp\FimExport"
$ThrottleLimit = 2
$stamp = Get-Date -Format "yyyyMMdd"
$FimModule = "C:\Batch\FIMAutomation\FIMAutomation.psd1"

Write-Host "Nacitam ObjectTypeDescription..."
$ObjectTypes = @(
    GetMIMObject "/ObjectTypeDescription" |
        ForEach-Object { $_.Name } |
        Where-Object { $_ } |
        Sort-Object -Unique
)
if ($ObjectTypes.Count -eq 0) {
    throw "Z ObjectTypeDescription se nepodarilo precist zadny typ."
}
Write-Host ("Nalezeno typu: {0}" -f $ObjectTypes.Count)
$ObjectTypes | ForEach-Object { Write-Host ("  /{0}" -f $_) }

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
}

# Job nesmi volat FIMPowerShell.ps1 - ten kazde nacteni prepisuje FIMAutomation.psd1.
$worker = {
    param($Type, $Uri, $OutDir, $Stamp, $ModulePath)

    $ErrorActionPreference = "Stop"
    $started = Get-Date
    Import-Module $ModulePath -Force

    try {
        $filter = "/" + $Type
        $objects = @()
        $raw = @(Export-FIMConfig -onlyBaseResources -customconfig $filter -Uri $Uri)
        foreach ($item in $raw) {
            if (-not $item) { continue }
            $ps = New-Object PSObject
            foreach ($attr in $item.ResourceManagementObject.ResourceManagementAttributes) {
                if ($attr.IsMultiValue) {
                    $ps | Add-Member NoteProperty $attr.AttributeName $attr.Values
                }
                else {
                    $ps | Add-Member NoteProperty $attr.AttributeName $attr.Value
                }
            }
            $objects += $ps
        }

        $file = Join-Path $OutDir ("{0}_{1}.xml" -f $Type.ToLowerInvariant(), $Stamp)
        $objects | Export-Clixml -LiteralPath $file -Depth 6

        [pscustomobject]@{
            Type    = $Type
            Count   = $objects.Count
            File    = $file
            Error   = $null
            Started = $started
            Ended   = Get-Date
            Pid     = $PID
        }
    }
    catch {
        [pscustomobject]@{
            Type    = $Type
            Count   = 0
            File    = $null
            Error   = $_.Exception.Message
            Started = $started
            Ended   = Get-Date
            Pid     = $PID
        }
    }
}

function Export-FimTypeOnce {
    param($Type, $Uri, $OutDir, $Stamp)

    $started = Get-Date
    $filter = "/" + $Type
    $objects = @()
    $raw = @(Export-FIMConfig -onlyBaseResources -customconfig $filter -Uri $Uri)
    foreach ($item in $raw) {
        if (-not $item) { continue }
        $ps = New-Object PSObject
        foreach ($attr in $item.ResourceManagementObject.ResourceManagementAttributes) {
            if ($attr.IsMultiValue) {
                $ps | Add-Member NoteProperty $attr.AttributeName $attr.Values
            }
            else {
                $ps | Add-Member NoteProperty $attr.AttributeName $attr.Value
            }
        }
        $objects += $ps
    }

    $file = Join-Path $OutDir ("{0}_{1}.xml" -f $Type.ToLowerInvariant(), $Stamp)
    $objects | Export-Clixml -LiteralPath $file -Depth 6

    [pscustomobject]@{
        Type    = $Type
        Count   = $objects.Count
        File    = $file
        Error   = $null
        Started = $started
        Ended   = Get-Date
        Pid     = $PID
    }
}

$jobs = @()
$results = @()
try {
    foreach ($type in $ObjectTypes) {
        while (@(Get-Job -State Running).Count -ge $ThrottleLimit) {
            Start-Sleep -Milliseconds 400
        }
        $jobs += Start-Job -Name $type -ScriptBlock $worker -ArgumentList $type, $DefaultUri, $OutputDirectory, $stamp, $FimModule
        Write-Host ("{0:HH:mm:ss} start  {1}  (bezi {2})" -f (Get-Date), $type, @(Get-Job -State Running).Count)
    }

    while (@(Get-Job -State Running).Count -gt 0) {
        $running = (Get-Job -State Running | Select-Object -ExpandProperty Name) -join ", "
        Write-Host ("{0:HH:mm:ss} bezi   {1}" -f (Get-Date), $running)
        Start-Sleep -Seconds 2
    }

    $results = @($jobs | Receive-Job)
}
finally {
    $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
}

$failed = @($results | Where-Object { $_.Error })
if ($failed.Count -gt 0) {
    Write-Host ("Opakuji serove {0} typu s chybou..." -f $failed.Count)
    $retried = @()
    foreach ($item in $failed) {
        Write-Host ("{0:HH:mm:ss} retry  {1}" -f (Get-Date), $item.Type)
        try {
            $retried += Export-FimTypeOnce -Type $item.Type -Uri $DefaultUri -OutDir $OutputDirectory -Stamp $stamp
        }
        catch {
            $retried += [pscustomobject]@{
                Type    = $item.Type
                Count   = 0
                File    = $null
                Error   = $_.Exception.Message
                Started = Get-Date
                Ended   = Get-Date
                Pid     = $PID
            }
        }
    }
    $ok = @($results | Where-Object { -not $_.Error })
    $results = $ok + $retried
}

$results | Sort-Object Type
Write-Host "Hotovo. S chybou:"
$results | Where-Object { $_.Error } | Format-Table Type, Error -Wrap
