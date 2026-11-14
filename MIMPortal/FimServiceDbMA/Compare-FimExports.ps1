#Requires -Version 5.1
<#
.SYNOPSIS
    Porovna exporty Person a Group ze dvou prostredi.

.DESCRIPTION
    Prvni cast: objekty, ktere v jednom prostredi prebyvaji a ve druhem chybi.
    Skupiny se paruji podle AccountName. Uzivatele se paruji postupne podle EmployeeID,
    pak AccountName a nakonec DisplayName. Dalsi krok bere jen objekty, ktere se
    v predchozim kroku nenapojily, a to jen kdyz je klic na obou stranach prave jednou.
    Druha cast: clenstvi ve skupinach (csoblComputedMember). Do porovnani vstupuji
    jen uzivatele, kteri se mezi prostredimi napojili. Nespárovani se ignoruji.
    Objekty a clenove bez AccountName se do reportu nedostanou. Ucty v IgnoredMembers
    se pri porovnani clenstvi vynechavaji.

    Kazdy report ma TXT a HTML. Od kazdeho rozdilu vznikne jen jeden soubor.

.PARAMETER DataDir
    Adresar s exporty FimExport-*.json a odpovidajicimi *.ids.txt.
    Vychozi je ..\Data vuci tomuto skriptu.

.PARAMETER ProdJson
    Cesta k JSON exportu PROD. Kdyz neni zadana, vezme se nejnovejsi FimExport-PROD-*.json.

.PARAMETER TestJson
    Cesta k JSON exportu TEST. Kdyz neni zadana, vezme se nejnovejsi FimExport-Test-*.json.

.PARAMETER OutDir
    Kam se zapisi reporty. Vychozi je DataDir\Compare. Existujici reporty se prepisuji.

.PARAMETER IgnoredMembers
    AccountName, ktere se pri porovnani clenstvi ignoruji.
#>
[CmdletBinding()]
param(
    [string] $DataDir,
    [string] $ProdJson,
    [string] $TestJson,
    [string] $OutDir,
    [string[]] $IgnoredMembers = @(
        'usacevova_admin',
        '17706481',
        'mikula',
        'mikula_admin',
        'jelinkova',
        'nikmonova',
        '17706813',
        '17702053',
        'tack',
        '17707004',
        '17707111',
        '17709617',
        '17709639',
        '17706466',
        '17709619'
    )
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if (-not $DataDir) {
    $DataDir = Join-Path $PSScriptRoot '..\Data'
}
$DataDir = [System.IO.Path]::GetFullPath($DataDir)

function Resolve-ExportJson {
    param([string] $Directory, [string] $Explicit, [string] $Token)
    if ($Explicit) {
        if (-not (Test-Path -LiteralPath $Explicit)) {
            throw "Export neexistuje: $Explicit"
        }
        return [System.IO.Path]::GetFullPath($Explicit)
    }
    $found = @(Get-ChildItem -LiteralPath $Directory -Filter 'FimExport-*.json' -File |
        Where-Object { $_.BaseName -match [regex]::Escape($Token) } |
        Sort-Object Name -Descending)
    if ($found.Count -eq 0) {
        throw "V $Directory neni zadny FimExport-$Token-*.json"
    }
    return $found[0].FullName
}

function Get-IdsPath {
    param([string] $JsonPath)
    $dir = [System.IO.Path]::GetDirectoryName($JsonPath)
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($JsonPath)
    return (Join-Path $dir ($stem + '.ids.txt'))
}

function Read-IdMap {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Chybi mapovaci soubor: $Path"
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $lines = [System.IO.File]::ReadAllLines($Path, $utf8)
    $map = @{}
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($i -eq 0 -and $line.StartsWith('ObjectID')) { continue }
        $parts = $line.Split("`t")
        if ($parts.Length -lt 2) { continue }
        $id = $parts[0].Trim().ToLowerInvariant()
        $account = $parts[1].Trim()
        if ($id -and $account) { $map[$id] = $account }
    }
    return $map
}

function Read-FimExport {
    param([string] $Path)
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $text = [System.IO.File]::ReadAllText($Path, $utf8)
    return ($text | ConvertFrom-Json)
}

function Get-AccountText {
    param($Object)
    $name = $Object.accountName
    if ([string]::IsNullOrWhiteSpace([string] $name)) {
        $attr = Get-Attr $Object 'AccountName'
        if ($null -ne $attr) { $name = [string] $attr }
    }
    if ([string]::IsNullOrWhiteSpace([string] $name)) { return $null }
    return ([string] $name).Trim()
}

function Get-AccountKey {
    param([string] $Account)
    if ([string]::IsNullOrWhiteSpace($Account)) { return $null }
    return $Account.Trim().ToLowerInvariant()
}

function Get-Attr {
    param($Object, [string] $Name)
    $attrs = $Object.attributes
    if ($null -eq $attrs) { return $null }
    $prop = $attrs.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

function Convert-ToText {
    param($Value)
    if ($null -eq $Value) { return '' }
    if ($Value -is [bool]) { return $(if ($Value) { 'true' } else { 'false' }) }
    return ([string] $Value).Trim()
}

function Get-MemberIds {
    param($Value)
    $ids = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Value) { return $ids }
    if ($Value -is [string]) {
        $one = $Value.Trim().ToLowerInvariant()
        if ($one) { $ids.Add($one) }
        return $ids
    }
    foreach ($item in $Value) {
        if ($null -eq $item) { continue }
        $one = ([string] $item).Trim().ToLowerInvariant()
        if ($one) { $ids.Add($one) }
    }
    return $ids
}

function New-Bucket {
    return New-Object System.Collections.ArrayList
}

