#Requires -Version 5.1
<#
.SYNOPSIS
    Zalozí na MIM portálu skupiny, které podle reportu přebývají v PROD.

.DESCRIPTION
    Bere skupiny z reportu objekty-prebyvaji-v-PROD a jejich atributy z exportu
    AllGroups_PROD.xml (Export-Clixml). Před založením ověří, že AccountName
    na cílovém portálu ještě není. Reference se přemapují jen u atributů
    csoblComputedMember, Owner, DisplayedOwner a csoblAppRoles:
    ObjectID v PROD -> AccountName -> ObjectID v TEST. Ostatní reference se nezapisují.
    Zakládají se jen skupiny s csoblGroupType apl nebo bus. Nejdřív aplikační role,
    potom bus, protože bus role na aplikační role odkazují. grp a ostatní typy se
    přeskočí. Nové ObjectID se dopíše do TEST ids.

    Nastaví všechny atributy s prefixem csobl. Ze standardních atributů
    AccountName, DisplayName, Description, Domain, Email, Type, Scope, Filter
    (jen bez ObjectID), MembershipAddWorkflow, MembershipLocked, Owner,
    DisplayedOwner a Temporal.
    Systémové atributy (ObjectID, Creator, CreatedTime, MVObjectID,
    DomainConfiguration, msidm*) se nepřenášejí.

    S -ObjectType RoleApplication založí aplikace z allapps_prod.xml.
    Identita je AccountName, a když chybí, DisplayName. Stejným atributem
    se ověřuje, že objekt na portálu ještě není.

.PARAMETER ObjectType
    Group zakládá aplikační a bus role. RoleApplication zakládá aplikace
    z XML exportu.

.PARAMETER ReportPath
    TXT report objektů, které přebývají v PROD.

.PARAMETER GroupsXml
    Export skupin z PROD portálu, Export-Clixml.

.PARAMETER ProdIds
    Mapování ObjectID a AccountName z PROD. Když chybí, vezme se nejnovější
    FimExport-PROD-*.ids.txt nebo FimMap-PROD-*.ids.txt v Data.

.PARAMETER TestIds
    Mapování ObjectID a AccountName z TEST. Když chybí, vezme se nejnovější
    FimExport-Test-*.ids.txt nebo FimMap-Test-*.ids.txt v Data.

.PARAMETER FimLibrary
    Skript s funkcemi portálu. Vychozi je ..\Data\FIMPowershell.ps1.

.PARAMETER Uri
    Adresa MIM služby. Když chybí, použije se $DefaultUri z FIMPowershell.ps1.

.PARAMETER WhatIf
    Jen ověří existenci a zapíše, co by se založilo. Na portál nic nezapíše.
#>
[CmdletBinding()]
param(
    [ValidateSet('Group', 'RoleApplication')]
    [string] $ObjectType = 'Group',

    [string] $ReportPath,
    [string] $GroupsXml,
    [string] $ProdIds,
    [string] $TestIds,
    [string] $FimLibrary,
    [string] $Uri,
    [string] $LogPath,
    [switch] $WhatIf
)

Set-StrictMode -Version 2.0
$script:LogWriter = $null
$ErrorActionPreference = 'Stop'

$DataDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.\Data'))
if (-not (Test-Path -LiteralPath $DataDir)) {
    $siblingData = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Data'))
    if (Test-Path -LiteralPath $siblingData) { $DataDir = $siblingData }
}
if (-not $ReportPath) {
    $ReportPath = Join-Path $DataDir 'Compare\objekty-prebyvaji-v-PROD.txt'
    if (-not (Test-Path -LiteralPath $ReportPath)) {
        $ReportPath = Join-Path $DataDir 'objekty-prebyvaji-v-PROD.txt'
    }
}
if (-not $GroupsXml) {
    if ($ObjectType -eq 'RoleApplication') { $GroupsXml = Join-Path $DataDir 'allapps_prod.xml' }
    else { $GroupsXml = Join-Path $DataDir 'AllGroups_PROD.xml' }
}
if (-not $FimLibrary) { $FimLibrary = 'C:\Batch\FIMAutomation\FIMPowerShell.ps1'}

