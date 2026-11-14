#Requires -Version 5.1
<#
.SYNOPSIS
    Odebere vybrané uživatele ze skupin založených podle reportu objekty-prebyvaji-v-PROD.

.DESCRIPTION
    Skupiny bere z reportu. Zpracuje jen Group s csoblGroupType apl nebo bus.
    Uživatele najde podle AccountName a odebere je z csoblComputedMember
    a ExplicitMember, pokud tam jsou.

.PARAMETER ReportPath
    Report objekty-prebyvaji-v-PROD.txt.

.PARAMETER Account
    AccountName uživatelů, které je potřeba odebrat.

.PARAMETER WhatIf
    Jen vypíše, co by se odebralo. Na portál nic nezapíše.

.EXAMPLE
    .\Remove-CreatedGroupMembers.ps1 -WhatIf
    .\Remove-CreatedGroupMembers.ps1
#>
[CmdletBinding()]
param(
    [string] $ReportPath,
    [string[]] $Account = @(
        '17707258', 'gyorgy', 'rosova', '17709660', '17707111', '17709634', 'balaz',
        '17709638', '17707035', '17709647', '17709642', '17707299', '17709672',
        '17709639', '17707024', '17706884', '17709650', '17709656', '17709669',
        '17706466', '17709635', '17709621', '17709637', '17709640', '17706927',
        '17707298', '17709624', '17707173', '17709616', '17706917', '17709652',
        '17701194', '17709686', '17709673', '17706481', '17709619', '17709645',
        '17709641', 'usacevova_admin', '17706481', 'mikula', 'mikula_admin', 
        'jelinkova', 'nikmonova', '17706813', '17702053', 'tack', '17707004', '17707111', '', '17709639', '17706466', '17709619'
    ),
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
if (-not $FimLibrary) { $FimLibrary = 'C:\Batch\FIMAutomation\FIMPowerShell.ps1' }

function Write-Log {
    param([string] $Message)
    $line = '{0}  {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    if ($script:LogWriter) { $script:LogWriter.WriteLine($line) }
}

function Read-ReportAplBus {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Report neexistuje: $Path" }
    $utf8 = New-Object System.Text.UTF8Encoding $true
    $lines = [System.IO.File]::ReadAllLines($Path, $utf8)
    $names = New-Object System.Collections.Generic.List[object]
    $headerSeen = $false
    foreach ($line in $lines) {
        if (-not $headerSeen) {
            if ($line.StartsWith("Typ`t")) { $headerSeen = $true }
            continue
        }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line.Split("`t")
        if ($parts.Length -lt 8) { continue }
        if ($parts[0].Trim() -ne 'Group') { continue }
        $account = $parts[1].Trim()
        $groupType = $parts[7].Trim()
        $typeKey = $groupType.ToLowerInvariant()
        if ($typeKey -ne 'apl' -and $typeKey -ne 'bus') { continue }
        if (-not $account) { continue }
        $names.Add([pscustomobject]@{ Account = $account; GroupType = $groupType })
    }
    return ,$names.ToArray()
}

function Get-GuidKey {
    param([string] $Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $match = [regex]::Match($Value, '(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')
    if (-not $match.Success) { return $null }
    return $match.Value.ToLowerInvariant()
}

function Get-PropertyText {
    param($Object, [string[]] $Names)
    if ($null -eq $Object) { return '' }
    foreach ($name in $Names) {
        $prop = $Object.PSObject.Properties[$name]
        if ($prop -and $null -ne $prop.Value -and -not [string]::IsNullOrWhiteSpace([string] $prop.Value)) {
            return [string] $prop.Value
        }
    }
    return ''
}

function Convert-ToPortalObject {
    param($Raw)
    if ($null -eq $Raw) { return $null }
    $source = $Raw
    $rmo = $Raw.PSObject.Properties['ResourceManagementObject']
    if ($rmo -and $null -ne $rmo.Value) { $source = $rmo.Value }
    $obj = New-Object PSObject
    $attrs = $source.PSObject.Properties['ResourceManagementAttributes']
    if ($attrs -and $null -ne $attrs.Value) {
        foreach ($attr in $attrs.Value) {
            $nameProp = $attr.PSObject.Properties['AttributeName']
            if (-not $nameProp -or [string]::IsNullOrWhiteSpace([string] $nameProp.Value)) { continue }
            $isMulti = $false
            $multiProp = $attr.PSObject.Properties['IsMultiValue']
            if ($multiProp -and $multiProp.Value) { $isMulti = $true }
            $value = $null
            if ($isMulti) {
                $valuesProp = $attr.PSObject.Properties['Values']
                if ($valuesProp) { $value = $valuesProp.Value }
            }
            else {
                $valueProp = $attr.PSObject.Properties['Value']
                if ($valueProp) { $value = $valueProp.Value }
            }
            $obj | Add-Member -NotePropertyName ([string] $nameProp.Value) -NotePropertyValue $value -Force
        }
    }
    $idText = Get-PropertyText $obj 'ObjectID'
    if (-not $idText) { $idText = Get-PropertyText $source 'ObjectIdentifier', 'ObjectID' }
    if (-not $idText) { $idText = Get-PropertyText $Raw 'ObjectIdentifier', 'ObjectID', 'TargetObjectIdentifier', 'SourceObjectIdentifier' }
    if ($idText) {
        if ($obj.PSObject.Properties['ObjectID']) { $obj.PSObject.Properties['ObjectID'].Value = $idText }
        else { $obj | Add-Member -NotePropertyName 'ObjectID' -NotePropertyValue $idText }
    }
    if (-not $obj.PSObject.Properties['ObjectID'] -and -not $attrs) { return $null }
    return $obj
}

function Get-PortalObjects {
    param([string] $XPath, [string] $PortalUri, [string] $AccountName, [switch] $IncludeReferences)
    if ($IncludeReferences) {
        $found = Export-FIMConfig -customconfig $XPath -Uri $PortalUri
    }
    else {
        $found = Export-FIMConfig -onlyBaseResources -customconfig $XPath -Uri $PortalUri
    }
    $list = New-Object System.Collections.Generic.List[object]
    if ($null -eq $found) { return ,$list.ToArray() }
    $items = @()
    if ($found -is [System.Array]) { $items = @($found | Where-Object { $null -ne $_ }) }
    else { $items = @($found) }
    foreach ($item in $items) {
        $obj = Convert-ToPortalObject $item
        if ($null -eq $obj) { continue }
        if ($AccountName) {
            $got = (Get-PropertyText $obj 'AccountName').Trim()
            if (-not $got -or $got.ToLowerInvariant() -ne $AccountName.Trim().ToLowerInvariant()) { continue }
        }
        $list.Add($obj)
    }
    return ,$list.ToArray()
}

function Get-MemberRefs {
    param($Object, [string] $AttributeName)
    $prop = $Object.PSObject.Properties[$AttributeName]
    if (-not $prop -or $null -eq $prop.Value) { return @() }
    $refs = New-Object System.Collections.Generic.List[object]
    foreach ($item in @($prop.Value)) {
        $raw = [string] $item
        $guid = Get-GuidKey $raw
        if (-not $guid) { continue }
        $value = $raw.Trim()
        if ($value -notmatch '(?i)^urn:uuid:') { $value = 'urn:uuid:' + $guid }
        $refs.Add([pscustomobject]@{ Guid = $guid; Value = $value })
    }
    return ,$refs.ToArray()
}

function Invoke-RemoveMembers {
    $Account = @($Account | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
    if ($Account.Count -eq 0) { throw 'Zadejte aspon jeden -Account.' }

    if (-not $LogPath) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $LogPath = Join-Path $DataDir ("Remove-CreatedGroupMembers-{0}.log" -f $stamp)
    }
    $logDir = [System.IO.Path]::GetDirectoryName($LogPath)
    if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    $script:LogWriter = New-Object System.IO.StreamWriter ($LogPath, $false, $utf8Bom)
    $script:LogWriter.AutoFlush = $true

    try {
        Write-Log ("Report: {0}" -f $ReportPath)
        if (-not (Test-Path -LiteralPath $FimLibrary)) { throw "Chybi knihovna portalu: $FimLibrary" }
        . $FimLibrary
        $portalUri = $Uri
        if (-not $portalUri) { $portalUri = $DefaultUri }
        Write-Log ("Portal: {0}" -f $portalUri)
        if ($WhatIf) { Write-Log 'Rezim WhatIf: clenove se neodebiraji.' }

        $groups = Read-ReportAplBus $ReportPath
        $apl = @($groups | Where-Object { $_.GroupType.ToLowerInvariant() -eq 'apl' }).Count
        $bus = @($groups | Where-Object { $_.GroupType.ToLowerInvariant() -eq 'bus' }).Count
        Write-Log ("Skupin z reportu apl+bus: {0}   apl: {1}   bus: {2}" -f $groups.Count, $apl, $bus)

        $people = @{}
        foreach ($name in $Account) {
            $escaped = $name.Replace("'", "''")
            $found = Get-PortalObjects "/Person[AccountName='$escaped']" $portalUri $name
            if ($null -eq $found -or $found.Count -eq 0) {
                Write-Log ("UZIVATEL CHYBI  {0}" -f $name)
                continue
            }
            if ($found.Count -gt 1) {
                Write-Log ("UZIVATEL NEJEDNOZNACNY  {0}  pocet={1}" -f $name, $found.Count)
                continue
            }
            $guid = Get-GuidKey (Get-PropertyText $found[0] 'ObjectID')
            if (-not $guid) {
                Write-Log ("UZIVATEL BEZ OBJECTID  {0}" -f $name)
                continue
            }
            $people[$name.ToLowerInvariant()] = [pscustomobject]@{ Account = $name; Id = $guid }
            Write-Log ("UZIVATEL  {0}  {1}" -f $name, $guid)
        }
        if ($people.Count -eq 0) { throw 'Zadny z uvedeny uzivatelu se na portalu nenasel.' }

        $changed = 0
        $missingGroup = 0
        $unchanged = 0
        $failed = 0
        $removed = 0

        foreach ($group in $groups) {
            $escaped = $group.Account.Replace("'", "''")
            try {
                $found = Get-PortalObjects "/Group[AccountName='$escaped']" $portalUri $group.Account
            }
            catch {
                $failed++
                Write-Log ("CHYBA CTENI  {0}  {1}  {2}" -f $group.GroupType, $group.Account, $_.Exception.Message)
                continue
            }
            if ($null -eq $found -or $found.Count -eq 0) {
                $missingGroup++
                Write-Log ("SKUPINA CHYBI  {0}  {1}" -f $group.GroupType, $group.Account)
                continue
            }
            if ($found.Count -ne 1) {
                $failed++
                Write-Log ("SKUPINA NEJEDNOZNACNA  {0}  {1}  pocet={2}" -f $group.GroupType, $group.Account, $found.Count)
                continue
            }
            $groupId = Get-GuidKey (Get-PropertyText $found[0] 'ObjectID')
            if (-not $groupId) {
                $failed++
                Write-Log ("SKUPINA BEZ OBJECTID  {0}  {1}" -f $group.GroupType, $group.Account)
                continue
            }
            $detailed = Get-PortalObjects "/Group[ObjectID='$groupId']" $portalUri '' -IncludeReferences
            $portalGroup = $null
            foreach ($candidate in @($detailed)) {
                $candidateId = Get-GuidKey (Get-PropertyText $candidate 'ObjectID')
                if ($candidateId -eq $groupId) { $portalGroup = $candidate; break }
            }
            if ($null -eq $portalGroup) { $portalGroup = $found[0] }

            $memberAttrs = @('ExplicitMember', 'csoblComputedMember')
            $hits = New-Object System.Collections.Generic.List[object]
            foreach ($attrName in $memberAttrs) {
                $memberRefs = @(Get-MemberRefs $portalGroup $attrName)
                foreach ($person in $people.Values) {
                    foreach ($memberRef in $memberRefs) {
                        if ($memberRef.Guid -eq $person.Id) {
                            $hits.Add([pscustomobject]@{ Attribute = $attrName; Person = $person; Value = $memberRef.Value })
                            break
                        }
                    }
                }
            }
            if ($hits.Count -eq 0) {
                $unchanged++
                continue
            }

            Write-Log ("ODEBIRAM  {0}  {1}  clenu={2}" -f $group.GroupType, $group.Account, $hits.Count)
            foreach ($hit in $hits) {
                Write-Log ("  {0}  {1}  {2}" -f $hit.Attribute, $hit.Person.Account, $hit.Value)
            }
            if ($WhatIf) { continue }

            try {
                $modify = ModifyImportObject -TargetIdentifier ("urn:uuid:" + $groupId) -ObjectType 'Group'
                foreach ($hit in $hits) {
                    RemoveMultiValue -ImportObject $modify -AttributeName $hit.Attribute -NewAttributeValue $hit.Value -FullyResolved 1
                }
                $null = $modify | Import-FIMConfig -Uri $portalUri
                $changed++
                $removed += $hits.Count
            }
            catch {
                $failed++
                Write-Log ("CHYBA ODEBRANI  {0}  {1}  {2}" -f $group.GroupType, $group.Account, $_.Exception.Message)
            }
        }

        Write-Log ("Hotovo. Skupin zmeneno={0}  beze zmeny={1}  chybi na portalu={2}  chyba={3}  odebranych clenstvi={4}" -f $changed, $unchanged, $missingGroup, $failed, $removed)
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
    Invoke-RemoveMembers
}