function Add-Bucket {
    param($Index, [string] $Key, $Item)
    if (-not $Index.ContainsKey($Key)) {
        $Index[$Key] = (New-Bucket)
    }
    [void] $Index[$Key].Add($Item)
}

function New-SideIndex {
    param($Export, $IdMap)
    $byKey = @{}
    $byId = @{}
    $noAccount = New-Bucket
    $duplicates = New-Bucket

    foreach ($obj in @($Export.objects)) {
        $id = ([string] $obj.objectId).Trim().ToLowerInvariant()
        $byId[$id] = $obj
        $account = Get-AccountText $obj
        $key = Get-AccountKey $account
        $type = [string] $obj.objectType
        if (-not $key) {
            [void] $noAccount.Add($obj)
            continue
        }
        $pairKey = ($type.ToLowerInvariant() + '|' + $key)
        Add-Bucket $byKey $pairKey $obj
    }

    foreach ($pairKey in @($byKey.Keys)) {
        $list = $byKey[$pairKey]
        if ($list.Count -gt 1) {
            foreach ($obj in $list) { [void] $duplicates.Add($obj) }
        }
    }

    return @{
        ByKey      = $byKey
        ById       = $byId
        NoAccount  = $noAccount
        Duplicates = $duplicates
        IdMap      = $IdMap
        Meta       = $Export.meta
    }
}

function Convert-ToObjectRow {
    param($Object)
    return [pscustomobject]@{
        Typ          = [string] $Object.objectType
        AccountName  = (Get-AccountText $Object)
        ObjectID     = [string] $Object.objectId
        DisplayName  = (Convert-ToText (Get-Attr $Object 'DisplayName'))
        EmployeeID   = (Convert-ToText (Get-Attr $Object 'EmployeeID'))
        EmployeeType = (Convert-ToText (Get-Attr $Object 'EmployeeType'))
        FinalStatus  = (Convert-ToText (Get-Attr $Object 'csoblFinalPropetyStatus'))
        Status       = (Convert-ToText (Get-Attr $Object 'csoblvalidStatus'))
        GroupType    = (Convert-ToText (Get-Attr $Object 'csoblGroupType'))
    }
}

function Get-GroupsOnlyOnSide {
    param($Left, $Right)
    $rows = New-Bucket
    foreach ($pairKey in @($Left.ByKey.Keys)) {
        if (-not $pairKey.StartsWith('group|')) { continue }
        if ($Right.ByKey.ContainsKey($pairKey)) { continue }
        foreach ($obj in $Left.ByKey[$pairKey]) {
            [void] $rows.Add((Convert-ToObjectRow $obj))
        }
    }
    return ,$rows
}

function Get-PersonRecords {
    param($Side)
    $list = New-Bucket
    $byId = @{}
    foreach ($pairKey in @($Side.ByKey.Keys)) {
        if (-not $pairKey.StartsWith('person|')) { continue }
        foreach ($obj in $Side.ByKey[$pairKey]) {
            $rec = [pscustomobject]@{
                Object      = $obj
                Id          = ([string] $obj.objectId).Trim().ToLowerInvariant()
                EmployeeKey = (Get-AccountKey (Convert-ToText (Get-Attr $obj 'EmployeeID')))
                AccountKey  = (Get-AccountKey (Get-AccountText $obj))
                DisplayKey  = (Get-AccountKey (Convert-ToText (Get-Attr $obj 'DisplayName')))
                Paired      = $false
                PairId      = ''
                PairLevel   = ''
                Ignored     = $false
            }
            [void] $list.Add($rec)
            $byId[$rec.Id] = $rec
        }
    }
    return @{ List = $list; ById = $byId }
}

function Invoke-PersonPairing {
    param($ProdPersons, $TestPersons)
    $ambiguous = New-Bucket
    $pairs = New-Bucket
    $pairPartner = @{}
    $counts = @{ EmployeeID = 0; AccountName = 0; DisplayName = 0 }
    $nextId = 0
    $levels = @(
        @{ Name = 'EmployeeID'; Prop = 'EmployeeKey' },
        @{ Name = 'AccountName'; Prop = 'AccountKey' },
        @{ Name = 'DisplayName'; Prop = 'DisplayKey' }
    )

    foreach ($level in $levels) {
        $prop = $level.Prop
        $prodBy = @{}
        $testBy = @{}
        foreach ($person in $ProdPersons.List) {
            if ($person.Paired) { continue }
            $key = $person.$prop
            if (-not $key) { continue }
            Add-Bucket $prodBy $key $person
        }
        foreach ($person in $TestPersons.List) {
            if ($person.Paired) { continue }
            $key = $person.$prop
            if (-not $key) { continue }
            Add-Bucket $testBy $key $person
        }

        foreach ($key in @($prodBy.Keys)) {
            if (-not $testBy.ContainsKey($key)) { continue }
            $left = $prodBy[$key]
            $right = $testBy[$key]
            if ($left.Count -eq 1 -and $right.Count -eq 1) {
                $nextId++
                $pairId = 'p:{0}' -f $nextId
                $left[0].Paired = $true
                $right[0].Paired = $true
                $left[0].PairId = $pairId
                $right[0].PairId = $pairId
                $left[0].PairLevel = $level.Name
                $right[0].PairLevel = $level.Name
                $pairPartner[$pairId] = $right[0]
                [void] $pairs.Add([pscustomobject]@{ Level = $level.Name; Prod = $left[0].Object; Test = $right[0].Object })
                $counts[$level.Name] = $counts[$level.Name] + 1
            }
            else {
                foreach ($person in $left) {
                    [void] $ambiguous.Add([pscustomobject]@{ Side = 'PROD'; Level = $level.Name; Key = $key; Person = $person })
                }
                foreach ($person in $right) {
                    [void] $ambiguous.Add([pscustomobject]@{ Side = 'TEST'; Level = $level.Name; Key = $key; Person = $person })
                }
            }
        }
    }

    foreach ($prodPerson in $ProdPersons.List) {
        if (-not $prodPerson.PairId) { continue }
        if (-not $pairPartner.ContainsKey($prodPerson.PairId)) { continue }
        $testPerson = $pairPartner[$prodPerson.PairId]
        $skip = $false
        if ($prodPerson.AccountKey -and $ignoredMemberKeys.ContainsKey($prodPerson.AccountKey)) { $skip = $true }
        if ($testPerson.AccountKey -and $ignoredMemberKeys.ContainsKey($testPerson.AccountKey)) { $skip = $true }
        if ($skip) {
            $prodPerson.Ignored = $true
            $testPerson.Ignored = $true
        }
    }

    return @{ Counts = $counts; Ambiguous = $ambiguous; Pairs = $pairs }
}

