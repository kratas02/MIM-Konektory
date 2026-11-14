#Requires -Version 5.1
<#
.SYNOPSIS
    Contract s vice nez jednou hodnotou EbelnManco zapise na portal jako manager.

.DESCRIPTION
    V metaverse (FIMSynchronizationService) najde objekty daneho typu, ktere
    maji ve vicehodnotovem reference atributu EbelnManco vic nez jednu hodnotu.

    ID pro portal neni object_id z metaverse. Pro contract i pro kazdy
    odkaz se vezme skutecna hodnota anchoru konektoru EIDMMD (idmObjectId).
    Kdyz anchor nejde precist, pouzije se RDN connector space.

    JSON odpovida exportu EasyIDM konektoru pro idmObjectType 31 (Contract):
    POST {BaseUrl}/api/mimsync/synchronize/31
    telo je pole s jednim objektem, manager je v Attributes a hodnoty
    jsou GUID oddelene strednikem.

    Prihlaseni na portal je client_credentials, client_id EasyIDMPortalVNext,
    scope easyidm_portal. URL a secret jdou z parametru, nebo z XML
    konektoru (Base, ClientSecret, Export URL Full).

.PARAMETER ObjectType
    Typ objektu v metaverse. Vychozi je contract.

.PARAMETER ReferenceAttribute
    Vicehodnotovy reference atribut v metaverse. Vychozi je EbelnManco.

.PARAMETER ManagerAttribute
    Technicky nazev atributu na portalu. Vychozi je manager.

.PARAMETER ManagementAgent
    Management agent, ze ktereho se cte skutecne idmObjectId. Vychozi je EIDMMD.

.PARAMETER IdmObjectTypeId
    ID typu na portalu. Pro Contract je 31.

.PARAMETER PortalBaseUrl
    Zakladni URL portalu, treba https://idm.example.

.PARAMETER ClientSecret
    Client secret pro /connect/token.

.PARAMETER ConnectorConfig
    XML konektoru EIDMA. Doplni URL, secret a export path, ktere nebyly zadane.

.PARAMETER ExportPath
    Cesta exportu pripojena k PortalBaseUrl. Vychozi /api/mimsync/synchronize.

.PARAMETER Server
    SQL server. Bez nej se bere z registru sluzby FIMSynchronizationService.

.PARAMETER Database
    Databaze. Vychozi je hodnota z registru, jinak FIMSynchronizationService.

.EXAMPLE
    .\Update-EidmContractManager.ps1 -ConnectorConfig "D:\MA\EIDMAConnector.xml" -WhatIf

