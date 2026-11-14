#Requires -Version 5.1
<#
.SYNOPSIS
    Export objektu Person a Group z databaze MIM Service (FIMService) do JSON nebo XML.

.DESCRIPTION
    Cte stejnym zpusobem jako konektor FimServiceDbMA: primo z tabulek [fim].[Objects]
    a [fim].[ObjectValue*], v izolaci READ UNCOMMITTED, takze nezamyka radky bezici
    sluzby. Do databaze nic nezapisuje. Staci db_datareader.

    ObjectID bere z [fim].[Objects].[ObjectID]. Ostatni atributy z hodnotovych tabulek
    podle [fim].[AttributeInternal] a [fim].[BindingInternal]. Nazev atributu musi
    odpovidat nazvu v portalu (na velikosti pismen nezalezi). Atribut, ktery v databazi
    neni, se nevymysli - vypise se ve vystupu v attributesNotInDatabase.

    Reference (Manager, csoblComputedMember a dalsi) se exportuji jako ObjectID
    ciloveho objektu, bez prekladu na AccountName. Premapovani mezi PROD a TEST
    patri az porovnavacimu skriptu. Mapovaci klic samotneho objektu je accountName
    v koreni kazdeho zaznamu.

    Vedle hlavniho exportu vznikne soubor *.ids.txt: tabulka ObjectID, AccountName,
    EmployeeID a DisplayName oddelena tabulatorem, prvni radek je hlavicka.
    Objekty bez AccountName v nem nejsou. EmployeeID a DisplayName mohou byt prazdne.

    Objekty, ktere nemaji ani jednu hodnotu pozadovaneho atributu, se ve vychozim
    stavu preskakuji - stejne jako je preskakuje konektor. -IncludeEmpty je vypise take.

.PARAMETER Server
    SQL Server, napr. MIMSQL, MIMSQL\INST nebo mimsql,1433.

.PARAMETER Database
    Databaze MIM Service. Vychozi je FIMService.

.PARAMETER User
    SQL login. Kdyz neni zadany, pouzije se Windows autentizace beziciho uctu.

.PARAMETER Password
    Heslo k SQL loginu. Kdyz chybi a -User je zadany, skript se na nej zepta.

.PARAMETER ConnectionOptions
    Dalsi cast connection stringu, napr. Encrypt=True;TrustServerCertificate=True
    nebo ApplicationIntent=ReadOnly pri cteni z AlwaysOn repliky.

.PARAMETER Environment
    Oznaceni prostredi (PROD, TEST, ...). Zapise se do vystupu, aby slo exporty
    od sebe odlisit.

.PARAMETER ObjectType
    Object types k exportu. Vychozi je Person a Group.

.PARAMETER Attribute
    Atributy k exportu. Vychozi seznam je ten, ktery se ma porovnavat mezi prostredimi.
    ObjectID se vzdy cte z radku objektu, ne z hodnotove tabulky.

.PARAMETER Format
    Json (vychozi) nebo Xml.

.PARAMETER OutputPath
    Cilovy soubor. Vychozi je .\FimExport-<Environment>-<cas>.json (nebo .xml).
    Existujici soubor se neprepisuje.

.PARAMETER CommandTimeout
    Timeout SQL prikazu v sekundach. 0 = bez limitu. Vychozi je 0, protoze export
    celeho clenstvi skupin muze trvat dele nez bezny importni dotaz konektoru.

.PARAMETER LocaleKey
    Lokalizace textovych hodnot. 127 je invariant, tedy to, co cte konektor a portál.

.PARAMETER IncludeEmpty
    Exportovat i objekty bez jedine hodnoty pozadovanych atributu.

.EXAMPLE
    .\Export-FimObjects.ps1 -Server MIMSQL -Environment PROD

.EXAMPLE
    .\Export-FimObjects.ps1 -Server MIMSQL-TEST -Environment TEST -ConnectionOptions 'Encrypt=True;TrustServerCertificate=True'

.EXAMPLE
    .\Export-FimObjects.ps1 -Server MIMSQL -Environment PROD -User fimreader -OutputPath C:\Export\prod.json
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

    [string[]] $ObjectType = @('Person', 'Group'),

    [string[]] $Attribute = @(
        'ObjectID',
        'csoblTechName',
        'csoblvalidStatus',
        'csoblComputedMember',
        'csoblGroupType',
        'Description',
        'csoblPlacedAD',
        'Department',
        'DisplayName',
        'Email',
        'EmployeeID',
        'EmployeeType',
        'csoblFinalPropetyStatus',
        'csoblGLOWID',
        'csoblValidTo',
        'Manager',
        'AccountName'
    ),

    [ValidateSet('Json', 'Xml')]
    [string] $Format = 'Json',

    [string] $OutputPath,

    [int] $CommandTimeout = 0,

    [int] $LocaleKey = 127,

    [switch] $IncludeEmpty
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$script:Invariant = [System.Globalization.CultureInfo]::InvariantCulture

