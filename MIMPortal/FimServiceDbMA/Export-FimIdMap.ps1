#Requires -Version 5.1
<#
.SYNOPSIS
    Vyexportuje jen mapování ObjectID a AccountName z databáze FIMService.

.DESCRIPTION
    Bez parametru čte Person a Group podle AccountName a RoleApplication
    podle DisplayName. DisplayName se zapíše do stejného sloupce, aby šla
    reference přeložit mezi prostředími. Objekty bez mapovací hodnoty se vynechají.
    Soubor se nepřepisuje. New-MissingGroups.ps1 ho použije jako -ProdIds
    nebo -TestIds, případně sám vezme nejnovější FimMap-*.ids.txt v Data.

.PARAMETER Server
    SQL Server s databází FIMService.

.PARAMETER Database
    Název databáze. Výchozí je FIMService.

.PARAMETER Environment
    Jméno prostředí v názvu souboru, například PROD nebo Test.

.PARAMETER ObjectType
    Typy objektů. Výchozí je Person, Group a RoleApplication.

.PARAMETER OutputPath
    Cílový soubor. Výchozí je ..\Data\FimMap-<Environment>-<čas>.ids.txt,
    když složka Data vedle skriptu existuje. Jinak se zapíše do aktuální složky.

.EXAMPLE
    .\Export-FimIdMap.ps1 -Server MIMSQL -Environment PROD

.EXAMPLE
    .\Export-FimIdMap.ps1 -Server MIMSQL-TEST -Environment Test -ConnectionOptions 'Encrypt=True;TrustServerCertificate=True'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Server,

    [string] $Database = 'FIMService',

    [string] $User,

    [string] $Password,

    [string] $ConnectionOptions,

    [Parameter(Mandatory = $true)]
    [string] $Environment,

    [string[]] $ObjectType = @('Person', 'Group', 'RoleApplication'),

    [string] $OutputPath,

    [int] $CommandTimeout = 0,

    [int] $LocaleKey = 127
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

function Connect-FimDatabase {
    if (-not [string]::IsNullOrWhiteSpace($User) -and [string]::IsNullOrEmpty($Password)) {
        $secure = Read-Host -Prompt "Heslo pro SQL login '$User'" -AsSecureString
        $Password = (New-Object System.Net.NetworkCredential('', $secure)).Password
    }

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server.Trim()
    $builder['Initial Catalog'] = $Database.Trim()
    $builder['Application Name'] = 'FimServiceDbMA-IdMap'
    $builder['Connect Timeout'] = 30
    $builder['Pooling'] = $false

    if ([string]::IsNullOrWhiteSpace($User)) {
        $builder['Integrated Security'] = $true
    }
    else {
        $builder['User ID'] = $User.Trim()
        $builder['Password'] = $Password
    }

    $connectionString = $builder.ConnectionString
    if (-not [string]::IsNullOrWhiteSpace($ConnectionOptions)) {
        $extra = $ConnectionOptions.Trim().TrimStart(';')
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder ($connectionString + ';' + $extra)
        $connectionString = $builder.ConnectionString
    }

    $conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandTimeout = 30
    $cmd.CommandText = 'SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED'
    [void] $cmd.ExecuteNonQuery()
    $cmd.Dispose()
    return $conn
}

function Invoke-FimReader {
    param(
        [System.Data.SqlClient.SqlConnection] $Connection,
        [string] $Sql,
        [scriptblock] $Bind,
        [scriptblock] $Read
    )
    $cmd = $Connection.CreateCommand()
    $cmd.CommandTimeout = $CommandTimeout
    $cmd.CommandText = $Sql
    if ($Bind) { & $Bind $cmd }
    $reader = $cmd.ExecuteReader()
    try {
        while ($reader.Read()) { & $Read $reader }
    }
    finally {
        $reader.Dispose()
        $cmd.Dispose()
    }
}

function Add-SqlParameter {
    param($Command, [string] $Name, [System.Data.SqlDbType] $Type, $Value)
    $p = $Command.Parameters.Add($Name, $Type)
    $p.Value = $Value
}

function Format-IdMapField {
    param($Value)
    if ([string]::IsNullOrEmpty([string] $Value)) { return '' }
    return ([string] $Value -replace '[\r\n\t]', ' ').Trim()
}

function Get-MappingAttributeName {
    param([string] $TypeName)
    if ($TypeName -eq 'RoleApplication') { return 'DisplayName' }
    return 'AccountName'
}

