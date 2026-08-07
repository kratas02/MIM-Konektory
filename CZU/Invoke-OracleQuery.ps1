# Invoke-OracleQuery.ps1
# Obecny skript pro spousteni SQL dotazu na Magion Oracle DB
# Vystup: tabulka na konzoli a volitelne CSV soubor

param(
    [Parameter(Mandatory=$true)]
    [string]$Sql,               # SQL dotaz (inline nebo cesta k .sql souboru)

    [string]$CsvExport  = "",   # Volitelne: cesta pro export CSV
    [int]   $MaxRows    = 0,    # Volitelne: max pocet radku (0 = vsechny)
    [int]   $Timeout    = 120   # Timeout dotazu v sekundach
)

$OracleDll  = "C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\Extensions\Oracle.ManagedDataAccess.dll"
$DataSource = "(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(COMMUNITY=tcp.world)(PROTOCOL=TCP)(Host=magi.oradb-t.czu.cz)(Port=1523)))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME=MACV.oradb.czu.cz)))"
$Username   = "IDM"

# Pokud je Sql cesta k souboru, nacist obsah
if (Test-Path $Sql) {
    $Sql = Get-Content $Sql -Raw -Encoding UTF8
}

$Password  = Read-Host "Oracle heslo pro uzivatele '$Username'" -AsSecureString
$PlainPass = [System.Net.NetworkCredential]::new("", $Password).Password

Add-Type -Path $OracleDll

$connStr = "Data Source=$DataSource;User Id=$Username;Password=$PlainPass;Pooling=false;"

try {
    $conn = New-Object Oracle.ManagedDataAccess.Client.OracleConnection($connStr)
    $conn.Open()

    $cmd = New-Object Oracle.ManagedDataAccess.Client.OracleCommand($Sql, $conn)
    $cmd.CommandTimeout = $Timeout
    $reader = $cmd.ExecuteReader()

    # Zjisti nazvy sloupcu
    $cols = @()
    for ($i = 0; $i -lt $reader.FieldCount; $i++) {
        $cols += $reader.GetName($i)
    }

    # Nacti radky
    $results = @()
    while ($reader.Read()) {
        $row = [ordered]@{}
        foreach ($col in $cols) {
            $idx = $reader.GetOrdinal($col)
            $row[$col] = if ($reader.IsDBNull($idx)) { $null } else { "$($reader.GetValue($idx))" }
        }
        $results += [PSCustomObject]$row

        if ($MaxRows -gt 0 -and $results.Count -ge $MaxRows) {
            Write-Host "(Omezeno na $MaxRows radku parametrem -MaxRows)" -ForegroundColor Yellow
            break
        }
    }

    $reader.Close()
    $conn.Close()

    Write-Host "Nalezeno radku: $($results.Count)" -ForegroundColor Cyan
    $results | Format-Table -AutoSize

    if ($CsvExport) {
        $results | Export-Csv -Path $CsvExport -NoTypeInformation -Encoding UTF8
        Write-Host "Exportovano do: $CsvExport" -ForegroundColor Green
    }

    return $results

} catch {
    Write-Host "Chyba: $_" -ForegroundColor Red
} finally {
    if ($conn -and $conn.State -eq 'Open') { $conn.Close() }
}