if (-not ('FimExport.Json' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;

namespace FimExport {
    public static class Json {
        public static string Quote(string value) {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                switch (c) {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static int[] OrderByKey(string[] keys) {
            int n = keys.Length;
            int[] idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            Array.Sort(idx, delegate(int a, int b) {
                return string.CompareOrdinal(keys[a], keys[b]);
            });
            return idx;
        }
    }
}
'@
}

function Format-InvariantInt {
    param($Value)
    return [string] $Value.ToString($script:Invariant)
}

function Format-KeyList {
    param([System.Collections.IEnumerable] $Keys)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($key in $Keys) {
        $number = 0
        if (-not [int]::TryParse([string] $key, [ref] $number)) {
            throw "Klic atributu neni cislo: $key"
        }
        $parts.Add((Format-InvariantInt $number))
    }
    if ($parts.Count -eq 0) {
        throw 'Prazdny seznam klicu atributu.'
    }
    return ($parts -join ',')
}

function ConvertTo-JsonLiteral {
    param($Value)
    if ($null -eq $Value) {
        return 'null'
    }
    if ($Value -is [bool]) {
        if ($Value) { return 'true' } else { return 'false' }
    }
    if ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int32] -or $Value -is [int64]) {
        return Format-InvariantInt $Value
    }

    return [FimExport.Json]::Quote([string] $Value)
}

function Connect-FimDatabase {
    if (-not [string]::IsNullOrWhiteSpace($User) -and [string]::IsNullOrEmpty($Password)) {
        $secure = Read-Host -Prompt "Heslo pro SQL login '$User'" -AsSecureString
        $Password = (New-Object System.Net.NetworkCredential('', $secure)).Password
    }

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server.Trim()
    $builder['Initial Catalog'] = $Database.Trim()
    $builder['Application Name'] = 'FimServiceDbMA-Export'
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
    if ($Bind) {
        & $Bind $cmd
    }

    $reader = $cmd.ExecuteReader()
    try {
        while ($reader.Read()) {
            & $Read $reader
        }
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

function Get-InParameters {
    param([string[]] $Values, [string] $Prefix)
    $names = New-Object System.Collections.Generic.List[string]
    $i = 0
    foreach ($value in $Values) {
        $names.Add('@' + $Prefix + $i)
        $i++
    }
    return ($names -join ',')
}

function Get-FimMetadata {
    param([System.Data.SqlClient.SqlConnection] $Connection)

    $typeSql = @"
SELECT [oti].[Key], [oti].[Name]
FROM [fim].[ObjectTypeInternal] AS [oti]
WHERE [oti].[Name] IN ($(Get-InParameters $ObjectType 't'))
"@
    $types = @{}
    Invoke-FimReader -Connection $Connection -Sql $typeSql -Bind {
        param($cmd)
        for ($i = 0; $i -lt $ObjectType.Count; $i++) {
            Add-SqlParameter $cmd ('@t' + $i) ([System.Data.SqlDbType]::NVarChar) $ObjectType[$i]
        }
    } -Read {
        param($r)
        $types[$r.GetString(1)] = [int] $r.GetInt16(0)
    }

    $typesCi = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($pair in $types.GetEnumerator()) {
        $typesCi[$pair.Key] = @{ Key = [int] $pair.Value; Name = [string] $pair.Key }
    }
    $missingTypes = @($ObjectType | Where-Object { -not $typesCi.ContainsKey($_) })
    if ($missingTypes.Count -gt 0) {
        throw ("V [fim].[ObjectTypeInternal] chybi object type: " + ($missingTypes -join ', '))
    }

    $wanted = @($Attribute | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    $lookupNames = @($wanted | Where-Object { $_ -ne 'ObjectID' })

    $attrsByName = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    if ($lookupNames.Count -gt 0) {
        $attrSql = @"
SELECT [ai].[Key], [ai].[Name], [ai].[DataType], [ai].[Multivalued]
FROM [fim].[AttributeInternal] AS [ai]
WHERE [ai].[Name] IN ($(Get-InParameters $lookupNames 'a'))
"@
        Invoke-FimReader -Connection $Connection -Sql $attrSql -Bind {
            param($cmd)
            for ($i = 0; $i -lt $lookupNames.Count; $i++) {
                Add-SqlParameter $cmd ('@a' + $i) ([System.Data.SqlDbType]::NVarChar) $lookupNames[$i]
            }
        } -Read {
            param($r)
            $name = $r.GetString(1)
            $attrsByName[$name] = @{
                Key         = [int] $r.GetInt16(0)
                Name        = $name
                DataType    = $r.GetString(2).Trim()
                Multivalued = $r.GetBoolean(3)
            }
        }
    }

    $notInDatabase = New-Object System.Collections.Generic.List[string]
    foreach ($name in $lookupNames) {
        if (-not $attrsByName.ContainsKey($name)) {
            $notInDatabase.Add($name)
        }
    }

    if (-not $attrsByName.ContainsKey('AccountName')) {
        throw "V [fim].[AttributeInternal] neni atribut AccountName. Bez nej nema export mapovaci klic."
    }

    $typeKeyList = (Format-KeyList @($typesCi.Values | ForEach-Object { $_.Key }))
    $attrKeyList = (Format-KeyList @($attrsByName.Values | ForEach-Object { $_.Key }))

    $bound = @{}
    $bindSql = @"
SELECT [oti].[Name], [ai].[Name]
FROM [fim].[BindingInternal] AS [bi]
INNER JOIN [fim].[ObjectTypeInternal] AS [oti] ON [oti].[Key] = [bi].[ObjectTypeKey]
INNER JOIN [fim].[AttributeInternal] AS [ai] ON [ai].[Key] = [bi].[AttributeKey]
WHERE [bi].[ObjectTypeKey] IN ($typeKeyList)
  AND [bi].[AttributeKey] IN ($attrKeyList)
"@
    Invoke-FimReader -Connection $Connection -Sql $bindSql -Bind $null -Read {
        param($r)
        $typeName = $r.GetString(0)
        $attrName = $r.GetString(1)
        if (-not $bound.ContainsKey($typeName)) {
            $bound[$typeName] = [System.Collections.Generic.List[object]]::new()
        }
        $bound[$typeName].Add($attrsByName[$attrName])
    }

    return @{
        Types          = $typesCi
        Attributes     = $attrsByName
        NotInDatabase  = $notInDatabase
        Bound          = $bound
        Requested      = $wanted
    }
}

function Add-StoredValue {
    param($Store, [int64] $ObjectKey, [string] $Name, $Value)
    $attrs = $null
    if (-not $Store.TryGetValue($ObjectKey, [ref] $attrs)) {
        $attrs = @{}
        $Store[$ObjectKey] = $attrs
    }
    $list = $attrs[$Name]
    if ($null -eq $list) {
        $list = [System.Collections.Generic.List[object]]::new()
        $attrs[$Name] = $list
    }
    $list.Add($Value)
}

function Get-ValueTable {
    param([string] $DataType)
    switch ($DataType) {
        'String'     { return @{ Table = 'ObjectValueString';   Column = 'ValueString';     Localized = $true;  Kind = 'text';   HasSeq = $true } }
        'Text'       { return @{ Table = 'ObjectValueText';     Column = 'ValueText';       Localized = $true;  Kind = 'text';   HasSeq = $true } }
        'Xml'        { return @{ Table = 'ObjectValueXml';      Column = 'ValueXml';        Localized = $true;  Kind = 'text';   HasSeq = $true } }
        'Integer'    { return @{ Table = 'ObjectValueInteger';  Column = 'ValueInteger';    Localized = $false; Kind = 'number'; HasSeq = $true } }
        'Boolean'    { return @{ Table = 'ObjectValueBoolean';  Column = 'ValueBoolean';    Localized = $false; Kind = 'bool';   HasSeq = $false } }
        'DateTime'   { return @{ Table = 'ObjectValueDateTime'; Column = 'ValueDateTime';   Localized = $false; Kind = 'text';   HasSeq = $true } }
        'Identifier' { return @{ Table = 'ObjectValueIdentifier'; Column = 'ValueIdentifier'; Localized = $false; Kind = 'text'; HasSeq = $false } }
        'Reference'  { return @{ Table = 'ObjectValueReference'; Column = $null;            Localized = $false; Kind = 'ref';    HasSeq = $false } }
        default      { return $null }
    }
}

function Get-HasValueSql {
    param($BoundAttributes)
    $groups = @{}
    foreach ($attr in $BoundAttributes) {
        $spec = Get-ValueTable $attr.DataType
        if ($null -eq $spec) { continue }
        if (-not $groups.ContainsKey($spec.Table)) {
            $groups[$spec.Table] = @{ Spec = $spec; Keys = (New-Object System.Collections.Generic.List[int]) }
        }
        $groups[$spec.Table].Keys.Add([int] $attr.Key)
    }

    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($pair in $groups.GetEnumerator()) {
        $spec = $pair.Value.Spec
        $keys = Format-KeyList $pair.Value.Keys
        if ($spec.Kind -eq 'ref') {
            $parts.Add(@"
SELECT [v].[ObjectKey]
FROM [fim].[ObjectValueReference] AS [v]
INNER JOIN [fim].[Objects] AS [tgt] ON [tgt].[ObjectKey] = [v].[ValueReference]
WHERE [v].[ObjectTypeKey] = @typeKey AND [v].[AttributeKey] IN ($keys)
"@)
            continue
        }

        $extra = ''
        $valueExpr = "[v].[$($spec.Column)]"
        if ($spec.Localized) {
            $extra = ' AND [v].[LocaleKey] = @localeKey'
        }
        if ($spec.Table -eq 'ObjectValueString' -or $spec.Table -eq 'ObjectValueText') {
            $extra += " AND $valueExpr <> N''"
        }
        elseif ($spec.Table -eq 'ObjectValueXml') {
            $extra += " AND DATALENGTH($valueExpr) > 0"
        }

        $parts.Add(@"
SELECT [v].[ObjectKey]
FROM [fim].[$($spec.Table)] AS [v]
WHERE [v].[ObjectTypeKey] = @typeKey AND [v].[AttributeKey] IN ($keys)$extra
"@)
    }

    if ($parts.Count -eq 0) { return $null }
    return ($parts -join ' UNION ')
}

function Read-FimObjects {
    param(
        [System.Data.SqlClient.SqlConnection] $Connection,
        [int] $TypeKey,
        [string] $HasValueSql
    )

    $sql = "SELECT [o].[ObjectKey], [o].[ObjectID] FROM [fim].[Objects] AS [o] WHERE [o].[ObjectTypeKey] = @typeKey"
    if ($HasValueSql) {
        $sql += " AND [o].[ObjectKey] IN ($HasValueSql)"
    }
    $sql += ' ORDER BY [o].[ObjectKey]'

    $list = [System.Collections.Generic.List[object]]::new()
    Invoke-FimReader -Connection $Connection -Sql $sql -Bind {
        param($cmd)
        Add-SqlParameter $cmd '@typeKey' ([System.Data.SqlDbType]::SmallInt) $TypeKey
        if ($HasValueSql -and $HasValueSql.Contains('@localeKey')) {
            Add-SqlParameter $cmd '@localeKey' ([System.Data.SqlDbType]::SmallInt) $LocaleKey
        }
    } -Read {
        param($r)
        $list.Add(@{ Key = $r.GetInt64(0); ObjectId = $r.GetGuid(1).ToString('D') })
    }
    return $list
}

function Read-FimValues {
    param(
        [System.Data.SqlClient.SqlConnection] $Connection,
        [int] $TypeKey,
        $BoundAttributes
    )

    $store = New-Object 'System.Collections.Generic.Dictionary[System.Int64,System.Object]'
    $byTable = @{}
    foreach ($attr in $BoundAttributes) {
        $spec = Get-ValueTable $attr.DataType
        if ($null -eq $spec) {
            Write-Warning "Atribut $($attr.Name) ma neznamy DataType '$($attr.DataType)' a nebude ve vystupu."
            continue
        }
        if (-not $byTable.ContainsKey($spec.Table)) {
            $byTable[$spec.Table] = @{ Spec = $spec; Attrs = ([System.Collections.Generic.List[object]]::new()) }
        }
        $byTable[$spec.Table].Attrs.Add($attr)
    }

    $nameByKey = @{}
    foreach ($attr in $BoundAttributes) { $nameByKey[[int] $attr.Key] = $attr.Name }

    foreach ($pair in $byTable.GetEnumerator()) {
        $spec = $pair.Value.Spec
        $keys = Format-KeyList @($pair.Value.Attrs | ForEach-Object { $_.Key })
        Write-Host ("  hodnoty {0} ({1})" -f $spec.Table, $keys)

        if ($spec.Kind -eq 'ref') {
            # Jen ObjectID cile. AccountName se nedohledava, premapovani je na porovnavacim skriptu.
            $sql = @"
SELECT [v].[ObjectKey], [v].[AttributeKey], CONVERT(NVARCHAR(36), [tgt].[ObjectID])
FROM [fim].[ObjectValueReference] AS [v]
INNER JOIN [fim].[Objects] AS [tgt] ON [tgt].[ObjectKey] = [v].[ValueReference]
WHERE [v].[ObjectTypeKey] = @typeKey AND [v].[AttributeKey] IN ($keys)
"@
            Invoke-FimReader -Connection $Connection -Sql $sql -Bind {
                param($cmd)
                Add-SqlParameter $cmd '@typeKey' ([System.Data.SqlDbType]::SmallInt) $TypeKey
            } -Read {
                param($r)
                if ($r.IsDBNull(2)) { return }
                $objectId = $r.GetString(2).ToLowerInvariant()
                Add-StoredValue $store $r.GetInt64(0) $nameByKey[[int] $r.GetInt16(1)] ([pscustomobject]@{
                    Seq     = 0
                    Kind    = 'text'
                    Text    = $objectId
                    SortKey = $objectId
                })
            }
            continue
        }

        $column = $spec.Column
        $selectValue = "CAST([v].[$column] AS NVARCHAR(MAX))"
        if ($spec.Kind -eq 'bool') {
            $selectValue = "CASE WHEN [v].[$column] = 1 THEN N'true' ELSE N'false' END"
        }
        elseif ($spec.Table -eq 'ObjectValueDateTime') {
            $selectValue = "CONVERT(NVARCHAR(MAX), [v].[$column], 126)"
        }

        $seq = '0'
        if ($spec.HasSeq) { $seq = 'CAST([v].[SequenceID] AS INT)' }

        $where = "[v].[ObjectTypeKey] = @typeKey AND [v].[AttributeKey] IN ($keys)"
        if ($spec.Localized) { $where += ' AND [v].[LocaleKey] = @localeKey' }
        if ($spec.Table -eq 'ObjectValueString' -or $spec.Table -eq 'ObjectValueText') {
            $where += " AND [v].[$column] <> N''"
        }
        elseif ($spec.Table -eq 'ObjectValueXml') {
            $where += " AND DATALENGTH([v].[$column]) > 0"
        }

        $sql = @"
SELECT [v].[ObjectKey], [v].[AttributeKey], $selectValue, $seq
FROM [fim].[$($spec.Table)] AS [v]
WHERE $where
"@
        $kind = $spec.Kind
        Invoke-FimReader -Connection $Connection -Sql $sql -Bind {
            param($cmd)
            Add-SqlParameter $cmd '@typeKey' ([System.Data.SqlDbType]::SmallInt) $TypeKey
            if ($spec.Localized) {
                Add-SqlParameter $cmd '@localeKey' ([System.Data.SqlDbType]::SmallInt) $LocaleKey
            }
        } -Read {
            param($r)
            if ($r.IsDBNull(2)) { return }
            $text = $r.GetString(2)
            if ([string]::IsNullOrEmpty($text)) { return }

            $stored = $null
            $seq = 0
            if ($spec.HasSeq) { $seq = [int] $r.GetInt32(3) }
            $seqKey = $seq.ToString('D10', $script:Invariant)
            if ($kind -eq 'bool') {
                $stored = [pscustomobject]@{ Seq = $seq; Kind = 'bool'; Bool = ($text -eq 'true'); SortKey = $seqKey }
            }
            elseif ($kind -eq 'number') {
                $number = [int64] 0
                if (-not [int64]::TryParse($text, [System.Globalization.NumberStyles]::Integer, $script:Invariant, [ref] $number)) {
                    $stored = [pscustomobject]@{ Seq = $seq; Kind = 'text'; Text = $text; SortKey = ($seqKey + '|' + $text.ToLowerInvariant()) }
                }
                else {
                    $stored = [pscustomobject]@{
                        Seq     = $seq
                        Kind    = 'number'
                        Number  = $number
                        SortKey = ($seqKey + '|' + (Format-InvariantInt $number))
                    }
                }
            }
            else {
                $stored = [pscustomobject]@{ Seq = $seq; Kind = 'text'; Text = $text; SortKey = ($seqKey + '|' + $text.ToLowerInvariant()) }
            }
            Add-StoredValue $store $r.GetInt64(0) $nameByKey[[int] $r.GetInt16(1)] $stored
        }
    }

    return $store
}

function Get-FirstText {
    param($List)
    if ($null -eq $List -or $List.Count -eq 0) { return $null }
    $best = $null
    foreach ($item in $List) {
        if ($item.Kind -eq 'bool') { continue }
        if ($null -eq $best -or $item.Seq -lt $best.Seq) { $best = $item }
    }
    if ($null -eq $best) { return $null }
    if ($best.Kind -eq 'number') { return (Format-InvariantInt $best.Number) }
    return [string] $best.Text
}

function Get-ExportValues {
    param($List, [bool] $Multivalued)
    if ($null -eq $List -or $List.Count -eq 0) { return $null }

    if (-not $Multivalued) {
        $best = $null
        foreach ($item in $List) {
            if ($null -eq $best -or $item.Seq -lt $best.Seq) { $best = $item }
        }
        return ,@($best)
    }

    $n = $List.Count
    if ($n -lt 2) { return ,$List.ToArray() }

    $keys = New-Object 'string[]' $n
    for ($i = 0; $i -lt $n; $i++) { $keys[$i] = [string] $List[$i].SortKey }
    $order = [FimExport.Json]::OrderByKey($keys)
    $sorted = New-Object 'object[]' $n
    for ($i = 0; $i -lt $n; $i++) { $sorted[$i] = $List[$order[$i]] }
    return ,$sorted
}

function Write-JsonAttributeValue {
    param([System.Text.StringBuilder] $Sb, $Values, [bool] $Multivalued)

    # Jednoprvkove pole PowerShell pri predani do funkce rozbali, takze ho slozime znovu.
    $Values = @($Values)
    $count = 1
    if ($Multivalued) {
        $count = $Values.Count
        [void] $Sb.Append('[')
    }

    $first = $true
    for ($i = 0; $i -lt $count; $i++) {
        $item = $Values[$i]
        if (-not $first) { [void] $Sb.Append(',') }
        $first = $false

        if ($item.Kind -eq 'bool') {
            if ($item.Bool) { [void] $Sb.Append('true') } else { [void] $Sb.Append('false') }
        }
        elseif ($item.Kind -eq 'number') {
            [void] $Sb.Append((Format-InvariantInt $item.Number))
        }
        else {
            [void] $Sb.Append([FimExport.Json]::Quote([string] $item.Text))
        }
    }

    if ($Multivalued) { [void] $Sb.Append(']') }
}

function Write-JsonExport {
    param($Path, $Meta, $Items, $AttrByType)
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $writer = New-Object System.IO.StreamWriter ($Path, $false, $utf8)
    try {
        $writer.WriteLine('{')
        $writer.WriteLine('  "meta": {')
        $writer.WriteLine(('    "environment": {0},' -f (ConvertTo-JsonLiteral $Meta.Environment)))
        $writer.WriteLine(('    "server": {0},' -f (ConvertTo-JsonLiteral $Meta.Server)))
        $writer.WriteLine(('    "database": {0},' -f (ConvertTo-JsonLiteral $Meta.Database)))
        $writer.WriteLine(('    "exportedAtUtc": {0},' -f (ConvertTo-JsonLiteral $Meta.ExportedAtUtc)))
        $writer.WriteLine(('    "localeKey": {0},' -f (Format-InvariantInt $Meta.LocaleKey)))
        $writer.WriteLine(('    "includeEmpty": {0},' -f (ConvertTo-JsonLiteral ([bool] $Meta.IncludeEmpty))))
        $writer.WriteLine('    "mappingAttribute": "AccountName",')
        $writer.Write('    "attributesRequested": [')
        $writer.Write((($Meta.Requested | ForEach-Object { ConvertTo-JsonLiteral $_ }) -join ','))
        $writer.WriteLine('],')
        $writer.Write('    "attributesNotInDatabase": [')
        $writer.Write((($Meta.NotInDatabase | ForEach-Object { ConvertTo-JsonLiteral $_ }) -join ','))
        $writer.WriteLine('],')
        $writer.WriteLine('    "objectTypes": [')
        for ($i = 0; $i -lt $Meta.Types.Count; $i++) {
            $t = $Meta.Types[$i]
            $comma = ','
            if ($i -eq ($Meta.Types.Count - 1)) { $comma = '' }
            $names = @($t.Attributes | ForEach-Object { ConvertTo-JsonLiteral $_ })
            $writer.WriteLine(('      {{ "name": {0}, "exported": {1}, "inDatabase": {2}, "withoutAccountName": {3}, "attributes": [{4}] }}{5}' -f `
                (ConvertTo-JsonLiteral $t.Name),
                (Format-InvariantInt $t.Exported),
                (Format-InvariantInt $t.InDatabase),
                (Format-InvariantInt $t.WithoutAccountName),
                ($names -join ','),
                $comma))
        }
        $writer.WriteLine('    ]')
        $writer.WriteLine('  },')
        $writer.WriteLine('  "objects": [')

        $index = 0
        foreach ($item in $Items) {
            $sb = New-Object System.Text.StringBuilder 256
            [void] $sb.Append('    {')
            [void] $sb.Append('"objectType":')
            [void] $sb.Append((ConvertTo-JsonLiteral $item.ObjectType))
            [void] $sb.Append(',"objectId":')
            [void] $sb.Append((ConvertTo-JsonLiteral $item.ObjectId))
            [void] $sb.Append(',"accountName":')
            [void] $sb.Append((ConvertTo-JsonLiteral $item.AccountName))
            [void] $sb.Append(',"attributes":{')

            $defs = $AttrByType[$item.ObjectType]
            $firstAttr = $true
            foreach ($def in $defs) {
                $list = $null
                if ($item.Values -and $item.Values.ContainsKey($def.Name)) {
                    $list = $item.Values[$def.Name]
                }
                $values = Get-ExportValues $list ([bool] $def.Multivalued)
                if ($null -eq $values) { continue }
                if (-not $firstAttr) { [void] $sb.Append(',') }
                $firstAttr = $false
                [void] $sb.Append((ConvertTo-JsonLiteral $def.Name))
                [void] $sb.Append(':')
                Write-JsonAttributeValue $sb $values ([bool] $def.Multivalued)
            }
            [void] $sb.Append('}}')
            if ($index -lt ($Items.Count - 1)) { [void] $sb.Append(',') }
            $writer.WriteLine($sb.ToString())
            $index++
            if (($index % 200) -eq 0) {
                $writer.Flush()
                Write-Host ("  zapsano {0}/{1}" -f $index, $Items.Count)
            }
        }

        $writer.WriteLine('  ]')
        $writer.WriteLine('}')
    }
    finally {
        $writer.Dispose()
    }
}

function Format-IdMapField {
    param($Value)
    if ([string]::IsNullOrEmpty([string] $Value)) { return '' }
    return ([string] $Value -replace '[\r\n\t]', ' ')
}

function Write-IdMap {
    param($Path, $Items)

    $rows = @($Items | Where-Object { -not [string]::IsNullOrEmpty($_.AccountName) } | Sort-Object ObjectId)
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $writer = New-Object System.IO.StreamWriter ($Path, $false, $utf8)
    $seenAccount = @{}
    $duplicateAccounts = 0
    try {
        $writer.WriteLine("ObjectID`tAccountName`tEmployeeID`tDisplayName")
        foreach ($row in $rows) {
            $accountKey = $row.AccountName.ToLowerInvariant()
            if ($seenAccount.ContainsKey($accountKey)) { $duplicateAccounts++ }
            else { $seenAccount[$accountKey] = $true }

            $employeeId = $null
            $displayName = $null
            if ($row.Values) {
                if ($row.Values.ContainsKey('EmployeeID')) { $employeeId = Get-FirstText $row.Values['EmployeeID'] }
                if ($row.Values.ContainsKey('DisplayName')) { $displayName = Get-FirstText $row.Values['DisplayName'] }
            }

            $writer.WriteLine(('{0}{1}{2}{1}{3}{1}{4}' -f `
                $row.ObjectId, "`t", `
                (Format-IdMapField $row.AccountName), `
                (Format-IdMapField $employeeId), `
                (Format-IdMapField $displayName)))
        }
    }
    finally {
        $writer.Dispose()
    }

    return @{
        Written            = [int] $rows.Count
        Skipped            = [int] ($Items.Count - $rows.Count)
        DuplicateAccounts  = $duplicateAccounts
    }
}

function Write-XmlValue {
    param($Writer, $Item)
    $Writer.WriteStartElement('value')
    if ($Item.Kind -eq 'bool') {
        $Writer.WriteString($(if ($Item.Bool) { 'true' } else { 'false' }))
    }
    elseif ($Item.Kind -eq 'number') {
        $Writer.WriteString((Format-InvariantInt $Item.Number))
    }
    else {
        $Writer.WriteString([string] $Item.Text)
    }
    $Writer.WriteEndElement()
}

function Write-XmlExport {
    param($Path, $Meta, $Items, $AttrByType)
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = $utf8
    $settings.CloseOutput = $true
    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('fimExport')
        $writer.WriteAttributeString('environment', $Meta.Environment)
        $writer.WriteAttributeString('server', $Meta.Server)
        $writer.WriteAttributeString('database', $Meta.Database)
        $writer.WriteAttributeString('exportedAtUtc', $Meta.ExportedAtUtc)
        $writer.WriteAttributeString('localeKey', (Format-InvariantInt $Meta.LocaleKey))
        $writer.WriteAttributeString('includeEmpty', ([bool] $Meta.IncludeEmpty).ToString().ToLowerInvariant())
        $writer.WriteAttributeString('mappingAttribute', 'AccountName')

        $writer.WriteStartElement('attributesNotInDatabase')
        foreach ($name in $Meta.NotInDatabase) {
            $writer.WriteStartElement('attribute')
            $writer.WriteAttributeString('name', $name)
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()

        $writer.WriteStartElement('objectTypes')
        foreach ($t in $Meta.Types) {
            $writer.WriteStartElement('objectType')
            $writer.WriteAttributeString('name', $t.Name)
            $writer.WriteAttributeString('exported', (Format-InvariantInt $t.Exported))
            $writer.WriteAttributeString('inDatabase', (Format-InvariantInt $t.InDatabase))
            $writer.WriteAttributeString('withoutAccountName', (Format-InvariantInt $t.WithoutAccountName))
            foreach ($name in $t.Attributes) {
                $writer.WriteStartElement('attribute')
                $writer.WriteAttributeString('name', $name)
                $writer.WriteEndElement()
            }
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()

        $writer.WriteStartElement('objects')
        foreach ($item in $Items) {
            $writer.WriteStartElement('object')
            $writer.WriteAttributeString('objectType', $item.ObjectType)
            $writer.WriteAttributeString('objectId', $item.ObjectId)
            if ($item.AccountName) { $writer.WriteAttributeString('accountName', $item.AccountName) }

            $defs = $AttrByType[$item.ObjectType]
            foreach ($def in $defs) {
                $list = $null
                if ($item.Values -and $item.Values.ContainsKey($def.Name)) {
                    $list = $item.Values[$def.Name]
                }
                $values = Get-ExportValues $list ([bool] $def.Multivalued)
                if ($null -eq $values) { continue }

                $writer.WriteStartElement('attr')
                $writer.WriteAttributeString('name', $def.Name)
                $writer.WriteAttributeString('multivalued', ([bool] $def.Multivalued).ToString().ToLowerInvariant())
                if ($def.Multivalued) {
                    foreach ($value in $values) {
                        Write-XmlValue $writer $value
                    }
                }
                else {
                    Write-XmlValue $writer $values[0]
                }
                $writer.WriteEndElement()
            }
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    }
    finally {
        $writer.Dispose()
    }
}

$ObjectType = @($ObjectType | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
if ($ObjectType.Count -eq 0) {
    throw 'Zadejte aspon jeden -ObjectType.'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', $script:Invariant)
    $ext = 'json'
    if ($Format -eq 'Xml') { $ext = 'xml' }
    $safeEnv = ($Environment -replace '[^\w\-]', '_')
    $OutputPath = Join-Path (Get-Location) ("FimExport-{0}-{1}.{2}" -f $safeEnv, $stamp, $ext)
}
if (Test-Path -LiteralPath $OutputPath) {
    throw "Soubor uz existuje a neprepisuji ho: $OutputPath"
}

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$IdMapPath = Join-Path ([System.IO.Path]::GetDirectoryName($OutputPath)) `
    ([System.IO.Path]::GetFileNameWithoutExtension($OutputPath) + '.ids.txt')
if (Test-Path -LiteralPath $IdMapPath) {
    throw "Soubor uz existuje a neprepisuji ho: $IdMapPath"
}

Write-Host ("Pripojuji {0} / {1} ({2})..." -f $Server, $Database, $Environment)

$conn = Connect-FimDatabase
try {
    $meta = Get-FimMetadata -Connection $conn
    if ($meta.NotInDatabase.Count -gt 0) {
        Write-Warning ("Tyto atributy v [fim].[AttributeInternal] nejsou a nebudou ve vystupu: " + ($meta.NotInDatabase -join ', '))
    }

    $items = [System.Collections.Generic.List[object]]::new()
    $typeStats = [System.Collections.Generic.List[object]]::new()
    $attrByType = @{}

    foreach ($requestedType in $ObjectType) {
        $typeInfo = $meta.Types[$requestedType]
        $typeName = $typeInfo.Name
        $typeKey = [int] $typeInfo.Key
        $bound = @()
        if ($meta.Bound.ContainsKey($typeName)) { $bound = @($meta.Bound[$typeName]) }

        # Poradi atributu ve vystupu = poradi v -Attribute, nazvy jak jsou v databazi.
        $ordered = [System.Collections.Generic.List[object]]::new()
        $orderedNames = @{}
        foreach ($requestedName in $meta.Requested) {
            if ($requestedName -eq 'ObjectID') { continue }
            $match = @($bound | Where-Object { $_.Name -eq $requestedName })
            if ($match.Count -eq 0) { continue }
            if ($orderedNames.ContainsKey($match[0].Name)) { continue }
            $orderedNames[$match[0].Name] = $true
            $ordered.Add($match[0])
        }
        $attrByType[$typeName] = $ordered
        if (-not $orderedNames.ContainsKey('AccountName')) {
            Write-Warning "$typeName nema navazany atribut AccountName. Zaznamy tohoto typu pujdou ve vystupu s accountName = null a porovnani podle nej je nenajde."
        }

        $countBox = @{ Value = [int64] 0 }
        Invoke-FimReader -Connection $conn -Sql 'SELECT COUNT_BIG(*) FROM [fim].[Objects] WHERE [ObjectTypeKey] = @typeKey' -Bind {
            param($cmd)
            Add-SqlParameter $cmd '@typeKey' ([System.Data.SqlDbType]::SmallInt) $typeKey
        } -Read {
            param($r)
            $countBox.Value = $r.GetInt64(0)
        }
        $inDatabase = [int64] $countBox.Value

        $hasValueSql = $null
        if (-not $IncludeEmpty) { $hasValueSql = Get-HasValueSql $ordered }

        Write-Host ("{0}: v databazi {1}, nacitam objekty..." -f $typeName, $inDatabase)
        if ($ordered.Count -eq 0 -and -not $IncludeEmpty) {
            Write-Warning "$typeName : zadny pozadovany atribut neni na tento typ navazany, export tohoto typu je prazdny."
            $objects = @()
        }
        else {
            $objects = @(Read-FimObjects -Connection $conn -TypeKey $typeKey -HasValueSql $hasValueSql)
        }
        Write-Host ("{0}: {1} objektu k exportu, nacitam atributy..." -f $typeName, $objects.Count)

        $values = Read-FimValues -Connection $conn -TypeKey $typeKey -BoundAttributes $ordered
        $withoutAccount = 0
        foreach ($obj in $objects) {
            $bag = $null
            [void] $values.TryGetValue([int64] $obj.Key, [ref] $bag)
            $account = $null
            if ($bag -and $bag.ContainsKey('AccountName')) {
                $account = Get-FirstText $bag['AccountName']
            }
            if ([string]::IsNullOrEmpty($account)) { $withoutAccount++ }
            $items.Add([pscustomobject]@{
                ObjectType  = $typeName
                ObjectId    = [string] $obj.ObjectId
                AccountName = $account
                Values      = $bag
            })
        }

        $typeStats.Add([pscustomobject]@{
            Name               = $typeName
            Exported           = [int64] $objects.Count
            InDatabase         = $inDatabase
            WithoutAccountName = [int64] $withoutAccount
            Attributes         = @($ordered | ForEach-Object { $_.Name })
        })
        Write-Host ("{0}: hotovo, bez AccountName: {1}" -f $typeName, $withoutAccount)
    }
}
finally {
    $conn.Dispose()
}

Write-Host ("Razim {0} objektu..." -f $items.Count)
$sorted = @($items | Sort-Object `
    @{ Expression = { $_.ObjectType }; Ascending = $true }, `
    @{ Expression = { if ([string]::IsNullOrEmpty($_.AccountName)) { 1 } else { 0 } }; Ascending = $true }, `
    @{ Expression = { $_.AccountName }; Ascending = $true }, `
    @{ Expression = { $_.ObjectId }; Ascending = $true })

Write-Host ("Zapisuji slovnik {0} ..." -f $IdMapPath)
$idMap = Write-IdMap -Path $IdMapPath -Items $sorted
Write-Host ("Slovnik: {0} dvojic, bez AccountName preskoceno {1}" -f $idMap.Written, $idMap.Skipped)
if ($idMap.DuplicateAccounts -gt 0) {
    Write-Warning ("Stejny AccountName ma vic objektu ({0}). Zpetny preklad AccountName -> ObjectID pak nebude jednoznacny." -f $idMap.DuplicateAccounts)
}

Write-Host ("Zapisuji {0} ..." -f $OutputPath)

$exportMeta = @{
    Environment    = $Environment
    Server         = $Server.Trim()
    Database       = $Database.Trim()
    ExportedAtUtc  = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', $script:Invariant)
    LocaleKey      = $LocaleKey
    IncludeEmpty   = [bool] $IncludeEmpty
    Requested      = @($meta.Requested)
    NotInDatabase  = @($meta.NotInDatabase)
    Types          = @($typeStats)
}

if ($Format -eq 'Xml') {
    Write-XmlExport -Path $OutputPath -Meta $exportMeta -Items $sorted -AttrByType $attrByType
}
else {
    Write-JsonExport -Path $OutputPath -Meta $exportMeta -Items $sorted -AttrByType $attrByType
}

Write-Host ("Export dokoncen: {0} ({1} objektu)" -f $OutputPath, $sorted.Count)
Write-Host ("Slovnik ObjectID: {0}" -f $IdMapPath)