function Convert-ToPairRow {
    param([string] $Level, $ProdObject, $TestObject)
    $prodRow = Convert-ToObjectRow $ProdObject
    $testRow = Convert-ToObjectRow $TestObject
    return [pscustomobject]@{
        Podle            = $Level
        Typ              = $prodRow.Typ
        ProdAccount      = $prodRow.AccountName
        ProdDisplay      = $prodRow.DisplayName
        ProdEmployeeId   = $prodRow.EmployeeID
        ProdEmployeeType = $prodRow.EmployeeType
        ProdFinal        = $prodRow.FinalStatus
        ProdStatus       = $prodRow.Status
        ProdGroupType    = $prodRow.GroupType
        TestAccount      = $testRow.AccountName
        TestDisplay      = $testRow.DisplayName
        TestEmployeeId   = $testRow.EmployeeID
        TestEmployeeType = $testRow.EmployeeType
        TestFinal        = $testRow.FinalStatus
        TestStatus       = $testRow.Status
        TestGroupType    = $testRow.GroupType
        SortLevel        = $(switch ($Level) { 'EmployeeID' { 0 } 'AccountName' { 1 } 'DisplayName' { 2 } default { 9 } })
    }
}

function Get-GroupPairRows {
    param($Prod, $Test)
    $rows = New-Bucket
    foreach ($pairKey in @($Prod.ByKey.Keys)) {
        if (-not $pairKey.StartsWith('group|')) { continue }
        if (-not $Test.ByKey.ContainsKey($pairKey)) { continue }
        foreach ($prodObject in $Prod.ByKey[$pairKey]) {
            foreach ($testObject in $Test.ByKey[$pairKey]) {
                [void] $rows.Add((Convert-ToPairRow 'AccountName' $prodObject $testObject))
            }
        }
    }
    return ,$rows
}

function Convert-ToAmbiguousRow {
    param($Hit)
    $obj = $Hit.Person.Object
    $result = 'nespárováno'
    if ($Hit.Person.Paired) { $result = 'spárováno později podle ' + $Hit.Person.PairLevel }
    $row = Convert-ToObjectRow $obj
    return [pscustomobject]@{
        Uroven      = $Hit.Level
        Klic        = $Hit.Key
        Vysledek    = $result
        Typ         = $row.Typ
        AccountName  = $row.AccountName
        DisplayName  = $row.DisplayName
        EmployeeID   = $row.EmployeeID
        EmployeeType = $row.EmployeeType
        FinalStatus  = $row.FinalStatus
    }
}

function Get-UnpairedPeople {
    param($Persons)
    $rows = New-Bucket
    foreach ($person in $Persons.List) {
        if ($person.Paired) { continue }
        [void] $rows.Add((Convert-ToObjectRow $person.Object))
    }
    return ,$rows
}

function Find-ObjectByAccount {
    param($Side, [string] $ObjectType, [string] $AccountKey)
    $pairKey = ($ObjectType.ToLowerInvariant() + '|' + $AccountKey)
    if (-not $Side.ByKey.ContainsKey($pairKey)) { return $null }
    return $Side.ByKey[$pairKey][0]
}

function Get-MemberSet {
    param($Side, $GroupObject, $PersonById)
    $set = @{}
    $ids = Get-MemberIds (Get-Attr $GroupObject 'csoblComputedMember')
    foreach ($id in $ids) {
        $member = $null
        if ($Side.ById.ContainsKey($id)) { $member = $Side.ById[$id] }

        $account = $null
        if ($Side.IdMap.ContainsKey($id)) {
            $account = [string] $Side.IdMap[$id]
        }
        elseif ($null -ne $member) {
            $account = Get-AccountText $member
        }

        $accountKey = Get-AccountKey $account
        if (-not $accountKey) { continue }
        if ($ignoredMemberKeys.ContainsKey($accountKey)) { continue }

        $type = ''
        if ($null -ne $member) { $type = [string] $member.objectType }
        if ($type -ne 'Person' -or -not $PersonById.ContainsKey($id)) { continue }
        $rec = $PersonById[$id]
        if ($rec.Ignored -or -not $rec.PairId) { continue }
        $matchKey = $rec.PairId

        if ($set.ContainsKey($matchKey)) { continue }
        $set[$matchKey] = [pscustomobject]@{
            AccountName = $account.Trim()
            ObjectID    = $id
            Typ         = $type
            DisplayName  = $(if ($null -ne $member) { (Convert-ToText (Get-Attr $member 'DisplayName')) } else { '' })
            EmployeeID   = $(if ($null -ne $member) { (Convert-ToText (Get-Attr $member 'EmployeeID')) } else { '' })
            EmployeeType = $(if ($null -ne $member) { (Convert-ToText (Get-Attr $member 'EmployeeType')) } else { '' })
            FinalStatus  = $(if ($null -ne $member) { (Convert-ToText (Get-Attr $member 'csoblFinalPropetyStatus')) } else { '' })
        }
    }
    return $set
}

