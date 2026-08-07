# Query-MagionPPVManager.ps1
# Pro kazdeho aktivniho zamestnance hleda managera pres PRIMY hierarchicky odkaz:
# V_IDM_ZAM -> ZAKL_PP -> emp.PracMista.PMIS_NADRIZ -> mgr.PracMista.UIC -> V_IDM_ZAM (manager)
# Jeden radek = jeden zamestnanec

param(
    [string]$OsCis     = "",   # Volitelne: filtr na konkretni osobni cislo
    [string]$Uic       = "",   # Volitelne: filtr na konkretni UIC
    [string]$CsvExport = ""    # Volitelne: cesta pro export CSV napr. "C:\ppv_manager.csv"
)

$OracleDll  = "C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\Extensions\Oracle.ManagedDataAccess.dll"
$DataSource = "(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(COMMUNITY=tcp.world)(PROTOCOL=TCP)(Host=magi.oradb-t.czu.cz)(Port=1523)))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME=MACV.oradb.czu.cz)))"
$Username   = "IDM"
$Password   = Read-Host "Oracle heslo pro uzivatele '$Username'" -AsSecureString
$PlainPass  = [System.Net.NetworkCredential]::new("", $Password).Password

Add-Type -Path $OracleDll

$connStr = "Data Source=$DataSource;User Id=$Username;Password=$PlainPass;Pooling=false;"

# ── SQL ─────────────────────────────────────────────────────────────────────
# Subquery "active_mista" kolapsuji historicke zaznamy:
#   emp_active  = jedno aktualni pracovni misto per (UIC, CISLO_PP)
#   mgr_active  = jeden aktivni clovek per PMIS_CISLO (MIN UIC, role != X)
# Vysledek: vzdy 0 nebo 1 manager - zadne VICE MOZNOSTI
$sql = @"
SELECT
    zam.OS_CIS,
    zam.UIC,
    zam.ZAKL_PP,
    ppv.OSPOM,
    ppv.DRUH_PP,
    ppv.FUNKCE,
    ppv.STRED_KMEN_KOD,
    ppv.STRED_KMEN,
    emp_pm.PMIS_CISLO                                    AS emp_pmis_cislo,
    emp_pm.PMIS_NAZEV                                    AS emp_pmis_nazev,
    emp_pm.PMIS_NADRIZ                                   AS emp_pmis_nadriz,
    CASE WHEN mgr.UIC IS NOT NULL THEN 1 ELSE 0 END      AS mgr_pocet_kandidatu,
    mgr.UIC                                              AS mgr_uic,
    mgr.OS_CIS                                           AS mgr_os_cis,
    mgr.PRIJMENI || ' ' || mgr.JMENO                     AS mgr_jmeno
FROM IDM.V_IDM_ZAM zam
-- Pouze zakladni PPV (CISLO_PP = ZAKL_PP, aktivni)
JOIN IDM.V_IDM_PPV ppv
    ON  ppv.UIC      = zam.UIC
    AND ppv.CISLO_PP = zam.ZAKL_PP
    AND ppv.AKTIVNI  = 1
-- Pracovni misto zamestnance: jedno aktivni per (UIC, CISLO_PP)
LEFT JOIN (
    SELECT UIC, CISLO_PP,
           MIN(PMIS_CISLO) AS PMIS_CISLO,
           MAX(PMIS_NAZEV) AS PMIS_NAZEV,
           MAX(PMIS_NADRIZ) AS PMIS_NADRIZ
    FROM IDM.V_IDM_MISTA
    WHERE (PLATNOST_OD IS NULL OR PLATNOST_OD <= TRUNC(SYSDATE))
      AND (PLATNOST_DO IS NULL OR PLATNOST_DO >= TRUNC(SYSDATE))
      AND (PMIS_ZANIK  IS NULL OR PMIS_ZANIK  >= TRUNC(SYSDATE))
    GROUP BY UIC, CISLO_PP
) emp_pm ON emp_pm.UIC = zam.UIC AND emp_pm.CISLO_PP = zam.ZAKL_PP
-- Nadrizene misto: jeden clovek per PMIS_CISLO (MIN UIC, bez administrativnich roli X)
LEFT JOIN (
    SELECT PMIS_CISLO, MIN(UIC) AS UIC
    FROM IDM.V_IDM_MISTA
    WHERE (PLATNOST_OD IS NULL OR PLATNOST_OD <= TRUNC(SYSDATE))
      AND (PLATNOST_DO IS NULL OR PLATNOST_DO >= TRUNC(SYSDATE))
      AND (PMIS_ZANIK  IS NULL OR PMIS_ZANIK  >= TRUNC(SYSDATE))
      AND PMIS_TYP_VED != 'X'
      AND UIC IS NOT NULL
    GROUP BY PMIS_CISLO
) mgr_pm ON mgr_pm.PMIS_CISLO = emp_pm.PMIS_NADRIZ
-- Manager: musi byt aktivni zamestnanec (ma aspon jedno aktivni PPV)
LEFT JOIN IDM.V_IDM_ZAM mgr
    ON mgr.UIC = mgr_pm.UIC
