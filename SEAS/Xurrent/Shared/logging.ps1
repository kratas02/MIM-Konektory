[string]$DefaultPath = "C:\Batch\Logs\Script\scripts.log"

function Write-Log {
       param (
        
        [PSCustomObject]$Message,
        [string]$Level="Information",
        [string]$source,
        $ex,
        [string]$detail,
        [string]$logFile,
        [int]$duration,
        [string]$account
    )

    if([string]::IsNullOrEmpty($Message))
    {
        return
    }

    if([string]::IsNullOrEmpty($source))
    {
        $source="PSScript"
    }

    if([string]::IsNullOrEmpty($logFile))
    {
        $Path=$DefaultPath
    }
    else
    {
        $Path =  $logFile
    }

    if([string]::IsNullOrEmpty($scriptName))
    {
        $script=$MyInvocation.MyCommand.Name
    }
    else
    {
        $script = $scriptName
    }

    if($ex -ne $null)
    {
        
    }

    $LogEntry = [PSCustomObject]@{
    "@t"  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    "@m"   = $Message
    "@l"     = $Level
    Exception = $ex
    Source    = $source
    Script    = $script
    Detail    = $detail
    Account    = $account
    Duration    = $duration
    Executed_by      = $env:USERNAME
}

    $MaxSizeInBytes = 100 * 1024 * 1024 # 10 MB

    # Vytvoření globálního pojmenovaného zámku (Mutexu) v OS Windows
    $MutexName = "Global\PowerShellLogMutex-" + $Path.GetHashCode()
    $Mutex = New-Object System.Threading.Mutex($false, $MutexName)

    # Skript se pokusí získat exkluzivní přístup (počká maximálně 10 sekund)
    if ($Mutex.WaitOne(10000)) {
        try {
            # 1. Kontrola velikosti a rotace logu (nyní bezpečně pod zámkem)
            if (Test-Path $Path) {
                $FileInfo = Get-Item $Path
                if ($FileInfo.Length -ge $MaxSizeInBytes) {
                    $Timestamp = (Get-Date -Format "yyyyMMdd-HHmmss")
                    $RotatedPath = $Path -replace '\.log$', "-$Timestamp.log"
                    if ($RotatedPath -eq $Path) { $RotatedPath = "$Path-$Timestamp" }
                    
                    Move-Item -Path $Path -Destination $RotatedPath -Force -ErrorAction SilentlyContinue
                }
            }

            # 2. Příprava dat pro zápis
            $JsonLine = ($LogEntry | ConvertTo-Json -Compress) + "`n" # [Environment]::NewLine
           
            $EncodingNoBOM = New-Object System.Text.UTF8Encoding($false)
            $Bytes = $EncodingNoBOM.GetBytes($JsonLine)

            #$Bytes    = [System.Text.Encoding]::UTF8.GetBytes($JsonLine)

            # 3. Zápis do souboru (FileShare.ReadWrite zde necháváme, aby LogViewer mohl soubor číst i za běhu)
            $FileStream = New-Object System.IO.FileStream(
                $Path, 
                [System.IO.FileMode]::Append, 
                [System.IO.FileAccess]::Write, 
                [System.IO.FileShare]::ReadWrite
            )
            
            $FileStream.Write($Bytes, 0, $Bytes.Length)
            $FileStream.Close()
            $FileStream.Dispose()
        }
        finally {
            # Uvolnění zámku pro další skripty v řadě
            $Mutex.ReleaseMutex()
            $Mutex.Dispose()
        }
    }
    else {
        # Pokud by se zápis zasekl na více než 10 sekund, zapíše se nouzově do Event Logu Windows
        Write-EventLog -LogName Application -Source "Application" -EntryType Error -EventId 9999 `
            -Message "Logovací soubor byl příliš dlouho uzamčen. Data: $($LogData | ConvertTo-Json -Compress)"
    }
}



function Optimize-OldLogs {
    param (
        [string]$LogFolder,
        [string]$BaseLogName = "script" # Základní název logu bez přípony
    )

    $LimitDate = (Get-Date).AddDays(-7)

    # 1. Najde rotované logy starší než 7 dní
    $OldLogs = Get-ChildItem -Path $LogFolder -Filter "$BaseLogName-*.log" | 
        Where-Object { $_.LastWriteTime -lt $LimitDate }

    if ($null -eq $OldLogs) {
        return "Žádné logy starší než týden k archivaci nenalezeny."
    }

    # 2. Seskupení logů podle čísla týdne v roce
    $GroupedLogs = $OldLogs | Group-Object {
        $Culture = [System.Globalization.CultureInfo]::CurrentCulture
        $Week = $Culture.Calendar.GetWeekOfYear($_.LastWriteTime, $Culture.DateTimeFormat.CalendarWeekRule, $Culture.DateTimeFormat.FirstDayOfWeek)
        $Year = $_.LastWriteTime.Year
        "{0}-W{1:D2}" -f $Year, $Week
    }

    # 3. Zazipování a následné smazání
    foreach ($Group in $GroupedLogs) {
        $ZipName = "{0}-{1}.zip" -f $BaseLogName, $Group.Name
        $ZipPath = Join-Path -Path $LogFolder -ChildPath $ZipName

        # Filtrace pouze volných (neuzamčených) souborů
        $FilesToZip = foreach ($File in $Group.Group) {
            try {
                $Stream = [System.IO.File]::Open($File.FullName, 'Open', 'Read', 'None')
                $Stream.Close()
                $Stream.Dispose()
                $File.FullName
            }
            catch {
                Write-Warning "Soubor $($File.Name) je uzamčen, bude zpracován příště."
            }
        }

        if ($FilesToZip) {
            try {
                # Vytvoření/Aktualizace ZIP archivu
                Compress-Archive -Path $FilesToZip -DestinationPath $ZipPath -Update -ErrorAction Stop
                
                # BEZPEČNOSTNÍ KONTROLA: Existuje ZIP a má velikost větší než 0 B?
                if (Test-Path $ZipPath) {
                    $ZipInfo = Get-Item $ZipPath
                    if ($ZipInfo.Length -gt 0) {
                        # Původní logy se po úspěšném zazipování smažou
                        Remove-Item -Path $FilesToZip -Force -ErrorAction SilentlyContinue
                        Write-Host "Úspěšně archivováno a smazáno $($FilesToZip.Count) souborů pro týden $($Group.Name)."
                    }
                }
            }
            catch {
                Write-Error "Chyba při archivaci nebo mazání týdne $($Group.Name): $_"
            }
        }
    }
}