function Get-MembershipDiff {
    param($Prod, $Test, $ProdPersons, $TestPersons)
    $onlyProd = New-Bucket
    $onlyTest = New-Bucket
    $compared = 0
    $same = 0
    $different = 0

    $groupKeys = @{}
    foreach ($pairKey in @($Prod.ByKey.Keys)) {
        if ($pairKey.StartsWith('group|')) { $groupKeys[$pairKey] = $true }
    }
    foreach ($pairKey in @($Test.ByKey.Keys)) {
        if ($pairKey.StartsWith('group|')) { $groupKeys[$pairKey] = $true }
    }

    foreach ($pairKey in @($groupKeys.Keys)) {
        $inProd = $Prod.ByKey.ContainsKey($pairKey)
        $inTest = $Test.ByKey.ContainsKey($pairKey)
        if (-not ($inProd -and $inTest)) { continue }

        $compared++
        $prodGroup = $Prod.ByKey[$pairKey][0]
        $testGroup = $Test.ByKey[$pairKey][0]
        $prodSet = Get-MemberSet $Prod $prodGroup $ProdPersons.ById
        $testSet = Get-MemberSet $Test $testGroup $TestPersons.ById

        # Dalsi objekty se stejnym AccountName: clenstvi se sjednoti.
        if ($Prod.ByKey[$pairKey].Count -gt 1) {
            for ($i = 1; $i -lt $Prod.ByKey[$pairKey].Count; $i++) {
                $extra = Get-MemberSet $Prod $Prod.ByKey[$pairKey][$i] $ProdPersons.ById
                foreach ($k in @($extra.Keys)) { if (-not $prodSet.ContainsKey($k)) { $prodSet[$k] = $extra[$k] } }
            }
        }
        if ($Test.ByKey[$pairKey].Count -gt 1) {
            for ($i = 1; $i -lt $Test.ByKey[$pairKey].Count; $i++) {
                $extra = Get-MemberSet $Test $Test.ByKey[$pairKey][$i] $TestPersons.ById
                foreach ($k in @($extra.Keys)) { if (-not $testSet.ContainsKey($k)) { $testSet[$k] = $extra[$k] } }
            }
        }

        $before = $onlyProd.Count + $onlyTest.Count
        foreach ($k in @($prodSet.Keys)) {
            if ($testSet.ContainsKey($k)) { continue }
            $m = $prodSet[$k]
            [void] $onlyProd.Add([pscustomobject]@{
                Skupina         = (Get-AccountText $prodGroup)
                SkupinaNazev    = (Convert-ToText (Get-Attr $prodGroup 'DisplayName'))
                SkupinaObjectID = [string] $prodGroup.objectId
                Typ             = $m.Typ
                AccountName     = $m.AccountName
                EmployeeID      = $m.EmployeeID
                EmployeeType    = $m.EmployeeType
                FinalStatus     = $m.FinalStatus
                DisplayName     = $m.DisplayName
            })
        }
        foreach ($k in @($testSet.Keys)) {
            if ($prodSet.ContainsKey($k)) { continue }
            $m = $testSet[$k]
            [void] $onlyTest.Add([pscustomobject]@{
                Skupina         = (Get-AccountText $testGroup)
                SkupinaNazev    = (Convert-ToText (Get-Attr $testGroup 'DisplayName'))
                SkupinaObjectID = [string] $testGroup.objectId
                Typ             = $m.Typ
                AccountName     = $m.AccountName
                EmployeeID      = $m.EmployeeID
                EmployeeType    = $m.EmployeeType
                FinalStatus     = $m.FinalStatus
                DisplayName     = $m.DisplayName
            })
        }
        if (($onlyProd.Count + $onlyTest.Count) -eq $before) { $same++ } else { $different++ }
    }

    return @{
        OnlyProd     = @($onlyProd | Sort-Object Skupina, AccountName, EmployeeID)
        OnlyTest     = @($onlyTest | Sort-Object Skupina, AccountName, EmployeeID)
        Compared     = $compared
        Same         = $same
        Different    = $different
    }
}

function ConvertTo-HtmlText {
    param([string] $Text)
    return [System.Net.WebUtility]::HtmlEncode($Text)
}

function Format-Cell {
    param([string] $Text)
    if ($null -eq $Text) { return '' }
    return (($Text -replace '[\r\n\t]', ' ').Trim())
}