$StandardAttributes = @{
    AccountName            = $true
    DisplayName            = $true
    Description            = $true
    Domain                 = $true
    Email                  = $true
    Type                   = $true
    Scope                  = $true
    Filter                 = $true
    MembershipAddWorkflow  = $true
    MembershipLocked       = $true
    Owner                  = $true
    DisplayedOwner         = $true
    Temporal               = $true
}

$ReferenceAttributes = @{
    csoblComputedMember = $true
    Owner               = $true
    DisplayedOwner      = $true
    csoblAppRoles       = $true
}

$MultiReferenceAttributes = @{
    csoblComputedMember = $true
    Owner               = $true
    csoblAppRoles       = $true
}

$SkipAttributes = @{
    ObjectID             = $true
    ObjectType           = $true
    CreatedTime          = $true
    Creator              = $true
    MVObjectID           = $true
    ResourceTime         = $true
    DomainConfiguration  = $true
}

function Write-Log {
    param([string] $Message)
    $line = '{0}  {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    if ($script:LogWriter) { $script:LogWriter.WriteLine($line) }
}

function Resolve-IdsFile {
    param([string] $Explicit, [string] $Token)
    if ($Explicit) {
        if (-not (Test-Path -LiteralPath $Explicit)) { throw "Mapovani neexistuje: $Explicit" }
        return [System.IO.Path]::GetFullPath($Explicit)
    }
    $found = @(Get-ChildItem -LiteralPath $DataDir -File |
        Where-Object { $_.Name -like 'FimExport-*.ids.txt' -or $_.Name -like 'FimMap-*.ids.txt' } |
        Where-Object { $_.BaseName -match [regex]::Escape($Token) } |
        Sort-Object @{
            Expression = {
                if ($_.BaseName -match '(\d{8}-\d{6})') { $Matches[1] } else { '00000000-000000' }
            }
            Descending = $true
        }, @{ Expression = 'Name'; Descending = $true })
    if ($found.Count -eq 0) { throw "V $DataDir neni FimExport-$Token-*.ids.txt ani FimMap-$Token-*.ids.txt" }
    return $found[0].FullName
}

function Read-IdRows {
    param([string] $Path)
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $lines = [System.IO.File]::ReadAllLines($Path, $utf8)
    $rows = New-Object System.Collections.ArrayList
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($i -eq 0 -and $line.StartsWith('ObjectID')) { continue }
        $parts = $line.Split("`t")
        if ($parts.Length -lt 2) { continue }
        $id = $parts[0].Trim().ToLowerInvariant()
        $account = $parts[1].Trim()
        if ($id -and $account) {
            [void] $rows.Add([pscustomobject]@{ Id = $id; Account = $account; Key = $account.ToLowerInvariant() })
        }
    }
    return ,$rows
}

function Read-ReportGroups {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Report neexistuje: $Path" }
    $utf8 = New-Object System.Text.UTF8Encoding $true
    $lines = [System.IO.File]::ReadAllLines($Path, $utf8)
    $names = New-Object System.Collections.Generic.List[object]
    $headerSeen = $false
    $index = 0
    foreach ($line in $lines) {
        if (-not $headerSeen) {
            if ($line.StartsWith("Typ`t")) { $headerSeen = $true }
            continue
        }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line.Split("`t")
        if ($parts.Length -lt 2) { continue }
        if ($parts[0].Trim() -ne 'Group') { continue }
        $account = $parts[1].Trim()
        if (-not $account) { continue }
        $groupType = ''
        if ($parts.Length -ge 8) { $groupType = $parts[7].Trim() }
        $names.Add([pscustomobject]@{
            Account   = $account
            GroupType = $groupType
            Index     = $index
        })
        $index++
    }
    return ,$names.ToArray()
}

