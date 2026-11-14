<#
.SYNOPSIS
    Synchronizuje clenstvi v AD skupine podle rolove (Cast) prirazeni v databazi EasyIDM.

.DESCRIPTION
    CMD1 - vraci TechName vsech uzivatelu, kteri aktualne (platne, nearchivovane, bez expirace)
           maji prirazenou roli $RoleId. Tito uzivatele MAJI byt clenem AD skupiny.
    CMD2 - vraci TechName VSECH uzivatelskych objektu v DB (IDMObjectTypeId = 3, platnych,
           nearchivovanych). Predstavuje "univerzum" uzivatelu, o kterych DB vubec vi.

    Synchronizace clenstvi AD skupiny:
      - kazdy uzivatel z CMD1, ktery jeste neni clenem skupiny, se PRIDA.
      - stavajici clen skupiny, ktery NENI v CMD2 (DB o nem nic nevi - typicky externi ucet,
        service account, rucne pridany clen), se NIKDY neodebira - je pouze zalogovan jako
        ignorovany.
      - stavajici clen skupiny, ktery JE v CMD2, ale NENI v CMD1 (ma v DB byt spravovan, ale
        prislusnou roli uz nema), se ODEBERE.

    Rezimy:
      -Mode Test  (vychozi) - jen spocita a zaloguje planovane zmeny, do AD nic nezapisuje.
      -Mode Real  - planovane zmeny do AD skupiny opravdu provede.

    Chybove chovani:
      Skript nejprve nacte data z SQL a z AD a sestavi kompletni plan zmen (Add/Remove).
      Pokud selze cteni dat (SQL, AD), skoncí drive, nez se cehokoliv v AD dotkne.
      Pri aplikaci planu (rezim Real) se pri jakekoliv chybe (napr. Add-ADGroupMember /
      Remove-ADGroupMember vyhodi vyjimku) zpracovani OKAMZITE zastavi - dalsi polozky planu
      se uz neaplikuji. AD skupina neni transakcni uloziste, takze zmeny provedene PRED chybou
      nelze vratit zpet; skript ale zaruci, ze se nepokracuje dal a incident je videt v logu
      i v exit code (1).

.PARAMETER SqlServer
    Nazev SQL serveru / instance s databazi EasyIDM.

.PARAMETER Database
    Nazev databaze. Vychozi EasyIDM.

.PARAMETER SqlCredential
    Volitelne PSCredential pro SQL autentizaci. Bez tohoto parametru se pouzije
    Windows (Integrated) autentizace.

.PARAMETER RoleId
    GUID role (Cast.RoleId), jejiz clenove maji byt v AD skupine.

.PARAMETER GroupIdentity
    Identita cilove AD skupiny (Name, SamAccountName, DistinguishedName, GUID nebo SID).
    Vychozi testSyncGroup.

.PARAMETER AdUserAttribute
    Atribut AD uzivatele, ktery odpovida TechName z databaze. Vychozi sAMAccountName.
    Pokud TechName v teto instalaci EasyIDM odpovida jinemu AD atributu
    (napr. extensionAttribute1), zadejte jeho nazev.

.PARAMETER Mode
    Test = pouze nasimuluje a zaloguje zmeny (vychozi, bezpecne).
    Real = zmeny v AD skupine skutecne provede.

.PARAMETER LogDirectory
    Slozka pro log soubory. Vychozi .\Logs vedle skriptu. Pro kazdy beh se vytvori
    samostatny soubor s casovym razitkem.

.PARAMETER CommandTimeout
    Timeout SQL dotazu v sekundach. Vychozi 30.

.EXAMPLE
    .\Sync-ADGroupMembership.ps1 -SqlServer MIMSQL -Mode Test
    Suchy beh - jen ukaze, co by se zmenilo.