function Write-ReportFiles {
    param(
        [string] $PathBase,
        [string] $Title,
        [string[]] $Lead,
        [string[]] $Headers,
        [string[]] $Fields,
        $Rows
    )
    $utf8 = New-Object System.Text.UTF8Encoding $true
    $list = @($Rows)
    $count = $list.Count
    if ($count -eq 1 -and $null -eq $list[0]) { $count = 0; $list = @() }

    $txt = New-Object System.Text.StringBuilder
    [void] $txt.AppendLine($Title)
    foreach ($line in $Lead) { [void] $txt.AppendLine($line) }
    [void] $txt.AppendLine(('Počet: {0}' -f $count))
    [void] $txt.AppendLine('')
    [void] $txt.AppendLine(($Headers -join "`t"))
    foreach ($row in $list) {
        $cells = foreach ($field in $Fields) { Format-Cell ([string] $row.$field) }
        [void] $txt.AppendLine(($cells -join "`t"))
    }
    [System.IO.File]::WriteAllText(($PathBase + '.txt'), $txt.ToString(), $utf8)

    $html = New-Object System.Text.StringBuilder
    [void] $html.AppendLine('<!DOCTYPE html>')
    [void] $html.AppendLine('<html lang="cs"><head><meta charset="utf-8">')
    [void] $html.Append('<title>')
    [void] $html.Append((ConvertTo-HtmlText $Title))
    [void] $html.AppendLine('</title>')
    [void] $html.AppendLine('<style>')
    [void] $html.AppendLine('body{font-family:Segoe UI,sans-serif;margin:24px;color:#1a1a1a;background:#f6f7f9}')
    [void] $html.AppendLine('h1{font-size:22px;margin:0 0 8px}')
    [void] $html.AppendLine('p{margin:4px 0;color:#333}')
    [void] $html.AppendLine('.count{font-size:18px;margin:16px 0}')
    [void] $html.AppendLine('input{padding:8px 10px;width:360px;margin:8px 0 16px;border:1px solid #ccc;border-radius:4px}')
    [void] $html.AppendLine('table{border-collapse:collapse;background:#fff;width:100%}')
    [void] $html.AppendLine('th,td{border:1px solid #ddd;padding:6px 8px;text-align:left;vertical-align:top;font-size:13px}')
    [void] $html.AppendLine('th{background:#1f4e79;color:#fff;position:sticky;top:0}')
    [void] $html.AppendLine('tr:nth-child(even){background:#f3f6fa}')
    [void] $html.AppendLine('</style></head><body>')
    [void] $html.Append('<h1>')
    [void] $html.Append((ConvertTo-HtmlText $Title))
    [void] $html.AppendLine('</h1>')
    foreach ($line in $Lead) {
        [void] $html.Append('<p>')
        [void] $html.Append((ConvertTo-HtmlText $line))
        [void] $html.AppendLine('</p>')
    }
    [void] $html.Append('<p class="count">Počet: ')
    [void] $html.Append($count)
    [void] $html.AppendLine('</p>')
    if ($count -gt 0) {
        [void] $html.AppendLine('<input id="q" type="search" placeholder="Filtr v tabulce">')
        [void] $html.AppendLine('<table><thead><tr>')
        foreach ($header in $Headers) {
            [void] $html.Append('<th>')
            [void] $html.Append((ConvertTo-HtmlText $header))
            [void] $html.Append('</th>')
        }
        [void] $html.AppendLine('</tr></thead><tbody>')
        foreach ($row in $list) {
            [void] $html.Append('<tr>')
            foreach ($field in $Fields) {
                [void] $html.Append('<td>')
                [void] $html.Append((ConvertTo-HtmlText (Format-Cell ([string] $row.$field))))
                [void] $html.Append('</td>')
            }
            [void] $html.AppendLine('</tr>')
        }
        [void] $html.AppendLine('</tbody></table>')
        [void] $html.AppendLine('<script>document.getElementById("q").oninput=function(){var q=this.value.toLowerCase();var rows=document.querySelectorAll("tbody tr");for(var i=0;i<rows.length;i++){rows[i].style.display=rows[i].innerText.toLowerCase().indexOf(q)>=0?"":"none";}};</script>')
    }
    [void] $html.AppendLine('</body></html>')
    [System.IO.File]::WriteAllText(($PathBase + '.html'), $html.ToString(), $utf8)
}

function Count-ByType {
    param($Rows, [string] $Type)
    $n = 0
    foreach ($row in @($Rows)) {
        if ($null -eq $row) { continue }
        if ($row.Typ -eq $Type) { $n++ }
    }
    return $n
}

$prodPath = Resolve-ExportJson $DataDir $ProdJson 'PROD'
$testPath = Resolve-ExportJson $DataDir $TestJson 'Test'
$prodIds = Get-IdsPath $prodPath
$testIds = Get-IdsPath $testPath
if (-not $OutDir) { $OutDir = Join-Path $DataDir 'Compare' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir | Out-Null
}

Write-Host "PROD  $prodPath"
Write-Host "TEST  $testPath"
Write-Host "Vystup $OutDir"

$ignoredMemberKeys = @{}
foreach ($name in @($IgnoredMembers)) {
    $key = Get-AccountKey $name
    if ($key) { $ignoredMemberKeys[$key] = $true }
}

$prodExport = Read-FimExport $prodPath
$testExport = Read-FimExport $testPath
$prod = New-SideIndex $prodExport (Read-IdMap $prodIds)
$test = New-SideIndex $testExport (Read-IdMap $testIds)
$prodPersons = Get-PersonRecords $prod
$testPersons = Get-PersonRecords $test
$pairing = Invoke-PersonPairing $prodPersons $testPersons

$onlyProdRows = Get-GroupsOnlyOnSide $prod $test
$onlyTestRows = Get-GroupsOnlyOnSide $test $prod
foreach ($row in (Get-UnpairedPeople $prodPersons)) { [void] $onlyProdRows.Add($row) }
foreach ($row in (Get-UnpairedPeople $testPersons)) { [void] $onlyTestRows.Add($row) }
$onlyProd = @($onlyProdRows | Sort-Object Typ, AccountName, ObjectID)
$onlyTest = @($onlyTestRows | Sort-Object Typ, AccountName, ObjectID)
$membership = Get-MembershipDiff $prod $test $prodPersons $testPersons

$prodAmbiguous = @($pairing.Ambiguous | Where-Object { $_.Side -eq 'PROD' } | ForEach-Object { Convert-ToAmbiguousRow $_ } | Sort-Object Uroven, Klic, AccountName, EmployeeID)
$testAmbiguous = @($pairing.Ambiguous | Where-Object { $_.Side -eq 'TEST' } | ForEach-Object { Convert-ToAmbiguousRow $_ } | Sort-Object Uroven, Klic, AccountName, EmployeeID)