function Get-StringValueTable {
    param([string] $DataType)
    switch ($DataType) {
        'String' { return @{ Table = 'ObjectValueString'; Column = 'ValueString' } }
        'Text'   { return @{ Table = 'ObjectValueText'; Column = 'ValueText' } }
        default  { throw "Mapovaci atribut ma typ '$DataType'. Ocekavam String nebo Text." }
    }
}

$ObjectType = @($ObjectType | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
if ($ObjectType.Count -eq 0) { throw 'Zadejte aspon jeden -ObjectType.' }

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [System.Globalization.CultureInfo]::InvariantCulture)
    $safeEnv = ($Environment -replace '[^\w\-]', '_')
    $fileName = 'FimMap-{0}-{1}.ids.txt' -f $safeEnv, $stamp
    $dataDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Data'))
    if (Test-Path -LiteralPath $dataDir) {
        $OutputPath = Join-Path $dataDir $fileName
    }
    else {
        $OutputPath = Join-Path (Get-Location) $fileName
    }
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) {
    throw "Soubor uz existuje a neprepisuji ho: $OutputPath"
}

Write-Host ("Pripojuji {0} / {1} ({2})..." -f $Server, $Database, $Environment)
$conn = Connect-FimDatabase
try {
    $script:foundTypes = @{}
    $typeParams = @()
    for ($i = 0; $i -lt $ObjectType.Count; $i++) { $typeParams += ('@t' + $i) }
    $typeSql = "SELECT [oti].[Name] FROM [fim].[ObjectTypeInternal] AS [oti] WHERE [oti].[Name] IN ($($typeParams -join ', '))"
    Invoke-FimReader -Connection $conn -Sql $typeSql -Bind {
        param($cmd)
        for ($i = 0; $i -lt $ObjectType.Count; $i++) {
            Add-SqlParameter $cmd ('@t' + $i) ([System.Data.SqlDbType]::NVarChar) $ObjectType[$i]
        }
    } -Read {
        param($r)
        if (-not $r.IsDBNull(0)) { $script:foundTypes[$r.GetString(0)] = $true }
    }
    $missingTypes = @($ObjectType | Where-Object { -not $script:foundTypes.ContainsKey($_) })
    if ($missingTypes.Count -gt 0) {
        throw ("V [fim].[ObjectTypeInternal] chybi object type: " + ($missingTypes -join ', '))
    }

    $neededAttributes = @($ObjectType | ForEach-Object { Get-MappingAttributeName $_ } | Select-Object -Unique)
    $script:attrMeta = @{}
    $attrParams = @()
    for ($i = 0; $i -lt $neededAttributes.Count; $i++) { $attrParams += ('@a' + $i) }
    $attrSql = "SELECT [ai].[Key], [ai].[Name], [ai].[DataType] FROM [fim].[AttributeInternal] AS [ai] WHERE [ai].[Name] IN ($($attrParams -join ', '))"
    Invoke-FimReader -Connection $conn -Sql $attrSql -Bind {
        param($cmd)
        for ($i = 0; $i -lt $neededAttributes.Count; $i++) {
            Add-SqlParameter $cmd ('@a' + $i) ([System.Data.SqlDbType]::NVarChar) $neededAttributes[$i]
        }
    } -Read {
        param($r)
        $script:attrMeta[$r.GetString(1)] = @{
            Key      = [int] $r.GetInt16(0)
            DataType = $r.GetString(2).Trim()
        }
    }
    foreach ($attrName in $neededAttributes) {
        if (-not $script:attrMeta.ContainsKey($attrName)) {
            throw "V [fim].[AttributeInternal] neni atribut $attrName."
        }
    }

    $script:byId = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    $script:order = New-Object System.Collections.Generic.List[string]
    $script:duplicateIds = 0
    $script:typeCounts = @{}

    $groups = @{}
    foreach ($typeName in $ObjectType) {
        $attrName = Get-MappingAttributeName $typeName
        if (-not $groups.ContainsKey($attrName)) { $groups[$attrName] = New-Object System.Collections.Generic.List[string] }
        [void] $groups[$attrName].Add($typeName)
    }

    foreach ($pair in $groups.GetEnumerator()) {
        $attrName = [string] $pair.Key
        $types = @($pair.Value)
        $meta = $script:attrMeta[$attrName]
        $valueTable = Get-StringValueTable $meta.DataType
        $localTypeParams = @()
        for ($i = 0; $i -lt $types.Count; $i++) { $localTypeParams += ('@t' + $i) }
        $localeFilter = 'AND [v].[LocaleKey] = @localeKey'
        $localeOrder = '[v].[SequenceID]'
        if ($attrName -eq 'DisplayName') {
            $localeFilter = ''
            $localeOrder = 'CASE WHEN [v].[LocaleKey] = @localeKey THEN 0 ELSE 1 END, [v].[LocaleKey], [v].[SequenceID]'
        }
        $sql = @"
SELECT LOWER(CONVERT(nvarchar(36), [o].[ObjectID])), [v].[$($valueTable.Column)], [oti].[Name]
FROM [fim].[Objects] AS [o]
INNER JOIN [fim].[ObjectTypeInternal] AS [oti] ON [oti].[Key] = [o].[ObjectTypeKey]
INNER JOIN [fim].[$($valueTable.Table)] AS [v]
    ON [v].[ObjectKey] = [o].[ObjectKey]
   AND [v].[ObjectTypeKey] = [o].[ObjectTypeKey]
   AND [v].[AttributeKey] = @attrKey
   $localeFilter
WHERE [oti].[Name] IN ($($localTypeParams -join ', '))
  AND [v].[$($valueTable.Column)] IS NOT NULL
  AND [v].[$($valueTable.Column)] <> N''
ORDER BY [o].[ObjectID], $localeOrder
"@
        $script:queryTypes = $types
        $script:queryAttrKey = [int] $meta.Key
        Invoke-FimReader -Connection $conn -Sql $sql -Bind {
            param($cmd)
            Add-SqlParameter $cmd '@attrKey' ([System.Data.SqlDbType]::SmallInt) $script:queryAttrKey
            Add-SqlParameter $cmd '@localeKey' ([System.Data.SqlDbType]::Int) $LocaleKey
            for ($i = 0; $i -lt $script:queryTypes.Count; $i++) {
                Add-SqlParameter $cmd ('@t' + $i) ([System.Data.SqlDbType]::NVarChar) $script:queryTypes[$i]
            }
        } -Read {
            param($r)
            if ($r.IsDBNull(0) -or $r.IsDBNull(1)) { return }
            $id = $r.GetString(0).Trim().ToLowerInvariant()
            $account = Format-IdMapField $r.GetString(1)
            if (-not $id -or -not $account) { return }
            $typeName = ''
            if (-not $r.IsDBNull(2)) { $typeName = $r.GetString(2) }
            if ($script:byId.ContainsKey($id)) {
                if ($script:byId[$id] -ne $account) { $script:duplicateIds++ }
                return
            }
            $script:byId[$id] = $account
            $script:order.Add($id)
            if (-not $script:typeCounts.ContainsKey($typeName)) { $script:typeCounts[$typeName] = 0 }
            $script:typeCounts[$typeName] = [int] $script:typeCounts[$typeName] + 1
        }
    }
}
finally {
    $conn.Dispose()
}