.EXAMPLE
    .\Update-EidmContractManager.ps1 -PortalBaseUrl "https://idm.example" -ClientSecret "secret"
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $ObjectType = 'contract',
    [string] $ReferenceAttribute = 'EbelnManco',
    [string] $ManagerAttribute = 'manager',
    [string] $ManagementAgent = 'EIDMMD',
    [int] $IdmObjectTypeId = 31,
    [string] $PortalBaseUrl,
    [string] $ClientSecret,
    [string] $ConnectorConfig,
    [string] $ExportPath = '/api/mimsync/synchronize',
    [string] $Server,
    [string] $Database
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Write-Log {
    param(
        [Parameter(Mandatory = $true)][string] $Message,
        [ValidateSet('INFO', 'WARN', 'ERROR')][string] $Level = 'INFO'
    )
    $color = switch ($Level) {
        'WARN'  { 'Yellow' }
        'ERROR' { 'Red' }
        default { 'Cyan' }
    }
    Write-Host ("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message) -ForegroundColor $color
}

function Get-FimSyncConnectionInfo {
    if ($script:Server -and $script:Database) {
        return [pscustomobject]@{ Server = $script:Server; Database = $script:Database }
    }

    $regPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\FIMSynchronizationService\Parameters'
    if (-not (Test-Path -LiteralPath $regPath)) {
        throw "Na tomhle pocitaci neni sluzba FIM Synchronization Service. Skript spuste na MIM Sync serveru, nebo zadejte -Server a -Database."
    }

    $reg = Get-ItemProperty -LiteralPath $regPath
    $sqlServer = $reg.Server
    $instance = $reg.SQLInstance
    $db = $reg.DBName
    if ([string]::IsNullOrWhiteSpace($db)) { $db = 'FIMSynchronizationService' }
    if ($script:Database) { $db = $script:Database }

    if ($script:Server) {
        $sqlServer = $script:Server
    }
    elseif (-not [string]::IsNullOrWhiteSpace($instance) -and $instance -ne 'MSSQLSERVER') {
        $sqlServer = '{0}\{1}' -f $reg.Server, $instance
    }

    if ([string]::IsNullOrWhiteSpace($sqlServer)) {
        throw "V registru sluzby chybi nazev SQL serveru (Server)."
    }

    return [pscustomobject]@{ Server = $sqlServer; Database = $db }
}

function Invoke-FimQuery {
    param(
        [Parameter(Mandatory = $true)] $Connection,
        [Parameter(Mandatory = $true)][string] $Sql,
        [hashtable] $Parameters
    )
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = $Sql
    $cmd.CommandTimeout = 180
    if ($Parameters) {
        foreach ($name in @($Parameters.Keys)) {
            $value = $Parameters[$name]
            if ($value -is [guid]) {
                $typed = $cmd.Parameters.Add($name, [System.Data.SqlDbType]::UniqueIdentifier)
                $typed.Value = $value
            }
            else {
                if ($null -eq $value) { $value = [DBNull]::Value }
                [void]$cmd.Parameters.AddWithValue($name, $value)
            }
        }
    }
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
    $table = New-Object System.Data.DataTable
    [void]$adapter.Fill($table)
    return ,$table
}

function Get-Cell {
    param($Row, [string] $Name)
    if ($null -eq $Row -or $Row -is [DBNull]) { return $null }
    if (-not $Row.Table.Columns.Contains($Name)) { return $null }
    if ($Row[$Name] -is [DBNull]) { return $null }
    return $Row[$Name]
}

function Get-ColumnSet {
    param(
        [Parameter(Mandatory = $true)] $Connection,
        [Parameter(Mandatory = $true)][string] $TableName
    )
    $table = Invoke-FimQuery -Connection $Connection -Sql @"
SELECT c.name
FROM sys.columns AS c
WHERE c.object_id = OBJECT_ID(@tableName)
"@ -Parameters @{ '@tableName' = $TableName }
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $table.Rows) {
        $name = [string](Get-Cell $row 'name')
        if ($name -match '^[A-Za-z_][A-Za-z0-9_]*$') { [void]$set.Add($name) }
    }
    return $set
}

function ConvertFrom-MimAnchor {
    param($Value)
    if ($null -eq $Value -or $Value -is [DBNull]) { return $null }

    $bytes = $null
    if ($Value -is [byte[]]) {
        $bytes = $Value
    }
    elseif ($Value.GetType().Name -eq 'SqlBinary') {
        $raw = $Value.Value
        if ($raw -is [byte[]]) { $bytes = $raw }
    }
    if (-not $bytes -or $bytes.Length -eq 0) { return $null }

    # Binarni GUID (16 bajtu). EIDMMD ho nema, anchor je retezec.
    if ($bytes.Length -eq 16) {
        return ([guid]::new($bytes)).ToString('D')
    }

    # String anchor: Int32 delka v bajtech (vcetne koncove nuly) + UTF-16.
    if ($bytes.Length -ge 6) {
        $length = [BitConverter]::ToInt32($bytes, 0)
        $payload = $bytes.Length - 4
        if ($length -gt 0 -and $length -le $payload -and ($length % 2) -eq 0) {
            $text = [Text.Encoding]::Unicode.GetString($bytes, 4, $length).TrimEnd([char]0).Trim()
            if (-not [string]::IsNullOrWhiteSpace($text)) { return $text }
        }
    }

    return $null
}