$pairedRows = New-Bucket
foreach ($pair in $pairing.Pairs) {
    [void] $pairedRows.Add((Convert-ToPairRow $pair.Level $pair.Prod $pair.Test))
}
foreach ($groupRow in (Get-GroupPairRows $prod $test)) {
    [void] $pairedRows.Add($groupRow)
}
$paired = @($pairedRows | Sort-Object Typ, SortLevel, ProdAccount, TestAccount)

$objectHeaders = @('Typ', 'AccountName', 'DisplayName', 'EmployeeID', 'EmployeeType', 'csoblFinalPropetyStatus', 'csoblvalidStatus', 'csoblGroupType')
$objectFields = @('Typ', 'AccountName', 'DisplayName', 'EmployeeID', 'EmployeeType', 'FinalStatus', 'Status', 'GroupType')
$pairHeaders = @(
    'Podle', 'Typ',
    'PROD AccountName', 'PROD DisplayName', 'PROD EmployeeID', 'PROD EmployeeType', 'PROD csoblFinalPropetyStatus', 'PROD csoblvalidStatus', 'PROD csoblGroupType',
    'TEST AccountName', 'TEST DisplayName', 'TEST EmployeeID', 'TEST EmployeeType', 'TEST csoblFinalPropetyStatus', 'TEST csoblvalidStatus', 'TEST csoblGroupType'
)
$pairFields = @(
    'Podle', 'Typ',
    'ProdAccount', 'ProdDisplay', 'ProdEmployeeId', 'ProdEmployeeType', 'ProdFinal', 'ProdStatus', 'ProdGroupType',
    'TestAccount', 'TestDisplay', 'TestEmployeeId', 'TestEmployeeType', 'TestFinal', 'TestStatus', 'TestGroupType'
)

$prodMeta = $prod.Meta
$testMeta = $test.Meta
$sourceLead = @(
    ('PROD: {0} ({1}, {2})' -f $prodMeta.environment, $prodMeta.server, $prodMeta.exportedAtUtc),
    ('TEST: {0} ({1}, {2})' -f $testMeta.environment, $testMeta.server, $testMeta.exportedAtUtc),
    'Uživatelé se párují postupně podle EmployeeID, AccountName a DisplayName. Napojený objekt se znovu nenapojuje. Skupiny se párují podle AccountName.'
)

$pairedPersons = @($paired | Where-Object { $_.Typ -eq 'Person' })
$pairedGroups = @($paired | Where-Object { $_.Typ -eq 'Group' })

Write-ReportFiles (Join-Path $OutDir 'uzivatele-sparovane') `
    'Spárovaní uživatelé' `
    ($sourceLead + @('Sloupec Podle je klíč, kterým se uživatel napojil. Každý jen jednou, v pořadí EmployeeID, AccountName, DisplayName.')) `
    $pairHeaders $pairFields $pairedPersons

Write-ReportFiles (Join-Path $OutDir 'skupiny-sparovane') `
    'Spárované skupiny' `
    ($sourceLead + @('Skupina se páruje podle AccountName.')) `
    $pairHeaders $pairFields $pairedGroups

Write-ReportFiles (Join-Path $OutDir 'objekty-prebyvaji-v-PROD') `
    'Objekty, které přebývají v PROD (v TEST chybí)' `
    $sourceLead `
    $objectHeaders $objectFields $onlyProd

Write-ReportFiles (Join-Path $OutDir 'objekty-chybi-v-PROD') `
    'Objekty, které chybí v PROD (v TEST přebývají)' `
    $sourceLead `
    $objectHeaders $objectFields $onlyTest

$prodDup = @($prod.Duplicates | ForEach-Object { Convert-ToObjectRow $_ } | Sort-Object Typ, AccountName, ObjectID)
$testDup = @($test.Duplicates | ForEach-Object { Convert-ToObjectRow $_ } | Sort-Object Typ, AccountName, ObjectID)
Write-ReportFiles (Join-Path $OutDir 'objekty-duplicitni-AccountName-PROD') `
    'Objekty v PROD se stejným AccountName' `
    ($sourceLead + @('U členství se hodnoty csoblComputedMember těchto objektů sjednocují.')) `
    $objectHeaders $objectFields $prodDup
Write-ReportFiles (Join-Path $OutDir 'objekty-duplicitni-AccountName-TEST') `
    'Objekty v TEST se stejným AccountName' `
    ($sourceLead + @('U členství se hodnoty csoblComputedMember těchto objektů sjednocují.')) `
    $objectHeaders $objectFields $testDup

$ambiguousHeaders = @('Úroveň', 'Klíč', 'Výsledek', 'AccountName', 'EmployeeID', 'DisplayName', 'EmployeeType', 'csoblFinalPropetyStatus')
$ambiguousFields = @('Uroven', 'Klic', 'Vysledek', 'AccountName', 'EmployeeID', 'DisplayName', 'EmployeeType', 'FinalStatus')
$ambiguousLead = $sourceLead + @(
    'Klíč byl na obou stranách, ale ne právě jednou, takže se v tomto kroku nenapojil. Objekty pokračují do další úrovně.'
)
Write-ReportFiles (Join-Path $OutDir 'osoby-nejednoznacne-PROD') `
    'Uživatelé v PROD, které v daném kroku nešlo napojit jednoznačně' `
    $ambiguousLead $ambiguousHeaders $ambiguousFields $prodAmbiguous
Write-ReportFiles (Join-Path $OutDir 'osoby-nejednoznacne-TEST') `
    'Uživatelé v TEST, které v daném kroku nešlo napojit jednoznačně' `
    $ambiguousLead $ambiguousHeaders $ambiguousFields $testAmbiguous