$sorted = New-Object string[] $script:order.Count
$script:order.CopyTo($sorted, 0)
[Array]::Sort($sorted)
$utf8 = New-Object System.Text.UTF8Encoding $false
$writer = New-Object System.IO.StreamWriter ($OutputPath, $false, $utf8)
$seenAccount = @{}
$duplicateAccounts = 0
try {
    $writer.WriteLine("ObjectID`tAccountName")
    foreach ($id in $sorted) {
        $account = $script:byId[$id]
        $key = $account.ToLowerInvariant()
        if ($seenAccount.ContainsKey($key)) { $duplicateAccounts++ }
        else { $seenAccount[$key] = $true }
        $writer.WriteLine(('{0}{1}{2}' -f $id, "`t", $account))
    }
}
finally {
    $writer.Dispose()
}

Write-Host ("Zapsano {0} radku do {1}" -f $sorted.Count, $OutputPath)
foreach ($typeName in ($ObjectType | Sort-Object)) {
    $count = 0
    if ($script:typeCounts.ContainsKey($typeName)) { $count = [int] $script:typeCounts[$typeName] }
    Write-Host ("  {0} podle {1}: {2}" -f $typeName, (Get-MappingAttributeName $typeName), $count)
}
if ($script:duplicateIds -gt 0) { Write-Host ("ObjectID s vice AccountName, dalsi hodnoty vynechany: {0}" -f $script:duplicateIds) }
if ($duplicateAccounts -gt 0) { Write-Host ("Stejny AccountName u vice ObjectID: {0}" -f $duplicateAccounts) }