.EXAMPLE
    .\Sync-ADGroupMembership.ps1 -SqlServer MIMSQL -GroupIdentity testSyncGroup -Mode Real
    Provede skutecnou synchronizaci clenstvi.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    #[Parameter(Mandatory = $true)]
    [string] $SqlServer = "MIM",

    [string] $Database = "EasyIDM",

    [System.Management.Automation.PSCredential] $SqlCredential,

    [string] $RoleId = "9b241c42-7ab1-48d2-82d2-16cea2152fea",

    [string] $GroupIdentity = "testSyncGroup",

    [string] $AdUserAttribute = "sAMAccountName",

    [ValidateSet("Test", "Real")]
    [string] $Mode = "Test",

    [string] $LogDirectory = "C:\Projects\MIM\Asseco\Logs",

    [int] $CommandTimeout = 30
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ──────────────────────────────────────────────────────────────────────────────
# Logovani
# ──────────────────────────────────────────────────────────────────────────────

if (-not (Test-Path $LogDirectory)) {
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
}

$script:LogFile = Join-Path $LogDirectory ("Sync-{0}-{1}.log" -f $GroupIdentity, (Get-Date -Format "yyyyMMdd-HHmmss"))

function Write-Log {
    param(
        [Parameter(Mandatory = $true)] [string] $Message,
        [ValidateSet("INFO", "WARN", "ERROR", "ACTION")] [string] $Level = "INFO"
    )

    $line = "{0} [{1}] {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Level, $Message
    Add-Content -Path $script:LogFile -Value $line -Encoding UTF8
    switch ($Level) {
        "ERROR"  { Write-Host $line -ForegroundColor Red }
        "WARN"   { Write-Host $line -ForegroundColor Yellow }
        "ACTION" { Write-Host $line -ForegroundColor Cyan }
        default  { Write-Host $line }
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# SQL pomocne funkce (System.Data.SqlClient - bez zavislosti na SqlServer modulu)
# ──────────────────────────────────────────────────────────────────────────────

function New-SqlConnectionString {
    param([string] $Server, [string] $Db, [System.Management.Automation.PSCredential] $Credential)

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder["Data Source"] = $Server
    $builder["Initial Catalog"] = $Db
    $builder["Application Name"] = "Sync-ADGroupMembership"
    $builder["Connect Timeout"] = 30

    if ($Credential) {
        $builder["Integrated Security"] = $false
        $builder["User ID"] = $Credential.UserName
        $builder["Password"] = $Credential.GetNetworkCredential().Password
    }
    else {
        $builder["Integrated Security"] = $true
    }

    return $builder.ConnectionString
}

function Get-TechNameList {
    <#
        Provede dotaz, ktery musi vratit jediny sloupec TechName, a vrati vysledek
        jako string[]. Pouziva se pro CMD1 i CMD2.
    #>
    param(
        [Parameter(Mandatory = $true)] [string] $ConnectionString,
        [Parameter(Mandatory = $true)] [string] $Query,
        [System.Data.SqlClient.SqlParameter[]] $SqlParameters = @(),
        [int] $Timeout = 30
    )

    if ($null -eq $SqlParameters) { $SqlParameters = @() }

    $results = New-Object System.Collections.Generic.List[string]

    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = $Timeout
        if ($SqlParameters.Count -gt 0) {
            $command.Parameters.AddRange($SqlParameters)
        }

        $reader = $command.ExecuteReader()
        try {
            while ($reader.Read()) {
                if (-not $reader.IsDBNull(0)) {
                    $results.Add([string]$reader.GetValue(0))
                }
            }
        }
        finally {
            $reader.Close()
        }
    }
    finally {
        $connection.Close()
    }

    return $results
}

# ──────────────────────────────────────────────────────────────────────────────
# AD pomocne funkce
# ──────────────────────────────────────────────────────────────────────────────

function Get-EscapedLdapFilterValue {
    <# Escapuje LDAP-specialni znaky pro bezpecne pouziti v -Filter / -LDAPFilter. #>
    param([string] $Value)

    return $Value.Replace('\', '\5c').Replace('*', '\2a').Replace('(', '\28').Replace(')', '\29').Replace("`0", '\00')
}

function Get-AdUserAttributeValue {
    <# Vrati hodnotu $AdUserAttribute pro clena skupiny vraceneho Get-ADGroupMember. #>
    param($Member, [string] $Attribute)

    if ($Attribute -eq "sAMAccountName" -and $Member.PSObject.Properties.Match("SamAccountName").Count -gt 0) {
        return $Member.SamAccountName
    }

    $adObject = Get-ADObject -Identity $Member.DistinguishedName -Properties $Attribute
    return $adObject.$Attribute
}

# ──────────────────────────────────────────────────────────────────────────────
# Hlavni logika
# ──────────────────────────────────────────────────────────────────────────────

$exitCode = 0

try {
    Write-Log "=== Sync-ADGroupMembership start (Mode=$Mode, Group=$GroupIdentity, RoleId=$RoleId) ==="

    Import-Module ActiveDirectory -ErrorAction Stop

    $connectionString = New-SqlConnectionString -Server $SqlServer -Db $Database -Credential $SqlCredential

    # ── CMD1: kdo MA byt clenem (aktivni cast dane role) ─────────────────────
    $cmd1Query = @"
SELECT
    io.TechName
FROM dbo.Casts AS c
INNER JOIN dbo.IDMObjects AS io
    ON c.IdentityId = io.IDMObjectId
WHERE c.RoleId = @RoleId
  AND c.IsValid = 1
  AND c.IsArchived = 0
  AND (c.ValidTo > GETDATE() OR c.ValidTo IS NULL);
"@
    $roleIdParam = New-Object System.Data.SqlClient.SqlParameter("@RoleId", [System.Data.SqlDbType]::UniqueIdentifier)
    $roleIdParam.Value = [Guid]$RoleId

    $desiredMembers = Get-TechNameList -ConnectionString $connectionString -Query $cmd1Query `
        -SqlParameters @($roleIdParam) -Timeout $CommandTimeout
    Write-Log "CMD1: $($desiredMembers.Count) TechName (aktivni clenove role $RoleId)."

    # ── CMD2: univerzum vsech uzivatelu, o kterych DB vi ─────────────────────
    $cmd2Query = @"
SELECT TechName
FROM dbo.IDMObjects
WHERE IDMObjectTypeId = 3
  AND IsArchived = 0
  AND IsValid = 1;
"@
    $knownUsers = Get-TechNameList -ConnectionString $connectionString -Query $cmd2Query  -SqlParameters @() -Timeout $CommandTimeout
    Write-Log "CMD2: $($knownUsers.Count) TechName (vsichni platni uzivatele v DB)."

    $desiredSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$desiredMembers, [StringComparer]::OrdinalIgnoreCase)
    $knownSet   = [System.Collections.Generic.HashSet[string]]::new([string[]]$knownUsers,   [StringComparer]::OrdinalIgnoreCase)

    # ── Aktualni stav AD skupiny ──────────────────────────────────────────────
    $group = Get-ADGroup -Identity $GroupIdentity -ErrorAction Stop
    Write-Log "AD skupina nalezena: $($group.DistinguishedName)"

    $currentMembers = @(Get-ADGroupMember -Identity $group -ErrorAction Stop)
    Write-Log "AD skupina ma aktualne $($currentMembers.Count) clenu."

    # Mapa: hodnota AdUserAttribute (case-insensitive) -> AD member objekt. Objectclass se
    # neresi - o tom, jestli je clen "znamy", rozhoduje az dale to, zda je jeho hodnota
    # AdUserAttribute mezi vysledky CMD2 ($knownUsers).
    $currentMemberMap = New-Object 'System.Collections.Generic.Dictionary[string, object]' ([StringComparer]::OrdinalIgnoreCase)

    foreach ($member in $currentMembers) {
        $value = Get-AdUserAttributeValue -Member $member -Attribute $AdUserAttribute
        if ([string]::IsNullOrWhiteSpace($value)) {
            Write-Log "Clen $($member.DistinguishedName) [$($member.objectClass)] nema vyplneny atribut '$AdUserAttribute' - ignorovano." -Level WARN
            continue
        }

        $currentMemberMap[$value] = $member
    }

    # ── Plan: koho pridat ─────────────────────────────────────────────────────
    $toAdd = New-Object System.Collections.Generic.List[string]
    $notFoundInAd = New-Object System.Collections.Generic.List[string]

    foreach ($techName in $desiredMembers) {
        if ($currentMemberMap.ContainsKey($techName)) {
            continue
        }

        $filterValue = Get-EscapedLdapFilterValue -Value $techName
        $adUser = Get-ADUser -Filter "$AdUserAttribute -eq '$filterValue'" -Properties $AdUserAttribute -ErrorAction Stop |
            Select-Object -First 1

        if (-not $adUser) {
            $notFoundInAd.Add($techName)
            Write-Log "TechName '$techName' ma mit clenstvi, ale v AD neexistuje uzivatel s $AdUserAttribute='$techName'." -Level WARN
            continue
        }

        $toAdd.Add($adUser.DistinguishedName)
        Write-Log "Planovano PRIDANI: $techName ($($adUser.DistinguishedName))"
    }

    # ── Plan: koho odebrat ────────────────────────────────────────────────────
    $toRemove = New-Object System.Collections.Generic.List[string]
    $ignoredUnknownMembers = 0

    foreach ($kvp in $currentMemberMap.GetEnumerator()) {
        $techName = $kvp.Key
        $member   = $kvp.Value

        if (-not $knownSet.Contains($techName)) {
            # DB o tomto uzivateli nic nevi (externi/rucne pridany ucet, nebo objekt, ktery
            # v atributu AdUserAttribute nema TechName - napr. vnorena skupina) - nikdy se neodebira
            $ignoredUnknownMembers++
            Write-Log "Ignorovano (neni na portale): $techName [$($member.objectClass)]"
            continue
        }

        if (-not $desiredSet.Contains($techName)) {
            $toRemove.Add($member.DistinguishedName)
            Write-Log "Planovano ODEBRANI: $techName ($($member.DistinguishedName))"
        }
    }

    Write-Log ("Plan: pridat={0}, odebrat={1}, nenalezeno v AD={2}, ignorovano (neznamo na portale)={3}" -f `
        $toAdd.Count, $toRemove.Count, $notFoundInAd.Count, $ignoredUnknownMembers)

    # ── Aplikace planu ────────────────────────────────────────────────────────
    if ($Mode -eq "Test") {
        Write-Log "Rezim TEST - do AD se nic nezapisuje. Konec." -Level ACTION
    }
    else {
        Write-Log "Rezim REAL - aplikuji planovane zmeny." -Level ACTION

        foreach ($dn in $toAdd) {
            Add-ADGroupMember -Identity $group -Members $dn -ErrorAction Stop
            Write-Log "PRIDAN clen: $dn" -Level ACTION
        }

        foreach ($dn in $toRemove) {
            Remove-ADGroupMember -Identity $group -Members $dn -Confirm:$false -ErrorAction Stop
            Write-Log "ODEBRAN clen: $dn" -Level ACTION
        }

        Write-Log "Vsechny planovane zmeny byly uspesne aplikovany."
    }
}
catch {
    Write-Log "CHYBA - zpracovani zastaveno, dalsi zmeny se NEAPLIKUJI: $($_.Exception.Message)" -Level ERROR
    Write-Log ($_.ScriptStackTrace) -Level ERROR
    $exitCode = 1
}
finally {
    Write-Log "=== Sync-ADGroupMembership konec (exit code $exitCode) ==="
}

exit $exitCode