function Get-GroupTypeText {
    param($Source, [string] $ReportType)
    if ($null -ne $Source) {
        $prop = $Source.PSObject.Properties['csoblGroupType']
        if ($prop -and -not [string]::IsNullOrWhiteSpace([string] $prop.Value)) {
            return ([string] $prop.Value).Trim()
        }
    }
    if ($ReportType) { return $ReportType.Trim() }
    return ''
}

function Read-XmlText {
    param([string] $Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $encodingName = 'utf-8'
    $text = ''
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $encodingName = 'utf-8-bom'
        $text = (New-Object System.Text.UTF8Encoding $true).GetString($bytes)
    }
    elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $encodingName = 'utf-16'
        $text = [System.Text.Encoding]::Unicode.GetString($bytes)
    }
    elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
        $encodingName = 'utf-16-be'
        $text = [System.Text.Encoding]::BigEndianUnicode.GetString($bytes)
    }
    elseif ($bytes.Length -ge 8 -and $bytes[0] -eq 0x3C -and $bytes[1] -eq 0x00 -and $bytes[2] -eq 0x4F -and $bytes[3] -eq 0x00) {
        $encodingName = 'utf-16'
        $text = [System.Text.Encoding]::Unicode.GetString($bytes)
    }
    else {
        $strict = New-Object System.Text.UTF8Encoding $false, $true
        try {
            $text = $strict.GetString($bytes)
        }
        catch {
            $encodingName = 'windows-1250'
            $text = [System.Text.Encoding]::GetEncoding(1250).GetString($bytes)
        }
    }
    if ($text.Length -gt 0 -and [int] $text[0] -eq 0xFEFF) { $text = $text.Substring(1) }
    return [pscustomobject]@{ Text = $text; EncodingName = $encodingName }
}