function ConvertTo-EidmId {
    param([string] $Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $text = $Value.Trim().Trim('{}')
    $parsed = [guid]::Empty
    if ([guid]::TryParse($text, [ref]$parsed)) {
        return $parsed.ToString('D').ToUpperInvariant()
    }
    return $text
}

function Get-EidmIdFromCs {
    param($Row, [string] $AnchorColumn, [string] $RdnColumn)
    $fromAnchor = ConvertTo-EidmId (ConvertFrom-MimAnchor (Get-Cell $Row $AnchorColumn))
    $fromRdn = ConvertTo-EidmId ([string](Get-Cell $Row $RdnColumn))
    if ($fromAnchor -and $fromRdn -and ($fromAnchor -ne $fromRdn)) {
        return [pscustomobject]@{ Id = $fromAnchor; Source = 'anchor'; DiffersFromRdn = $true; Rdn = $fromRdn }
    }
    if ($fromAnchor) {
        return [pscustomobject]@{ Id = $fromAnchor; Source = 'anchor'; DiffersFromRdn = $false; Rdn = $fromRdn }
    }
    if ($fromRdn) {
        return [pscustomobject]@{ Id = $fromRdn; Source = 'rdn'; DiffersFromRdn = $false; Rdn = $fromRdn }
    }
    return $null
}

function ConvertTo-JsonString {
    param([string] $Value)
    if ($null -eq $Value) { return 'null' }
    $escaped = $Value.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
    return '"' + $escaped + '"'
}

function New-ContractExportJson {
    param(
        [Parameter(Mandatory = $true)][string] $IdmObjectId,
        [Parameter(Mandatory = $true)][int] $TypeId,
        [Parameter(Mandatory = $true)][string] $AttributeName,
        [Parameter(Mandatory = $true)][string[]] $ManagerIds
    )
    if ($AttributeName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "Nazev atributu manager neni bezpecny pro JSON: $AttributeName"
    }
    $joined = @($ManagerIds | Select-Object -Unique) -join ';'
    return '{{"IDMObjectTypeId":{0},"IDMObjectId":{1},"Attributes":{{{2}:{3}}}}}' -f `
        $TypeId, (ConvertTo-JsonString $IdmObjectId), (ConvertTo-JsonString $AttributeName), (ConvertTo-JsonString $joined)
}

function Read-ConnectorPortalSettings {
    param([Parameter(Mandatory = $true)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "XML konektoru neexistuje: $Path"
    }
    $doc = New-Object System.Xml.XmlDocument
    $doc.XmlResolver = $null
    $doc.Load($Path)

    $baseNode = $doc.SelectSingleNode("//*[local-name()='Base']")
    $secretNode = $doc.SelectSingleNode("//*[local-name()='ClientSecret']")
    $exportNode = $doc.SelectSingleNode("//*[local-name()='Export']/*[local-name()='URL'][@operationType='Full']")

    return [pscustomobject]@{
        BaseUrl    = $(if ($baseNode) { $baseNode.InnerText.Trim() } else { $null })
        Secret     = $(if ($secretNode) { $secretNode.InnerText.Trim() } else { $null })
        ExportPath = $(if ($exportNode) { $exportNode.InnerText.Trim() } else { $null })
    }
}

function Invoke-EidmHttp {
    param(
        [Parameter(Mandatory = $true)][string] $Method,
        [Parameter(Mandatory = $true)][string] $Url,
        [string] $BearerToken,
        [string] $Body,
        [string] $ContentType
    )
    $request = [System.Net.HttpWebRequest]::Create($Url)
    $request.Method = $Method
    $request.Timeout = 1500000
    $request.Accept = 'application/json'
    if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
        $request.Headers['Authorization'] = "Bearer $BearerToken"
    }
    if ($PSBoundParameters.ContainsKey('Body') -and $null -ne $Body) {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Body)
        # Bez charset. Portal nektere POST s charset odmitne.
        $request.ContentType = $ContentType
        $request.ContentLength = $bytes.Length
        $stream = $request.GetRequestStream()
        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Close() }
    }

    $response = $null
    try {
        $response = $request.GetResponse()
    }
    catch [System.Net.WebException] {
        $response = $_.Exception.Response
        if (-not $response) { throw }
    }

    try {
        $status = [int]$response.StatusCode
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream(), [Text.Encoding]::UTF8)
        try { $text = $reader.ReadToEnd() } finally { $reader.Close() }
        $media = ''
        if ($response.ContentType) { $media = [string]$response.ContentType }
        return [pscustomobject]@{ StatusCode = $status; Body = $text; ContentType = $media }
    }
    finally {
        $response.Close()
    }
}

function Get-EidmToken {
    param(
        [Parameter(Mandatory = $true)][string] $BaseUrl,
        [Parameter(Mandatory = $true)][string] $Secret
    )
    $tokenUrl = ($BaseUrl.TrimEnd('/')) + '/connect/token'
    $form = 'client_id=EasyIDMPortalVNext&grant_type=client_credentials&scope=easyidm_portal&client_secret=' +
        [Uri]::EscapeDataString($Secret)
    $result = Invoke-EidmHttp -Method 'POST' -Url $tokenUrl -Body $form -ContentType 'application/x-www-form-urlencoded'
    if ($result.StatusCode -lt 200 -or $result.StatusCode -ge 300) {
        throw "Token se nepodarilo ziskat (HTTP $($result.StatusCode))."
    }
    $parsed = $result.Body | ConvertFrom-Json
    $token = $null
    if ($parsed.PSObject.Properties['access_token']) { $token = [string]$parsed.access_token }
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Portal vratil prazdny access_token."
    }
    $expiresIn = 300
    if ($parsed.PSObject.Properties['expires_in']) {
        $parsedSeconds = 0
        if ([int]::TryParse([string]$parsed.expires_in, [ref]$parsedSeconds) -and $parsedSeconds -gt 0) {
            $expiresIn = $parsedSeconds
        }
    }
    # Stejne jako konektor: 30 s rezerva, at request nedorazi s tokenem, ktery mezitim vyprsi.
    $lifetime = [Math]::Max(30, $expiresIn - 30)
    return [pscustomobject]@{
        AccessToken = $token
        ExpiresAt   = [datetime]::UtcNow.AddSeconds($lifetime)
    }
}

function Get-CurrentEidmToken {
    param(
        [Parameter(Mandatory = $true)][string] $BaseUrl,
        [Parameter(Mandatory = $true)][string] $Secret,
        [switch] $Force
    )
    $stillValid = $script:EidmToken -and ([datetime]::UtcNow -lt $script:EidmTokenExpires)
    if ($Force -or -not $stillValid) {
        if ($script:EidmToken -and -not $Force) {
            Write-Log "Token brzy vyprsi, ziskavam novy."
        }
        $fresh = Get-EidmToken -BaseUrl $BaseUrl -Secret $Secret
        $script:EidmToken = $fresh.AccessToken
        $script:EidmTokenExpires = $fresh.ExpiresAt
    }
    return $script:EidmToken
}

function Test-EidmAuthFailure {
    param($Result)
    if ($Result.StatusCode -eq 401) { return $true }
    if ($Result.ContentType -match 'text/html') { return $true }
    $body = [string]$Result.Body
    if ($body.TrimStart().StartsWith('<')) { return $true }
    return $false
}

function Get-RowLabel {
    param($Row)
    foreach ($column in @('mv_displayName', 'mv_name', 'mv_ebeln', 'mv_accountName', 'mv_techName')) {
        $text = [string](Get-Cell $Row $column)
        if (-not [string]::IsNullOrWhiteSpace($text)) { return $text }
    }
    return ''
}

# Dot-source jen kvuli testu funkci. Normalni spusteni pokracuje.
if ($MyInvocation.InvocationName -eq '.') { return }

if (-not [string]::IsNullOrWhiteSpace($ConnectorConfig)) {
    $fromFile = Read-ConnectorPortalSettings -Path $ConnectorConfig
    if (-not $PSBoundParameters.ContainsKey('PortalBaseUrl') -and $fromFile.BaseUrl) {
        $PortalBaseUrl = $fromFile.BaseUrl
    }
    if (-not $PSBoundParameters.ContainsKey('ClientSecret') -and $fromFile.Secret) {
        $ClientSecret = $fromFile.Secret
    }
    if (-not $PSBoundParameters.ContainsKey('ExportPath') -and $fromFile.ExportPath) {
        $ExportPath = $fromFile.ExportPath
    }
}

$info = Get-FimSyncConnectionInfo
Write-Log ("Pripojuji se k {0}, databaze {1}" -f $info.Server, $info.Database)

$cs = "Server={0};Database={1};Integrated Security=True;Connection Timeout=15" -f $info.Server, $info.Database
$connection = New-Object System.Data.SqlClient.SqlConnection $cs
$connection.Open()
try {
    $csColumns = Get-ColumnSet -Connection $connection -TableName 'dbo.mms_connectorspace'
    $mvColumns = Get-ColumnSet -Connection $connection -TableName 'dbo.mms_metaverse'
    foreach ($required in @('rdn', 'ma_id', 'object_id')) {
        if (-not $csColumns.Contains($required)) {
            throw "mms_connectorspace nema sloupec $required."
        }
    }

    $maTable = Invoke-FimQuery -Connection $connection -Sql @"
SELECT ma_id, ma_name
FROM dbo.mms_management_agent WITH (NOLOCK)
WHERE ma_name COLLATE Latin1_General_CI_AS = @maName
"@ -Parameters @{ '@maName' = $ManagementAgent }
    if ($maTable.Rows.Count -eq 0) {
        $known = Invoke-FimQuery -Connection $connection -Sql "SELECT ma_name FROM dbo.mms_management_agent WITH (NOLOCK) ORDER BY ma_name"
        $names = @($known.Rows | ForEach-Object { [string](Get-Cell $_ 'ma_name') }) -join ', '
        throw "Management agent '$ManagementAgent' v databazi neni. Nalezeno: $names"
    }
    if ($maTable.Rows.Count -gt 1) {
        throw "Nazev management agenta '$ManagementAgent' sedi na vic radku."
    }
    $maId = [guid](Get-Cell $maTable.Rows[0] 'ma_id')
    Write-Log ("MA {0}: {1}" -f $ManagementAgent, $maId)

    $anchorSelect = 'CAST(NULL AS varbinary(800)) AS anchor'
    $anchorColumn = 'anchor'
    if ($csColumns.Contains('anchor')) { $anchorSelect = 'cs.anchor AS anchor' }
    $orderConnector = 'cs.object_id'
    if ($csColumns.Contains('is_connector')) {
        $orderConnector = 'CASE WHEN cs.is_connector = 1 THEN 0 ELSE 1 END, cs.object_id'
    }

    $labelSelect = New-Object System.Text.StringBuilder
    foreach ($pair in @(
            @{ Column = 'displayName'; Alias = 'mv_displayName' },
            @{ Column = 'name'; Alias = 'mv_name' },
            @{ Column = 'ebeln'; Alias = 'mv_ebeln' },
            @{ Column = 'accountName'; Alias = 'mv_accountName' },
            @{ Column = 'techName'; Alias = 'mv_techName' }
        )) {
        if ($mvColumns.Contains($pair.Column)) {
            [void]$labelSelect.Append((', mv.[{0}] AS {1}' -f $pair.Column, $pair.Alias))
        }
    }

    $sql = @"
SELECT
    mv.object_id AS contract_id,
    lnk.reference_id,
    contract_cs.rdn AS contract_rdn,
    contract_cs.anchor AS contract_anchor,
    contract_cs.link_count AS contract_links,
    ref_cs.rdn AS ref_rdn,
    ref_cs.anchor AS ref_anchor,
    ref_cs.link_count AS ref_links
    $($labelSelect.ToString())
FROM dbo.mms_metaverse AS mv WITH (NOLOCK)
INNER JOIN dbo.mms_mv_link AS lnk WITH (NOLOCK)
    ON lnk.object_id = mv.object_id
   AND lnk.attribute_name COLLATE Latin1_General_CI_AS = @attribute
OUTER APPLY (
    SELECT TOP 1 cs.rdn, $anchorSelect, links.link_count
    FROM dbo.mms_csmv_link AS link WITH (NOLOCK)
    INNER JOIN dbo.mms_connectorspace AS cs WITH (NOLOCK)
        ON cs.object_id = link.cs_object_id
       AND cs.ma_id = @maId
    CROSS APPLY (
        SELECT COUNT(*) AS link_count
        FROM dbo.mms_csmv_link AS link2 WITH (NOLOCK)
        INNER JOIN dbo.mms_connectorspace AS cs2 WITH (NOLOCK)
            ON cs2.object_id = link2.cs_object_id
           AND cs2.ma_id = @maId
        WHERE link2.mv_object_id = mv.object_id
    ) AS links
    WHERE link.mv_object_id = mv.object_id
    ORDER BY $orderConnector
) AS contract_cs
OUTER APPLY (
    SELECT TOP 1 cs.rdn, $anchorSelect, links.link_count
    FROM dbo.mms_csmv_link AS link WITH (NOLOCK)
    INNER JOIN dbo.mms_connectorspace AS cs WITH (NOLOCK)
        ON cs.object_id = link.cs_object_id
       AND cs.ma_id = @maId
    CROSS APPLY (
        SELECT COUNT(*) AS link_count
        FROM dbo.mms_csmv_link AS link2 WITH (NOLOCK)
        INNER JOIN dbo.mms_connectorspace AS cs2 WITH (NOLOCK)
            ON cs2.object_id = link2.cs_object_id
           AND cs2.ma_id = @maId
        WHERE link2.mv_object_id = lnk.reference_id
    ) AS links
    WHERE link.mv_object_id = lnk.reference_id
    ORDER BY $orderConnector
) AS ref_cs
WHERE mv.object_type COLLATE Latin1_General_CI_AS = @objectType
"@

    $rows = Invoke-FimQuery -Connection $connection -Sql $sql -Parameters @{
        '@attribute'  = $ReferenceAttribute
        '@objectType' = $ObjectType
        '@maId'       = $maId
    }
}
finally {
    $connection.Close()
}

if ($rows.Rows.Count -eq 0) {
    Write-Log ("Zadny objekt typu '{0}' nema reference atribut '{1}'." -f $ObjectType, $ReferenceAttribute) -Level WARN
    exit 0
}

$byContract = @{}
foreach ($row in $rows.Rows) {
    $id = ([guid](Get-Cell $row 'contract_id')).ToString('D').ToUpperInvariant()
    if (-not $byContract.ContainsKey($id)) {
        $byContract[$id] = [System.Collections.Generic.List[object]]::new()
    }
    $byContract[$id].Add($row)
}

$multi = New-Object System.Collections.Generic.List[object]
foreach ($entry in @($byContract.GetEnumerator())) {
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in @($entry.Value)) {
        [void]$seen.Add(([guid](Get-Cell $row 'reference_id')).ToString('D'))
    }
    if ($seen.Count -gt 1) { $multi.Add($entry) }
}
$multi = @($multi | Sort-Object Key)
Write-Log ("Objektu s {0}: {1}, z toho s vice nez jednou hodnotou: {2}" -f $ReferenceAttribute, $byContract.Count, $multi.Count)
if ($multi.Count -eq 0) {
    Write-Log "Neni co posilat."
    exit 0
}

$payloads = New-Object System.Collections.Generic.List[object]
$skipped = 0
foreach ($entry in $multi) {
    $group = @($entry.Value)
    $first = $group[0]
    $label = Get-RowLabel $first
    $contractCs = Get-EidmIdFromCs -Row $first -AnchorColumn 'contract_anchor' -RdnColumn 'contract_rdn'
    $linkCount = Get-Cell $first 'contract_links'
    if ($linkCount -and [int]$linkCount -gt 1) {
        Write-Log ("MV {0} ma na {1} {2} connector space objektu. Bere se prvni connector." -f $entry.Key, $ManagementAgent, $linkCount) -Level WARN
    }
    if (-not $contractCs) {
        $skipped++
        Write-Log ("Preskakuji {0} {1}: na {2} nema objekt." -f $entry.Key, $label, $ManagementAgent) -Level WARN
        continue
    }
    if ($contractCs.DiffersFromRdn) {
        Write-Log ("MV {0}: anchor {1} a RDN {2} se lisi, do portalu jde anchor." -f $entry.Key, $contractCs.Id, $contractCs.Rdn) -Level WARN
    }

    $managers = New-Object System.Collections.Generic.List[string]
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($row in $group) {
        $refId = ([guid](Get-Cell $row 'reference_id')).ToString('D').ToUpperInvariant()
        $refCs = Get-EidmIdFromCs -Row $row -AnchorColumn 'ref_anchor' -RdnColumn 'ref_rdn'
        if (-not $refCs) {
            [void]$missing.Add($refId)
            continue
        }
        if (-not $managers.Contains($refCs.Id)) { [void]$managers.Add($refCs.Id) }
    }

    if ($missing.Count -gt 0) {
        Write-Log ("{0}: {1} odkazu {2} nema objekt na {3}: {4}" -f $contractCs.Id, $missing.Count, $ReferenceAttribute, $ManagementAgent, ($missing -join ', ')) -Level WARN
    }
    if ($managers.Count -eq 0) {
        $skipped++
        Write-Log ("Preskakuji {0}: zadny odkaz nema id na {1}." -f $contractCs.Id, $ManagementAgent) -Level WARN
        continue
    }

    $json = New-ContractExportJson -IdmObjectId $contractCs.Id -TypeId $IdmObjectTypeId -AttributeName $ManagerAttribute -ManagerIds @($managers)
    $payloads.Add([pscustomobject]@{
        MetaverseId = $entry.Key
        Label       = $label
        IdmObjectId = $contractCs.Id
        Managers    = @($managers)
        Missing     = @($missing)
        Json        = $json
    })
}

Write-Log ("K odeslani: {0}, preskoceno: {1}" -f $payloads.Count, $skipped)
if ($payloads.Count -eq 0) {
    exit 1
}

$outDir = Join-Path $PSScriptRoot 'Reports'
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$jsonPath = Join-Path $outDir ("ContractManager_{0}.json" -f $stamp)
$utf8 = New-Object System.Text.UTF8Encoding $true
$bundle = @(foreach ($item in $payloads) { $item.Json }) -join ",`r`n"
[System.IO.File]::WriteAllText($jsonPath, "[$bundle]", $utf8)
Write-Log "JSON: $jsonPath"

$exportUrl = ''
if (-not [string]::IsNullOrWhiteSpace($PortalBaseUrl)) {
    $exportUrl = ($PortalBaseUrl.TrimEnd('/')) + '/' + $ExportPath.TrimStart('/') + '/' + $IdmObjectTypeId
}
$send = $PSCmdlet.ShouldProcess($(if ($exportUrl) { $exportUrl } else { 'portal' }), ("POST {0} contract objektu" -f $payloads.Count))
if (-not $send) {
    Write-Log "WhatIf: na portal se nic neposlalo."
    if ($skipped -gt 0) { exit 1 }
    exit 0
}
if ([string]::IsNullOrWhiteSpace($PortalBaseUrl) -or [string]::IsNullOrWhiteSpace($ClientSecret)) {
    throw "Chybi PortalBaseUrl nebo ClientSecret. Zadejte je, nebo -ConnectorConfig s Base a ClientSecret."
}

$script:EidmToken = $null
$script:EidmTokenExpires = [datetime]::MinValue
$token = Get-CurrentEidmToken -BaseUrl $PortalBaseUrl -Secret $ClientSecret
Write-Log ("Posilam na {0}" -f $exportUrl)

$ok = 0
$failed = 0
$logPath = Join-Path $outDir ("ContractManager_{0}.log.json" -f $stamp)
$log = New-Object System.Collections.Generic.List[object]
foreach ($item in $payloads) {
    $token = Get-CurrentEidmToken -BaseUrl $PortalBaseUrl -Secret $ClientSecret
    $result = Invoke-EidmHttp -Method 'POST' -Url $exportUrl -BearerToken $token -Body ('[' + $item.Json + ']') -ContentType 'application/json'
    if (Test-EidmAuthFailure $result) {
        Write-Log ("Token portal odmitl (HTTP {0}) u {1}, ziskavam novy a zkousim znovu." -f $result.StatusCode, $item.IdmObjectId) -Level WARN
        $token = Get-CurrentEidmToken -BaseUrl $PortalBaseUrl -Secret $ClientSecret -Force
        $result = Invoke-EidmHttp -Method 'POST' -Url $exportUrl -BearerToken $token -Body ('[' + $item.Json + ']') -ContentType 'application/json'
    }
    $html = Test-EidmAuthFailure $result
    $success = (-not $html) -and $result.StatusCode -ge 200 -and $result.StatusCode -lt 300
    if ($success) {
        $ok++
        Write-Log ("OK {0} manager={1} HTTP {2}" -f $item.IdmObjectId, $item.Managers.Count, $result.StatusCode)
    }
    else {
        $failed++
        $preview = $result.Body
        if ($preview.Length -gt 500) { $preview = $preview.Substring(0, 500) }
        Write-Log ("CHYBA {0} HTTP {1}: {2}" -f $item.IdmObjectId, $result.StatusCode, $preview) -Level ERROR
    }
    $log.Add([pscustomobject]@{
        idmObjectId = $item.IdmObjectId
        metaverseId = $item.MetaverseId
        label       = $item.Label
        managers    = @($item.Managers)
        missing     = @($item.Missing)
        status      = $result.StatusCode
        success     = $success
        response    = $result.Body
    })
}

[System.IO.File]::WriteAllText($logPath, ($log | ConvertTo-Json -Depth 5), $utf8)
Write-Log "Odpovedi: $logPath"
Write-Log ("Hotovo. Odeslano: {0}, chyb: {1}, preskoceno: {2}" -f $ok, $failed, $skipped)
if ($failed -gt 0 -or $skipped -gt 0) { exit 1 }
exit 0