$memberHeaders = @('Skupina', 'DisplayName skupiny', 'Typ člena', 'AccountName člena', 'EmployeeID člena', 'EmployeeType člena', 'csoblFinalPropetyStatus člena', 'DisplayName člena')
$memberFields = @('Skupina', 'SkupinaNazev', 'Typ', 'AccountName', 'EmployeeID', 'EmployeeType', 'FinalStatus', 'DisplayName')
$ignoredList = ((@($IgnoredMembers) | ForEach-Object { $_.Trim() } | Where-Object { $_ }) -join ', ')
$memberLead = $sourceLead + @(
    'Členství je csoblComputedMember. Porovnávají se jen uživatelé, kteří se napojili mezi prostředími. Nespárovaní uživatelé a ostatní členové se ignorují.',
    'Skupina se páruje podle svého AccountName. Porovnávají se jen skupiny, které jsou v obou prostředích.',
    ('Ignorované účty: {0}' -f $ignoredList)
)

Write-ReportFiles (Join-Path $OutDir 'clenove-prebyvaji-v-PROD') `
    'Členové, kteří ve skupině přebývají v PROD (v TEST ve stejné skupině chybí)' `
    $memberLead `
    $memberHeaders $memberFields $membership.OnlyProd

Write-ReportFiles (Join-Path $OutDir 'clenove-chybi-v-PROD') `
    'Členové, kteří ve skupině chybí v PROD (v TEST ve stejné skupině přebývají)' `
    $memberLead `
    $memberHeaders $memberFields $membership.OnlyTest

foreach ($stale in @(
    'objekty-sparovane',
    'objekty-chybi-v-TEST',
    'objekty-prebyvaji-v-TEST',
    'clenove-chybi-v-TEST',
    'clenove-prebyvaji-v-TEST',
    'objekty-bez-AccountName-PROD',
    'objekty-bez-AccountName-TEST',
    'clenove-bez-prekladu-PROD',
    'clenove-bez-prekladu-TEST'
)) {
    foreach ($ext in @('.txt', '.html')) {
        $stalePath = Join-Path $OutDir ($stale + $ext)
        if (Test-Path -LiteralPath $stalePath) { Remove-Item -LiteralPath $stalePath -Force }
    }
}

function Count-Side {
    param($Side, [string] $Type)
    $n = 0
    foreach ($pairKey in @($Side.ByKey.Keys)) {
        if ($pairKey.StartsWith($Type.ToLowerInvariant() + '|')) { $n += $Side.ByKey[$pairKey].Count }
    }
    return $n
}

$summaryLines = @(
    'Porovnání exportů MIM Service',
    '',
    ('PROD soubor: {0}' -f $prodPath),
    ('PROD mapování: {0}' -f $prodIds),
    ('PROD export: {0} / {1} / {2}' -f $prodMeta.environment, $prodMeta.server, $prodMeta.exportedAtUtc),
    ('TEST soubor: {0}' -f $testPath),
    ('TEST mapování: {0}' -f $testIds),
    ('TEST export: {0} / {1} / {2}' -f $testMeta.environment, $testMeta.server, $testMeta.exportedAtUtc),
    '',
    'Objekty s AccountName',
    ('  Person PROD: {0}' -f (Count-Side $prod 'Person')),
    ('  Person TEST: {0}' -f (Count-Side $test 'Person')),
    ('  Spárovaní uživatelé: {0}' -f @($pairedPersons).Count),
    ('  Spárované skupiny: {0}' -f @($pairedGroups).Count),
    ('  Person spárováno podle EmployeeID: {0}' -f $pairing.Counts.EmployeeID),
    ('  Person spárováno podle AccountName: {0}' -f $pairing.Counts.AccountName),
    ('  Person spárováno podle DisplayName: {0}' -f $pairing.Counts.DisplayName),
    ('  Person jen v PROD: {0}' -f (Count-ByType $onlyProd 'Person')),
    ('  Person jen v TEST: {0}' -f (Count-ByType $onlyTest 'Person')),
    ('  Nejednoznačné kroky PROD: {0}' -f @($prodAmbiguous).Count),
    ('  Nejednoznačné kroky TEST: {0}' -f @($testAmbiguous).Count),
    ('  Group PROD: {0}' -f (Count-Side $prod 'Group')),
    ('  Group TEST: {0}' -f (Count-Side $test 'Group')),
    ('  Group jen v PROD: {0}' -f (Count-ByType $onlyProd 'Group')),
    ('  Group jen v TEST: {0}' -f (Count-ByType $onlyTest 'Group')),
    ('  Duplicitní AccountName PROD: {0}' -f @($prodDup).Count),
    ('  Duplicitní AccountName TEST: {0}' -f @($testDup).Count),
    '',
    'Členství ve skupinách (csoblComputedMember)',
    ('  Skupin v obou prostředích: {0}' -f $membership.Compared),
    ('  Skupin se stejným členstvím: {0}' -f $membership.Same),
    ('  Skupin s rozdílem: {0}' -f $membership.Different),
    ('  Členů jen v PROD: {0}' -f @($membership.OnlyProd).Count),
    ('  Členů jen v TEST: {0}' -f @($membership.OnlyTest).Count),
    '  Porovnávají se jen spárovaní uživatelé. Nespárovaní se ignorují.',
    ('  Ignorované účty: {0}' -f $ignoredList),
    '',
    'Reporty (každý i jako .html)',
    '  uzivatele-sparovane.txt',
    '  skupiny-sparovane.txt',
    '  objekty-prebyvaji-v-PROD.txt',
    '  objekty-chybi-v-PROD.txt',
    '  objekty-duplicitni-AccountName-PROD.txt',
    '  objekty-duplicitni-AccountName-TEST.txt',
    '  osoby-nejednoznacne-PROD.txt',
    '  osoby-nejednoznacne-TEST.txt',
    '  clenove-prebyvaji-v-PROD.txt',
    '  clenove-chybi-v-PROD.txt'
)