function Read-GroupClixml {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Export skupin neexistuje: $Path" }
    $xmlText = Read-XmlText $Path
    Write-Log ("XML kodovani: {0}  {1}" -f $xmlText.EncodingName, $Path)
    $text = $xmlText.Text
    $parts = [regex]::Split($text, '(?=<Objs\b)')
    $groups = New-Object System.Collections.ArrayList
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    foreach ($part in $parts) {
        $chunk = $part.Trim()
        if (-not $chunk.StartsWith('<Objs')) { continue }
        if ($chunk -notmatch '</Objs>\s*$') { $chunk = $chunk + '</Objs>' }
        $tmp = [System.IO.Path]::GetTempFileName()
        try {
            [System.IO.File]::WriteAllText($tmp, $chunk, $utf8Bom)
            $loaded = @(Import-Clixml -Path $tmp)
            foreach ($item in $loaded) {
                if ($null -ne $item) { [void] $groups.Add($item) }
            }
        }
        finally {
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
    }
    return ,$groups
}

function Get-GuidKey {
    param([string] $Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $match = [regex]::Match($Value, '(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')
    if (-not $match.Success) { return $null }
    return $match.Value.ToLowerInvariant()
}

function Convert-ToTestReference {
    param(
        [string] $Value,
        $ProdById,
        $TestByAccount,
        [string] $GroupAccount,
        [string] $AttributeName
    )
    $guid = Get-GuidKey $Value
    if (-not $guid) { return $null }
    if (-not $ProdById.ContainsKey($guid)) {
        Write-Log ("  reference bez prekladu  skupina={0}  atribut={1}  PROD ObjectID={2}  (neni v PROD ids)" -f $GroupAccount, $AttributeName, $guid)
        return $null
    }
    $accountKey = ([string] $ProdById[$guid]).Trim().ToLowerInvariant()
    if (-not $TestByAccount.ContainsKey($accountKey)) {
        Write-Log ("  reference bez prekladu  skupina={0}  atribut={1}  AccountName={2}  (neni v TEST ids)" -f $GroupAccount, $AttributeName, $ProdById[$guid])
        return $null
    }
    $testId = [string] $TestByAccount[$accountKey]
    if ($Value -match '(?i)^\s*urn:uuid:') { return 'urn:uuid:' + $testId }
    return $testId
}

function Test-ReferenceValue {
    param($Value)
    $text = [string] $Value
    return ($text -match '(?i)^\s*urn:uuid:[0-9a-f]{8}-')
}

function Add-CopiedAttribute {
    param($ImportObject, [string] $Name, $Value, [string] $GroupAccount, $ProdById, $TestByAccount)
    if ($null -eq $Value) { return }
    $items = @()
    $multi = $false
    if ($Value -is [System.Array]) {
        $multi = $true
        $items = @($Value)
    }
    else {
        $items = @([string] $Value)
    }

    $translate = $ReferenceAttributes.ContainsKey($Name)
    if ($MultiReferenceAttributes.ContainsKey($Name)) { $multi = $true }
    if ($Name -eq 'Filter' -and ([string] $Value) -match '(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}') {
        Write-Log ("  Filter obsahuje ObjectID a nezapisuje se  skupina={0}" -f $GroupAccount)
        return
    }

    $mapped = New-Object System.Collections.Generic.List[string]
    foreach ($item in $items) {
        if ($null -eq $item) { continue }
        $text = [string] $item
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        if (Test-ReferenceValue $text) {
            if (-not $translate) {
                Write-Log ("  reference se neprenasi  skupina={0}  atribut={1}" -f $GroupAccount, $Name)
                continue
            }
            $translated = Convert-ToTestReference $text $ProdById $TestByAccount $GroupAccount $Name
            if ($translated) { $mapped.Add($translated) }
        }
        else {
            $mapped.Add($text.Trim())
        }
    }
    if ($mapped.Count -eq 0) { return }

    if ($multi) {
        foreach ($one in $mapped) {
            AddMultiValue $ImportObject $Name $one
        }
        return
    }
    SetSingleValue $ImportObject $Name $mapped[0]
}

function Get-PropertyText {
    param($Object, [string] $Name)
    if ($null -eq $Object) { return '' }
    $prop = $Object.PSObject.Properties[$Name]
    if (-not $prop -or $null -eq $prop.Value) { return '' }
    return ([string] $prop.Value).Trim()
}

function Get-SourceIdentity {
    param($Object)
    $account = Get-PropertyText $Object 'AccountName'
    $display = Get-PropertyText $Object 'DisplayName'
    if ($account) {
        return [pscustomobject]@{ Attribute = 'AccountName'; Value = $account; DisplayName = $display }
    }
    if ($display) {
        return [pscustomobject]@{ Attribute = 'DisplayName'; Value = $display; DisplayName = $display }
    }
    return $null
}

function Find-PortalObjectId {
    param([string] $PortalType, [string] $AttributeName, [string] $AttributeValue, [string] $PortalUri)
    $escaped = $AttributeValue.Replace("'", "''")
    $xpath = "/$PortalType[$AttributeName='$escaped']"
    $foundObject = Export-FIMConfig -onlyBaseResources -customconfig $xpath -Uri $PortalUri
    if ($null -eq $foundObject) { return $null }
    $items = @()
    if ($foundObject -is [System.Array]) { $items = @($foundObject | Where-Object { $null -ne $_ }) }
    else { $items = @($foundObject) }
    if ($items.Count -eq 0) { return $null }
    if ($items.Count -gt 1) {
        Write-Log ("  vice nez jeden objekt  {0}[{1}='{2}']  pocet={3}" -f $PortalType, $AttributeName, $AttributeValue, $items.Count)
        return ''
    }
    $first = $items[0]
    if ($null -eq $first) { return $null }
    $raw = $null
    $rmo = $first.PSObject.Properties['ResourceManagementObject']
    if ($rmo -and $null -ne $rmo.Value -and $rmo.Value.ObjectIdentifier) {
        $raw = [string] $rmo.Value.ObjectIdentifier
    }
    if (-not $raw) {
        $oid = $first.PSObject.Properties['ObjectID']
        if ($oid) { $raw = [string] $oid.Value }
    }
    if (-not $raw) { return '' }
    $guid = Get-GuidKey $raw
    if ($guid) { return $guid }
    return ''
}

function Find-PortalGroupId {
    param([string] $AccountName, [string] $PortalUri)
    return Find-PortalObjectId 'Group' 'AccountName' $AccountName $PortalUri
}

function Add-TestIdMapping {
    param($TestByAccount, [string] $IdsPath, [string] $ObjectId, [string] $AccountName, [string] $DisplayName)
    $guid = Get-GuidKey $ObjectId
    if (-not $guid) {
        Write-Log ("  mapovani: ObjectID nejde precist ({0}) pro {1}" -f $ObjectId, $AccountName)
        return
    }
    $key = $AccountName.Trim().ToLowerInvariant()
    if ($TestByAccount.ContainsKey($key)) {
        if ([string] $TestByAccount[$key] -ne $guid) {
            Write-Log ("  mapovani: {0} uz ma ObjectID {1}, nove {2} se nezapisuje" -f $AccountName, $TestByAccount[$key], $guid)
        }
        return
    }
    $TestByAccount[$key] = $guid
    $safeAccount = ($AccountName.Trim() -replace '[\r\n\t]', ' ')
    $safeDisplay = ''
    if ($DisplayName) { $safeDisplay = ($DisplayName.Trim() -replace '[\r\n\t]', ' ') }
    if ($WhatIf) {
        Write-Log ("  WHATIF mapovani  {0}  {1}  {2}" -f $guid, $safeAccount, $safeDisplay)
        return
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $needsBreak = $false
    $file = Get-Item -LiteralPath $IdsPath
    if ($file.Length -gt 0) {
        $stream = [System.IO.File]::Open($IdsPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            [void] $stream.Seek(-1, [System.IO.SeekOrigin]::End)
            $last = $stream.ReadByte()
            if ($last -ne 10 -and $last -ne 13) { $needsBreak = $true }
        }
        finally { $stream.Dispose() }
    }
    $writer = New-Object System.IO.StreamWriter ($IdsPath, $true, $utf8)
    try {
        if ($needsBreak) { $writer.WriteLine() }
        $writer.WriteLine(('{0}{1}{2}{1}{3}{1}{4}' -f $guid, "`t", $safeAccount, '', $safeDisplay))
    }
    finally { $writer.Dispose() }
    Write-Log ("  mapovani doplneno  {0}  {1}" -f $guid, $safeAccount)
}

function Invoke-MissingGroups {
    $ProdIds = Resolve-IdsFile $ProdIds 'PROD'
    $TestIds = Resolve-IdsFile $TestIds 'Test'
    if (-not $LogPath) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $LogPath = Join-Path $DataDir ("New-MissingGroups-{0}.log" -f $stamp)
    }
    $logDir = [System.IO.Path]::GetDirectoryName($LogPath)
    if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    $script:LogWriter = New-Object System.IO.StreamWriter ($LogPath, $false, $utf8Bom)
    $script:LogWriter.AutoFlush = $true

    try {
        Write-Log ("Report: {0}" -f $ReportPath)
        Write-Log ("Skupiny PROD: {0}" -f $GroupsXml)
        Write-Log ("PROD ids: {0}" -f $ProdIds)
        Write-Log ("TEST ids: {0}" -f $TestIds)

        if (-not (Test-Path -LiteralPath $FimLibrary)) { throw "Chybi knihovna portalu: $FimLibrary" }
        . $FimLibrary
        $portalUri = $Uri
        if (-not $portalUri) { $portalUri = $DefaultUri }
        Write-Log ("Portal: {0}" -f $portalUri)
        if ($WhatIf) { Write-Log 'Rezim WhatIf: skupiny se nezakladaji.' }

        $prodById = @{}
        foreach ($row in (Read-IdRows $ProdIds)) { $prodById[$row.Id] = $row.Account }
        $testByAccount = @{}
        foreach ($row in (Read-IdRows $TestIds)) {
            if ($testByAccount.ContainsKey($row.Key)) {
                Write-Log ("TEST ids: AccountName {0} je vic nez jednou, pouziva se prvni ObjectID." -f $row.Account)
                continue
            }
            $testByAccount[$row.Key] = $row.Id
        }

        $sourceByAccount = @{}
        foreach ($group in (Read-GroupClixml $GroupsXml)) {
            $account = $null
            $prop = $group.PSObject.Properties['AccountName']
            if ($prop) { $account = [string] $prop.Value }
            if ([string]::IsNullOrWhiteSpace($account)) { continue }
            $key = $account.Trim().ToLowerInvariant()
            if ($sourceByAccount.ContainsKey($key)) {
                Write-Log ("Export skupin: AccountName {0} je vic nez jednou, pouziva se prvni objekt." -f $account.Trim())
                continue
            }
            $sourceByAccount[$key] = $group
        }

        $wanted = Read-ReportGroups $ReportPath
        $ranked = New-Object System.Collections.ArrayList
        $skipped = 0
        foreach ($item in $wanted) {
            $sourceForRank = $null
            $rankKey = $item.Account.ToLowerInvariant()
            if ($sourceByAccount.ContainsKey($rankKey)) { $sourceForRank = $sourceByAccount[$rankKey] }
            $typeName = Get-GroupTypeText $sourceForRank $item.GroupType
            $typeKey = $typeName.ToLowerInvariant()
            if ($typeKey -ne 'apl' -and $typeKey -ne 'bus') {
                $skipped++
                Write-Log ("PRESKAKUJI  {0}  {1}" -f $typeName, $item.Account)
                continue
            }
            $rank = 1
            if ($typeKey -eq 'apl') { $rank = 0 }
            [void] $ranked.Add([pscustomobject]@{
                Account   = $item.Account
                GroupType = $typeName
                Index     = $item.Index
                Rank      = $rank
            })
        }
        $ordered = @($ranked | Sort-Object Rank, Index)
        $aplCount = @($ordered | Where-Object { $_.Rank -eq 0 }).Count
        Write-Log ("Skupin v reportu: {0}   v exportu XML: {1}   apl: {2}   bus: {3}   preskoceno: {4}" -f $wanted.Count, $sourceByAccount.Count, $aplCount, ($ordered.Count - $aplCount), $skipped)
        Write-Log 'Zakladaji se jen csoblGroupType apl a bus. Poradi: nejdrive apl, potom bus.'

        $created = 0
        $exists = 0
        $missing = 0
        $failed = 0

        foreach ($item in $ordered) {
            $accountName = $item.Account
            $key = $accountName.ToLowerInvariant()
            if (-not $sourceByAccount.ContainsKey($key)) {
                $missing++
                Write-Log ("CHYBI V XML  {0}  {1}" -f $item.GroupType, $accountName)
                continue
            }
            $source = $sourceByAccount[$key]
            $displayName = ''
            $displayProp = $source.PSObject.Properties['DisplayName']
            if ($displayProp) { $displayName = [string] $displayProp.Value }

            $portalId = $null
            try {
                $portalId = Find-PortalGroupId $accountName $portalUri
            }
            catch {
                $failed++
                Write-Log ("CHYBA OVERENI  {0}  {1}  {2}" -f $item.GroupType, $accountName, $_.Exception.Message)
                continue
            }
            if ($null -ne $portalId) {
                $exists++
                Write-Log ("UŽ EXISTUJE  {0}  {1}" -f $item.GroupType, $accountName)
                if ($portalId) {
                    Add-TestIdMapping $testByAccount $TestIds $portalId $accountName $displayName
                }
                continue
            }

            Write-Log ("ZAKLADAM  {0}  {1}" -f $item.GroupType, $accountName)
            if ($WhatIf) { continue }

            try {
                $newGroup = CreateImportObject -ObjectType 'Group'
                foreach ($prop in $source.PSObject.Properties) {
                    $name = [string] $prop.Name
                    if ($SkipAttributes.ContainsKey($name)) { continue }
                    if ($name.StartsWith('msidm')) { continue }
                    $copy = $StandardAttributes.ContainsKey($name) -or $name.StartsWith('csobl')
                    if (-not $copy) { continue }
                    Add-CopiedAttribute $newGroup $name $prop.Value $accountName $prodById $testByAccount
                }
                $imported = @($newGroup | Import-FIMConfig -Uri $portalUri)
                $created++
                $target = ''
                if ($imported.Count -gt 0 -and $null -ne $imported[0] -and $imported[0].PSObject.Properties['TargetObjectIdentifier']) {
                    $target = [string] $imported[0].TargetObjectIdentifier
                }
                Write-Log ("ZALOŽENO  {0}  {1}  {2}" -f $item.GroupType, $accountName, $target)
                if ($target) {
                    Add-TestIdMapping $testByAccount $TestIds $target $accountName $displayName
                }
                else {
                    Write-Log ("  mapovani: po zalozeni chybi ObjectID, {0} se do TEST ids nezapsalo" -f $accountName)
                }
            }
            catch {
                $failed++
                Write-Log ("CHYBA ZALOŽENÍ  {0}  {1}  {2}" -f $item.GroupType, $accountName, $_.Exception.Message)
            }
        }

        Write-Log ("Hotovo. Zalozeno={0}  uz existuje={1}  chybi ve XML={2}  chyba={3}  preskoceno={4}" -f $created, $exists, $missing, $failed, $skipped)
        Write-Log ("Log: {0}" -f $LogPath)
    }
    finally {
        if ($script:LogWriter) {
            $script:LogWriter.Dispose()
            $script:LogWriter = $null
        }
    }
}

function Invoke-MissingApplications {
    $ProdIds = Resolve-IdsFile $ProdIds 'PROD'
    $TestIds = Resolve-IdsFile $TestIds 'Test'
    if (-not $LogPath) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $LogPath = Join-Path $DataDir ("New-MissingApps-{0}.log" -f $stamp)
    }
    $logDir = [System.IO.Path]::GetDirectoryName($LogPath)
    if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    $script:LogWriter = New-Object System.IO.StreamWriter ($LogPath, $false, $utf8Bom)
    $script:LogWriter.AutoFlush = $true

    try {
        Write-Log ("Aplikace PROD: {0}" -f $GroupsXml)
        Write-Log ("PROD ids: {0}" -f $ProdIds)
        Write-Log ("TEST ids: {0}" -f $TestIds)
        if (-not (Test-Path -LiteralPath $FimLibrary)) { throw "Chybi knihovna portalu: $FimLibrary" }
        . $FimLibrary
        $portalUri = $Uri
        if (-not $portalUri) { $portalUri = $DefaultUri }
        Write-Log ("Portal: {0}" -f $portalUri)
        if ($WhatIf) { Write-Log 'Rezim WhatIf: aplikace se nezakladaji.' }

        $prodById = @{}
        foreach ($row in (Read-IdRows $ProdIds)) { $prodById[$row.Id] = $row.Account }
        $testByAccount = @{}
        foreach ($row in (Read-IdRows $TestIds)) {
            if ($testByAccount.ContainsKey($row.Key)) {
                Write-Log ("TEST ids: klic {0} je vic nez jednou, pouziva se prvni ObjectID." -f $row.Account)
                continue
            }
            $testByAccount[$row.Key] = $row.Id
        }

        $created = 0
        $exists = 0
        $skipped = 0
        $failed = 0
        $seen = @{}
        $sources = Read-GroupClixml $GroupsXml
        Write-Log ("Objektu v XML: {0}" -f @($sources).Count)

        foreach ($source in $sources) {
            $identity = Get-SourceIdentity $source
            if ($null -eq $identity) {
                $skipped++
                Write-Log 'PRESKAKUJI  chybi AccountName i DisplayName'
                continue
            }
            $portalType = Get-PropertyText $source 'ObjectType'
            if (-not $portalType) { $portalType = 'RoleApplication' }
            $key = $identity.Value.ToLowerInvariant()
            if ($seen.ContainsKey($portalType.ToLowerInvariant() + '|' + $key)) {
                $skipped++
                Write-Log ("PRESKAKUJI  duplicita v XML  {0}  {1}={2}" -f $portalType, $identity.Attribute, $identity.Value)
                continue
            }
            $seen[$portalType.ToLowerInvariant() + '|' + $key] = $true

            $portalId = $null
            try {
                $portalId = Find-PortalObjectId $portalType $identity.Attribute $identity.Value $portalUri
            }
            catch {
                $failed++
                Write-Log ("CHYBA OVERENI  {0}  {1}={2}  {3}" -f $portalType, $identity.Attribute, $identity.Value, $_.Exception.Message)
                continue
            }
            if ($null -ne $portalId) {
                $exists++
                Write-Log ("UŽ EXISTUJE  {0}  {1}={2}" -f $portalType, $identity.Attribute, $identity.Value)
                if ($portalId) {
                    Add-TestIdMapping $testByAccount $TestIds $portalId $identity.Value $identity.DisplayName
                }
                continue
            }

            Write-Log ("ZAKLADAM  {0}  {1}={2}" -f $portalType, $identity.Attribute, $identity.Value)
            if ($WhatIf) { continue }

            try {
                $newObject = CreateImportObject -ObjectType $portalType
                foreach ($prop in $source.PSObject.Properties) {
                    $name = [string] $prop.Name
                    if ($SkipAttributes.ContainsKey($name)) { continue }
                    if ($name.StartsWith('msidm')) { continue }
                    $copy = $StandardAttributes.ContainsKey($name) -or $name.StartsWith('csobl')
                    if (-not $copy) { continue }
                    Add-CopiedAttribute $newObject $name $prop.Value $identity.Value $prodById $testByAccount
                }
                $imported = @($newObject | Import-FIMConfig -Uri $portalUri)
                $created++
                $target = ''
                if ($imported.Count -gt 0 -and $null -ne $imported[0] -and $imported[0].PSObject.Properties['TargetObjectIdentifier']) {
                    $target = [string] $imported[0].TargetObjectIdentifier
                }
                Write-Log ("ZALOŽENO  {0}  {1}={2}  {3}" -f $portalType, $identity.Attribute, $identity.Value, $target)
                if ($target) {
                    Add-TestIdMapping $testByAccount $TestIds $target $identity.Value $identity.DisplayName
                }
                else {
                    Write-Log ("  mapovani: po zalozeni chybi ObjectID, {0} se do TEST ids nezapsalo" -f $identity.Value)
                }
            }
            catch {
                $failed++
                Write-Log ("CHYBA ZALOŽENÍ  {0}  {1}={2}  {3}" -f $portalType, $identity.Attribute, $identity.Value, $_.Exception.Message)
            }
        }

        Write-Log ("Hotovo. Zalozeno={0}  uz existuje={1}  preskoceno={2}  chyba={3}" -f $created, $exists, $skipped, $failed)
        Write-Log ("Log: {0}" -f $LogPath)
    }
    finally {
        if ($script:LogWriter) {
            $script:LogWriter.Dispose()
            $script:LogWriter = $null
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    if ($ObjectType -eq 'RoleApplication') { Invoke-MissingApplications }
    else { Invoke-MissingGroups }
}