LEFT JOIN (
    SELECT DISTINCT UIC FROM IDM.V_IDM_PPV WHERE AKTIVNI = 1
) mgr_ppv ON mgr_ppv.UIC = mgr.UIC
WHERE zam.UIC IS NOT NULL
  AND (mgr_pm.PMIS_CISLO IS NULL OR mgr_ppv.UIC IS NOT NULL)
"@

# Volitelne filtry
$extraWhere = @()
if ($OsCis) { $extraWhere += "zam.OS_CIS = '$OsCis'" }
if ($Uic)   { $extraWhere += "zam.UIC    = '$Uic'"   }
if ($extraWhere.Count -gt 0) {
    $sql += "`nAND " + ($extraWhere -join " AND ")
}

$sql += "`nORDER BY zam.OS_CIS"

try {
    Write-Host "Probiha dotaz na Oracle..." -ForegroundColor Cyan

    $conn = New-Object Oracle.ManagedDataAccess.Client.OracleConnection($connStr)
    $conn.Open()

    $cmd = New-Object Oracle.ManagedDataAccess.Client.OracleCommand($sql, $conn)
    $cmd.CommandTimeout = 120
    $reader = $cmd.ExecuteReader()

    $results = @()
    while ($reader.Read()) {
        $pocet = [int]$reader["mgr_pocet_kandidatu"]

        $mgrStav = switch ($pocet) {
            0 { "NENALEZEN" }
            1 { "OK" }
        }

        $uic      = "$($reader["UIC"])"
        $ospom    = "$($reader["OSPOM"])"
        $mgrUic   = if ($pocet -ge 1) { "$($reader["mgr_uic"])" } else { "" }

        $results += [PSCustomObject]@{
            OS_CIS           = "$($reader["OS_CIS"])"
            UIC              = $uic
            ZAKL_PP          = "$($reader["ZAKL_PP"])"
            OSPOM            = $ospom
            DRUH_PP          = "$($reader["DRUH_PP"])"
            FUNKCE           = "$($reader["FUNKCE"])"
            STRED_KMEN_KOD   = "$($reader["STRED_KMEN_KOD"])"
            STRED_KMEN       = "$($reader["STRED_KMEN"])"
            EMP_PMIS_CISLO   = "$($reader["emp_pmis_cislo"])"
            EMP_PMIS_NAZEV   = "$($reader["emp_pmis_nazev"])"
            EMP_PMIS_NADRIZ  = "$($reader["emp_pmis_nadriz"])"
            # MIM Connector Space DNs (format CN=<anchor>,CN=<ObjectType>)
            MIM_PERSON_DN    = "CN=$uic,CN=Person"
            MIM_PPV_DN       = "CN=$ospom,CN=PPV"
            MANAGER_STAV     = $mgrStav
            MANAGER_UIC      = $mgrUic
            MANAGER_OS_CIS   = if ($pocet -ge 1) { "$($reader["mgr_os_cis"])" } else { "" }
            MANAGER_JMENO    = if ($pocet -ge 1) { "$($reader["mgr_jmeno"])"  } else { "" }
            MIM_MANAGER_DN   = if ($mgrUic)      { "CN=$mgrUic,CN=Person"     } else { "" }
        }
    }

    $reader.Close()
    $conn.Close()

    # ── Souhrn ───────────────────────────────────────────────────────────────
    $celkem    = $results.Count
    $ok        = ($results | Where-Object { $_.MANAGER_STAV -eq "OK"        }).Count
    $nenalezen = ($results | Where-Object { $_.MANAGER_STAV -eq "NENALEZEN" }).Count
    $vice      = ($results | Where-Object { $_.MANAGER_STAV -like "VICE*"   }).Count

    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host " SOUHRN" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host " Celkem zamestnancu    : $celkem"
    Write-Host " Manager nalezen (OK)  : $ok"        -ForegroundColor Green
    Write-Host " Manager NENALEZEN     : $nenalezen"  -ForegroundColor Yellow
    # "VICE MOZNOSTI" eliminovano subquery (MIN UIC per pozice)
    Write-Host "================================================" -ForegroundColor Cyan

    # ── Problematicke zaznamy ─────────────────────────────────────────────────
    if ($nenalezen -gt 0) {
        Write-Host "`nZamestnanci bez manazera (prvnich 20):" -ForegroundColor Yellow
        $results |
            Where-Object { $_.MANAGER_STAV -eq "NENALEZEN" } |
            Select-Object OS_CIS, UIC, ZAKL_PP, DRUH_PP, STRED_KMEN_KOD, STRED_KMEN |
            Select-Object -First 20 |
            Format-Table -AutoSize
    }

    # "VICE MOZNOSTI" uz nemoze nastat - subquery vraci vzdy max 1 manazera

    # ── CSV export ────────────────────────────────────────────────────────────
    if ($CsvExport) {
        $results | Export-Csv -Path $CsvExport -NoTypeInformation -Encoding UTF8
        Write-Host "`nExportovano do: $CsvExport" -ForegroundColor Green
    }

    return $results

} catch {
    Write-Host "Chyba: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
} finally {
    if ($conn -and $conn.State -eq 'Open') { $conn.Close() }
}