$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText((Join-Path $OutDir 'souhrn.txt'), (($summaryLines -join "`r`n") + "`r`n"), $utf8Bom)

$summaryHtml = New-Object System.Text.StringBuilder
[void] $summaryHtml.AppendLine('<!DOCTYPE html><html lang="cs"><head><meta charset="utf-8"><title>Porovnání exportů</title>')
[void] $summaryHtml.AppendLine('<style>body{font-family:Segoe UI,sans-serif;margin:24px;color:#1a1a1a;background:#f6f7f9}h1{font-size:22px}h2{font-size:18px;margin-top:28px}li{margin:4px 0}a{color:#1f4e79}code{background:#fff;padding:1px 4px}</style>')
[void] $summaryHtml.AppendLine('</head><body><h1>Porovnání exportů MIM Service</h1>')
[void] $summaryHtml.Append('<p>PROD: ')
[void] $summaryHtml.Append((ConvertTo-HtmlText ('{0} / {1} / {2}' -f $prodMeta.environment, $prodMeta.server, $prodMeta.exportedAtUtc)))
[void] $summaryHtml.AppendLine('</p>')
[void] $summaryHtml.Append('<p>TEST: ')
[void] $summaryHtml.Append((ConvertTo-HtmlText ('{0} / {1} / {2}' -f $testMeta.environment, $testMeta.server, $testMeta.exportedAtUtc)))
[void] $summaryHtml.AppendLine('</p>')
[void] $summaryHtml.Append('<p>')
[void] $summaryHtml.Append((ConvertTo-HtmlText ('Uživatelé: EmployeeID {0}, AccountName {1}, DisplayName {2}.' -f $pairing.Counts.EmployeeID, $pairing.Counts.AccountName, $pairing.Counts.DisplayName)))
[void] $summaryHtml.AppendLine('</p>')
[void] $summaryHtml.AppendLine('<h2>Objekty</h2><ul>')
$summaryItems = @(
    @('uzivatele-sparovane', ('Spárovaní uživatelé: {0}' -f @($pairedPersons).Count)),
    @('skupiny-sparovane', ('Spárované skupiny: {0}' -f @($pairedGroups).Count)),
    @('objekty-prebyvaji-v-PROD', ('Přebývají v PROD / chybí v TEST: {0} (Person {1}, Group {2})' -f @($onlyProd).Count, (Count-ByType $onlyProd 'Person'), (Count-ByType $onlyProd 'Group'))),
    @('objekty-chybi-v-PROD', ('Chybí v PROD / přebývají v TEST: {0} (Person {1}, Group {2})' -f @($onlyTest).Count, (Count-ByType $onlyTest 'Person'), (Count-ByType $onlyTest 'Group'))),
    @('objekty-duplicitni-AccountName-PROD', ('Duplicitní AccountName v PROD: {0}' -f @($prodDup).Count)),
    @('objekty-duplicitni-AccountName-TEST', ('Duplicitní AccountName v TEST: {0}' -f @($testDup).Count)),
    @('osoby-nejednoznacne-PROD', ('Nejednoznačné kroky v PROD: {0}' -f @($prodAmbiguous).Count)),
    @('osoby-nejednoznacne-TEST', ('Nejednoznačné kroky v TEST: {0}' -f @($testAmbiguous).Count))
)
foreach ($item in $summaryItems) {
    [void] $summaryHtml.Append('<li><a href="')
    [void] $summaryHtml.Append($item[0])
    [void] $summaryHtml.Append('.html">')
    [void] $summaryHtml.Append((ConvertTo-HtmlText $item[1]))
    [void] $summaryHtml.AppendLine('</a></li>')
}
[void] $summaryHtml.AppendLine('</ul><h2>Členství ve skupinách</h2>')
[void] $summaryHtml.Append('<p>')
[void] $summaryHtml.Append((ConvertTo-HtmlText ('Porovnávají se jen spárovaní uživatelé. Nespárovaní se ignorují. Ignorované účty: {0}' -f $ignoredList)))
[void] $summaryHtml.AppendLine('</p><ul>')
[void] $summaryHtml.Append('<li>')
[void] $summaryHtml.Append((ConvertTo-HtmlText ('Skupin v obou prostředích: {0}, stejné členství: {1}, s rozdílem: {2}' -f $membership.Compared, $membership.Same, $membership.Different)))
[void] $summaryHtml.AppendLine('</li>')
$memberItems = @(
    @('clenove-prebyvaji-v-PROD', ('Členové přebývají v PROD / chybí v TEST: {0}' -f @($membership.OnlyProd).Count)),
    @('clenove-chybi-v-PROD', ('Členové chybí v PROD / přebývají v TEST: {0}' -f @($membership.OnlyTest).Count))
)
foreach ($item in $memberItems) {
    [void] $summaryHtml.Append('<li><a href="')
    [void] $summaryHtml.Append($item[0])
    [void] $summaryHtml.Append('.html">')
    [void] $summaryHtml.Append((ConvertTo-HtmlText $item[1]))
    [void] $summaryHtml.AppendLine('</a></li>')
}
[void] $summaryHtml.AppendLine('</ul></body></html>')
[System.IO.File]::WriteAllText((Join-Path $OutDir 'souhrn.html'), $summaryHtml.ToString(), $utf8Bom)

$summaryLines | ForEach-Object { Write-Host $_ }
