# Debug-MagionPracMista.ps1
# Diagnostika struktury V_IDM_MISTA pro ladeni joinu managera

$OracleDll  = "C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\Extensions\Oracle.ManagedDataAccess.dll"
$DataSource = "(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(COMMUNITY=tcp.world)(PROTOCOL=TCP)(Host=magi.oradb-t.czu.cz)(Port=1523)))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME=MACV.oradb.czu.cz)))"
$Username   = "IDM"
$Password   = Read-Host "Oracle heslo pro uzivatele '$Username'" -AsSecureString
$PlainPass  = [System.Net.NetworkCredential]::new("", $Password).Password

$OutputFile = "C:\Temp\debug_pracmista.txt"
if (-not (Test-Path "C:\Temp")) { New-Item -ItemType Directory "C:\Temp" | Out-Null }

Add-Type -Path $OracleDll
$connStr = "Data Source=$DataSource;User Id=$Username;Password=$PlainPass;Pooling=false;"

function Run-Query {
    param([string]$Title, [string]$Sql, [Oracle.ManagedDataAccess.Client.OracleConnection]$Conn)

    $out = "`n" + ("=" * 70) + "`n"
    $out += " $Title`n"
    $out += ("=" * 70) + "`n"

    try {
        $cmd    = New-Object Oracle.ManagedDataAccess.Client.OracleCommand($Sql, $Conn)
        $cmd.CommandTimeout = 60
        $reader = $cmd.ExecuteReader()

        $cols = @()
        for ($i = 0; $i -lt $reader.FieldCount; $i++) { $cols += $reader.GetName($i) }
        $out += ($cols -join " | ") + "`n"
        $out += ("-" * 70) + "`n"

        $rowCount = 0
        while ($reader.Read()) {
            $vals = @()
            foreach ($col in $cols) {
                $vals += if ($reader.IsDBNull($reader.GetOrdinal($col))) { "NULL" } else { "$($reader[$col])" }
            }
            $out += ($vals -join " | ") + "`n"
            $rowCount++
        }
        $reader.Close()
        $out += "`n  [ $rowCount radku ]`n"
    } catch {
        $out += "  CHYBA: $_`n"
    }

    return $out
}

try {
    $conn = New-Object Oracle.ManagedDataAccess.Client.OracleConnection($connStr)
    $conn.Open()
    $result = ""

    # ── 1. Vzorove zaznamy z V_IDM_MISTA ─────────────────────────────────────
    $result += Run-Query "1. Vzorove zaznamy V_IDM_MISTA (prvnich 10)" @"
SELECT PMIS_CISLO, PMIS_NAZEV, CISLO_PP, UIC, OS_CIS,
       SOJ_CISLO, PMIS_NADRIZ, PMIS_TYP_VED,
       PLATNOST_OD, PLATNOST_DO, PMIS_VZNIK, PMIS_ZANIK
FROM IDM.V_IDM_MISTA
WHERE ROWNUM <= 10
ORDER BY PMIS_CISLO
"@ $conn

    # ── 2. Kolik zaznamu ma PMIS_NADRIZ vyplneny ─────────────────────────────
    $result += Run-Query "2. Statistika PMIS_NADRIZ" @"
SELECT
    COUNT(*)                                       AS celkem,
    COUNT(PMIS_NADRIZ)                             AS s_nadriz_vyplnenym,
    COUNT(CASE WHEN PMIS_NADRIZ IS NULL THEN 1 END) AS s_nadriz_null
FROM IDM.V_IDM_MISTA
"@ $conn

    # ── 3. Vzorove hodnoty PMIS_NADRIZ vs PMIS_CISLO ─────────────────────────
    $result += Run-Query "3. Vzorove hodnoty PMIS_NADRIZ (10 neprazdnych)" @"
SELECT PMIS_CISLO, PMIS_NADRIZ, PMIS_NAZEV, UIC
FROM IDM.V_IDM_MISTA
WHERE PMIS_NADRIZ IS NOT NULL
  AND ROWNUM <= 10
"@ $conn

    # ── 4. Existuje PMIS_NADRIZ ktery odpovida nejake PMIS_CISLO? ────────────
    $result += Run-Query "4. Test platnosti PMIS_NADRIZ -> PMIS_CISLO joinu" @"
SELECT m1.PMIS_CISLO AS emp_pmis, m1.PMIS_NADRIZ AS emp_nadriz,
       m2.PMIS_CISLO AS mgr_pmis, m2.UIC AS mgr_uic
FROM IDM.V_IDM_MISTA m1
JOIN IDM.V_IDM_MISTA m2 ON m2.PMIS_CISLO = m1.PMIS_NADRIZ
WHERE m1.PMIS_NADRIZ IS NOT NULL
  AND ROWNUM <= 10
"@ $conn

    # ── 5. Zamestnanec a jeho PracMista ──────────────────────────────────────
    $result += Run-Query "5. Prvni zamestnanec s aktivnim PPV a jeho PracMista" @"
SELECT zam.OS_CIS, zam.UIC, zam.ZAKL_PP,
       pm.PMIS_CISLO, pm.CISLO_PP, pm.PMIS_NAZEV,
       pm.SOJ_CISLO, pm.PMIS_NADRIZ, pm.PMIS_TYP_VED,
       pm.PLATNOST_OD, pm.PLATNOST_DO
FROM IDM.V_IDM_ZAM zam
JOIN IDM.V_IDM_PPV ppv ON ppv.UIC = zam.UIC AND ppv.CISLO_PP = zam.ZAKL_PP AND ppv.AKTIVNI = 1
LEFT JOIN IDM.V_IDM_MISTA pm ON pm.UIC = zam.UIC
WHERE zam.UIC = (
    SELECT MIN(z2.UIC) FROM IDM.V_IDM_ZAM z2
    JOIN IDM.V_IDM_PPV p2 ON p2.UIC = z2.UIC AND p2.CISLO_PP = z2.ZAKL_PP AND p2.AKTIVNI = 1
    JOIN IDM.V_IDM_MISTA m2 ON m2.UIC = z2.UIC
)
ORDER BY pm.CISLO_PP
"@ $conn

    # ── 6. Datove typy klicovych sloupcu ─────────────────────────────────────
    $result += Run-Query "6. Datove typy sloupcu MISTA" @"
SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, DATA_PRECISION, DATA_SCALE
FROM ALL_TAB_COLUMNS
WHERE OWNER = 'IDM' AND TABLE_NAME = 'V_IDM_MISTA'
  AND COLUMN_NAME IN ('PMIS_CISLO','PMIS_NADRIZ','CISLO_PP','UIC','SOJ_CISLO','PMIS_TYP_VED')
ORDER BY COLUMN_ID
"@ $conn

    $conn.Close()

    $result | Out-File -FilePath $OutputFile -Encoding UTF8
    Write-Host "Diagnostika ulozena do: $OutputFile" -ForegroundColor Green
    Write-Host "Soubor prosim poslete pro dalsi analyzu." -ForegroundColor Cyan

} catch {
    Write-Host "Chyba: $_" -ForegroundColor Red
} finally {
    if ($conn -and $conn.State -eq 'Open') { $conn.Close() }
}
