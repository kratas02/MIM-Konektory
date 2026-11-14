<#
.SYNOPSIS
    Exportuje pending Add / Update / Delete z daneho management agenta
    do HTML a TXT reportu, zvlast pro kazdy typ objektu.

.DESCRIPTION
    Skript na MIM Sync serveru spusti csexport.exe /f:x pro zadany MA
    a z XML sestavi reporty. SQL se nepouziva.

    HTML: pro kazdy meneny objekt tabulka jen s menenymi atributy.
    TXT:  identifikator, typ objektu, operace, atribut, stara a nova hodnota.
    U vicehodnotovych atributu se vypisuji jen pridavane / odebirane hodnoty.

    Do reportu se nedostanou atributy company a extensionAttribute8/9/12/13/14.
    Update objekt, ktery po vyrazeni nema zadnou jinou zmenu, se vynecha.

    -RemovedGroupMembers csexport nijak neoreze. /f:x je nejužší přepínač a u
    kazde skupiny do XML stejne zapise i hologram se vsemi soucasnymi cleny.
    Skript hologram pri cteni preskakuje a do reportu da jen hodnoty member
    s operation=delete z unapplied-export. XML po reportu zustava. Pri dalsim
    csexportu se predchozi *_pending_*.xml daneho MA smaze.

.PARAMETER ManagementAgent
    Nazev management agenta tak, jak je videt v Synchronization Service Manageru.

.PARAMETER RemovedGroupMembers
    Místo plného reportu zapíše jen odebrané členy skupin (atribut member,
    operation=delete).

.PARAMETER ObjectType
    Typ objektu v connector space. S -RemovedGroupMembers je výchozí group.

.PARAMETER Attribute
    Atribut, jehož odebrané hodnoty se mají vypsat. S -RemovedGroupMembers
    je výchozí member. Lze zadat víc atributů.

.PARAMETER XmlPath
    Hotové csexport XML. Když je zadané, csexport se nespouští a soubor se
    nemaže.

.EXAMPLE
    .\Export-PendingExports.ps1 AD
    .\Export-PendingExports.ps1 -ManagementAgent "kratest.local"
    .\Export-PendingExports.ps1 AD -XmlPath "D:\Export\AD_pending.xml"
    .\Export-PendingExports.ps1 AD -RemovedGroupMembers
    .\Export-PendingExports.ps1 AD -RemovedGroupMembers -ObjectType group -Attribute member
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $ManagementAgent,

    [switch] $RemovedGroupMembers,

    [string] $ObjectType,

    [string[]] $Attribute,

    [string] $XmlPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ExcludeAttributes = @(
    'company',
    'extensionAttribute8',
    'extensionAttribute9',
    'extensionAttribute12',
    'extensionAttribute13',
    'extensionAttribute14'
)

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

function Resolve-CsExportExe {
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Forefront Identity Manager\2010\Synchronization Service\Bin\csexport.exe",
        "${env:ProgramFiles}\Microsoft Identity Manager\2016\Synchronization Service\Bin\csexport.exe",
        "${env:ProgramFiles}\Microsoft Azure AD Sync\Bin\csexport.exe",
        "${env:ProgramFiles}\Microsoft Azure AD Connect Sync\Bin\csexport.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) { return $path }
    }
    throw "csexport.exe nebyl nalezen. Spuste skript na MIM Sync serveru."
}

function Invoke-CsExport {
    param(
        [Parameter(Mandatory = $true)][string] $MaName,
        [Parameter(Mandatory = $true)][string] $OutputXml,
        [Parameter(Mandatory = $true)][string] $ExePath
    )

    $dir = Split-Path -Parent $OutputXml
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    if (Test-Path -LiteralPath $OutputXml) {
        Remove-Item -LiteralPath $OutputXml -Force
    }

    Write-Log "Spoustim csexport /f:x pro MA '$MaName' ..."
    $p = Start-Process -FilePath $ExePath -ArgumentList @($MaName, $OutputXml, '/f:x') -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        throw "csexport skoncil s kodem $($p.ExitCode). Zkontrolujte nazev MA a ze skript bezi na Sync serveru."
    }
    if (-not (Test-Path -LiteralPath $OutputXml)) {
        throw "csexport skoncil uspesne, ale XML soubor nevznikl: $OutputXml"
    }
    Write-Log "XML ulozeno: $OutputXml"
}

function ConvertTo-NormalizedOperation {
    param([string] $Raw)
    if ([string]::IsNullOrWhiteSpace($Raw)) { return 'Update' }
    switch ($Raw.Trim().ToLowerInvariant()) {
        'add'        { 'Add' }
        'update'     { 'Update' }
        'replace'    { 'Update' }
        'delete'     { 'Delete' }
        'delete-add' { 'DeleteAdd' }
        'deleteadd'  { 'DeleteAdd' }
        default      { $Raw }
    }
}

function Test-AttributeWanted {
    param([string] $Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    foreach ($skip in $script:ExcludeAttributes) {
        if ([string]::Equals($Name, $skip, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    }
    return $true
}

function ConvertTo-SafeFileName {
    param([string] $Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return 'unknown' }
    $safe = ($Name -replace '[\\/:*?"<>|]', '_')
    $safe = $safe.Trim()
    if ([string]::IsNullOrWhiteSpace($safe)) { return 'unknown' }
    return $safe
}

function Get-AttributeValuesFromElement {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlElement] $AttrElement
    )

    $result = New-Object System.Collections.Generic.List[object]
    foreach ($child in @($AttrElement.ChildNodes)) {
        if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $op = $child.GetAttribute('operation')
        if ([string]::IsNullOrWhiteSpace($op)) { $op = $null }

        if ($child.LocalName -eq 'value') {
            $result.Add([pscustomobject]@{ Operation = $op; Value = [string]$child.InnerText })
        }
        elseif ($child.LocalName -eq 'dn-value') {
            $dnNode = $child['dn']
            $dn = if ($dnNode) { [string]$dnNode.InnerText } else { [string]$child.InnerText }
            $result.Add([pscustomobject]@{ Operation = $op; Value = $dn })
        }
    }
    return $result
}

function Get-HologramMap {
    param([System.Xml.XmlElement] $CsObject)

    $map = @{}
    $entry = $null
    foreach ($sectionName in @('synchronized-hologram', 'hologram', 'import-hologram')) {
        $section = $CsObject[$sectionName]
        if ($section -and $section['entry']) {
            $entry = $section['entry']
            break
        }
    }
    if (-not $entry) { return $map }

    foreach ($attr in @($entry.ChildNodes)) {
        if ($attr.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        if ($attr.LocalName -ne 'attr' -and $attr.LocalName -ne 'dn-attr') { continue }
        $name = $attr.GetAttribute('name')
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $values = @(Get-AttributeValuesFromElement -AttrElement $attr | ForEach-Object { $_.Value })
        $map[$name] = $values
    }
    return $map
}

function ConvertTo-AccountExpiresDisplay {
    param([string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $Text }

    $raw = $Text.Trim()
    $ticks = [int64]0
    if ($raw -match '^\d+$') {
        if (-not [int64]::TryParse($raw, [ref]$ticks)) { return $Text }
    }
    elseif ($raw -match '^0[xX]([0-9a-fA-F]+)$') {
        $hex = $Matches[1]
        if ($hex.Length -gt 16) { return $Text }
        try {
            $ticks = [Convert]::ToInt64($hex, 16)
        }
        catch {
            return $Text
        }
    }
    else {
        return $Text
    }

    # 0 a 0x7FFFFFFFFFFFFFFF v AD znamena, ze ucet nevyprsi
    if ($ticks -le 0 -or $ticks -eq [int64]::MaxValue) { return 'nikdy' }

    try {
        return [DateTime]::FromFileTime($ticks).ToString('yyyy-MM-dd HH:mm:ss')
    }
    catch {
        return $Text
    }
}

function ConvertTo-UserAccountControlDisplay {
    param([string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $Text }

    $raw = $Text.Trim()
    $flags = [int64]0
    if ($raw -match '^\d+$') {
        if (-not [int64]::TryParse($raw, [ref]$flags)) { return $Text }
    }
    elseif ($raw -match '^0[xX]([0-9a-fA-F]+)$') {
        $hex = $Matches[1]
        if ($hex.Length -gt 16) { return $Text }
        try {
            $flags = [Convert]::ToInt64($hex, 16)
        }
        catch {
            return $Text
        }
    }
    else {
        return $Text
    }

    # ACCOUNTDISABLE = 0x0002
    $state = if (($flags -band 2) -eq 2) { 'neaktivni' } else { 'aktivni' }
    return "$raw ($state)"
}

function Add-ChangeRow {
    param(
        [System.Collections.Generic.List[object]] $Rows,
        [string] $Attribute,
        [string] $AttrOperation,
        [bool] $IsMulti,
        [string] $OldValue,
        [string] $NewValue
    )
    if ([string]::Equals($Attribute, 'accountExpires', [StringComparison]::OrdinalIgnoreCase)) {
        $OldValue = ConvertTo-AccountExpiresDisplay $OldValue
        $NewValue = ConvertTo-AccountExpiresDisplay $NewValue
    }
    elseif ([string]::Equals($Attribute, 'userAccountControl', [StringComparison]::OrdinalIgnoreCase)) {
        $OldValue = ConvertTo-UserAccountControlDisplay $OldValue
        $NewValue = ConvertTo-UserAccountControlDisplay $NewValue
    }
    $Rows.Add([pscustomobject]@{
        Attribute     = $Attribute
        AttrOperation = $AttrOperation
        IsMulti       = $IsMulti
        OldValue      = $OldValue
        NewValue      = $NewValue
    })
}

function ConvertTo-Joined {
    param([string[]] $Values)
    if (-not $Values -or $Values.Count -eq 0) { return $null }
    return ($Values -join '; ')
}

function Get-PendingChangesFromCsObject {
    param([System.Xml.XmlElement] $CsObject)

    $dn = $CsObject.GetAttribute('cs-dn')
    $objectTypeName = $CsObject.GetAttribute('object-type')
    $objectId = $CsObject.GetAttribute('id')

    $export = $CsObject['unapplied-export']
    if (-not $export) { return $null }
    $delta = $export['delta']
    if (-not $delta) { return $null }

    $rawOp = $delta.GetAttribute('operation')
    $operationName = ConvertTo-NormalizedOperation -Raw $rawOp
    if (-not $dn) { $dn = $delta.GetAttribute('dn') }
    $newDn = $delta.GetAttribute('newdn')

    $hologram = Get-HologramMap -CsObject $CsObject
    $rows = New-Object System.Collections.Generic.List[object]

    if (-not [string]::IsNullOrWhiteSpace($newDn) -and $newDn -ne $dn) {
        Add-ChangeRow -Rows $rows -Attribute 'DN' -AttrOperation 'replace' -IsMulti $false -OldValue $dn -NewValue $newDn
    }

    $isObjectAdd = ($operationName -eq 'Add')
    $isObjectDelete = ($operationName -eq 'Delete')

    foreach ($attr in @($delta.ChildNodes)) {
        if ($attr.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        if ($attr.LocalName -ne 'attr' -and $attr.LocalName -ne 'dn-attr') { continue }

        $attrName = $attr.GetAttribute('name')
        if ([string]::IsNullOrWhiteSpace($attrName)) { continue }
        if (-not (Test-AttributeWanted -Name $attrName)) { continue }

        $isMulti = ($attr.GetAttribute('multivalued') -eq 'true')
        $attrOp = $attr.GetAttribute('operation')
        if ([string]::IsNullOrWhiteSpace($attrOp)) {
            $attrOp = if ($isObjectAdd) { 'add' } elseif ($isObjectDelete) { 'delete' } else { 'replace' }
        }

        $deltaValues = @(Get-AttributeValuesFromElement -AttrElement $attr)
        $oldValues = @()
        if ($hologram.ContainsKey($attrName)) { $oldValues = @($hologram[$attrName]) }

        $added = @($deltaValues | Where-Object { $_.Operation -eq 'add' } | ForEach-Object { $_.Value })
        $removed = @($deltaValues | Where-Object { $_.Operation -eq 'delete' } | ForEach-Object { $_.Value })
        $plain = @($deltaValues | Where-Object { -not $_.Operation } | ForEach-Object { $_.Value })

        # MIM u jednokhodnotoveho update casto dava
        # <value operation="delete">stare</value><value operation="add">nove</value>
        if (-not $isMulti -and ($added.Count -gt 0 -or $removed.Count -gt 0)) {
            $old = ConvertTo-Joined $(if ($removed.Count) { $removed } else { $oldValues })
            $new = ConvertTo-Joined $added
            if ($attrOp -eq 'delete') { $new = $null }
            if ($attrOp -eq 'add') { $old = $null }
            Add-ChangeRow -Rows $rows -Attribute $attrName -AttrOperation $attrOp -IsMulti $false -OldValue $old -NewValue $new
            continue
        }

        if ($isMulti) {
            if ($attrOp -eq 'delete' -and $added.Count -eq 0 -and $removed.Count -eq 0 -and $plain.Count -eq 0) {
                foreach ($v in $oldValues) {
                    Add-ChangeRow -Rows $rows -Attribute $attrName -AttrOperation 'delete' -IsMulti $true -OldValue $v -NewValue $null
                }
                if ($oldValues.Count -eq 0) {
                    Add-ChangeRow -Rows $rows -Attribute $attrName -AttrOperation 'delete' -IsMulti $true -OldValue $null -NewValue $null
                }
                continue
            }

            if ($attrOp -eq 'add' -and $added.Count -eq 0) { $added = $plain }
            foreach ($v in $added) {
                Add-ChangeRow -Rows $rows -Attribute $attrName -AttrOperation 'add' -IsMulti $true -OldValue $null -NewValue $v
            }
            foreach ($v in $removed) {
                Add-ChangeRow -Rows $rows -Attribute $attrName -AttrOperation 'delete' -IsMulti $true -OldValue $v -NewValue $null
            }
            if ($added.Count -eq 0 -and $removed.Count -eq 0 -and $plain.Count -gt 0) {
                foreach ($v in $plain) {
                    Add-ChangeRow -Rows $rows -Attribute $attrName -AttrOperation $attrOp -IsMulti $true -OldValue $null -NewValue $v
                }
            }
            continue
        }

        switch ($attrOp) {
            'delete' {
                $old = if ($plain.Count) { ConvertTo-Joined $plain } else { ConvertTo-Joined $oldValues }
                Add-ChangeRow -Rows $rows -Attribute $attrName -AttrOperation 'delete' -IsMulti $false -OldValue $old -NewValue $null
            }
            'add' {
                $new = ConvertTo-Joined $(if ($plain.Count) { $plain } else { $added })
                Add-ChangeRow -Rows $rows -Attribute $attrName -AttrOperation 'add' -IsMulti $false -OldValue $null -NewValue $new
            }
            default {
                $new = ConvertTo-Joined $(if ($plain.Count) { $plain } else { $added })
                $old = ConvertTo-Joined $oldValues
                Add-ChangeRow -Rows $rows -Attribute $attrName -AttrOperation $attrOp -IsMulti $false -OldValue $old -NewValue $new
            }
        }
    }

    if ($isObjectDelete -and $rows.Count -eq 0 -and $hologram.Count -gt 0) {
        foreach ($name in ($hologram.Keys | Sort-Object)) {
            if (-not (Test-AttributeWanted -Name $name)) { continue }
            $vals = @($hologram[$name])
            $isMulti = ($vals.Count -gt 1)
            if ($isMulti) {
                foreach ($v in $vals) {
                    Add-ChangeRow -Rows $rows -Attribute $name -AttrOperation 'delete' -IsMulti $true -OldValue $v -NewValue $null
                }
            }
            else {
                Add-ChangeRow -Rows $rows -Attribute $name -AttrOperation 'delete' -IsMulti $false -OldValue (ConvertTo-Joined $vals) -NewValue $null
            }
        }
    }

    return [pscustomobject]@{
        Identifier    = $dn
        ObjectType    = $objectTypeName
        ObjectId      = $objectId
        Operation     = $operationName
        RawOperation  = $rawOp
        Changes       = $rows
    }
}

function Read-PendingExportsFromXml {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "XML soubor neexistuje: $Path"
    }

    $objects = New-Object System.Collections.Generic.List[object]
    $totalCsObjects = 0
    $pendingCsObjects = 0
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.IgnoreWhitespace = $true
    $settings.XmlResolver = $null

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $reader = [System.Xml.XmlReader]::Create($stream, $settings)
    try {
        # ReadOuterXml/Skip uz posunou reader na dalsi uzel - dalsi Read() by
        # preskocil kazdy druhy cs-object.
        $keepReading = $reader.Read()
        while ($keepReading) {
            if ($reader.NodeType -ne [System.Xml.XmlNodeType]::Element -or $reader.LocalName -ne 'cs-object') {
                $keepReading = $reader.Read()
                continue
            }

            $totalCsObjects++
            $outer = $reader.ReadOuterXml()
            $keepReading = ($reader.NodeType -ne [System.Xml.XmlNodeType]::None)

            if ($outer -notlike '*<unapplied-export*') {
                continue
            }
            $pendingCsObjects++

            $doc = New-Object System.Xml.XmlDocument
            $doc.XmlResolver = $null
            $doc.LoadXml($outer)
            $parsed = Get-PendingChangesFromCsObject -CsObject $doc.DocumentElement
            if (-not $parsed) { continue }
            # Update jen s vyloucenymi atributy do reportu nepatri
            if ($parsed.Operation -eq 'Update' -and ($null -eq $parsed.Changes -or $parsed.Changes.Count -eq 0)) {
                continue
            }
            $objects.Add($parsed)
        }
    }
    finally {
        $reader.Close()
        $stream.Close()
    }
    return [pscustomobject]@{
        Objects           = $objects
        TotalCsObjects    = $totalCsObjects
        PendingCsObjects  = $pendingCsObjects
    }
}

function ConvertTo-HtmlEncoded {
    param([string] $Text)
    if ($null -eq $Text) { return '' }
    return [System.Net.WebUtility]::HtmlEncode($Text)
}

function ConvertTo-DisplayValue {
    param([string] $Text)
    if ($null -eq $Text -or $Text -eq '') { return '' }
    return ($Text -replace "`r`n", ' ' -replace "`n", ' ' -replace "`r", ' ')
}

function Write-PendingExportsHtml {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Title,
        [Parameter(Mandatory = $true)]$Objects,
        [string] $Note
    )

    $addCount = @($Objects | Where-Object { $_.Operation -eq 'Add' }).Count
    $updCount = @($Objects | Where-Object { $_.Operation -eq 'Update' }).Count
    $delCount = @($Objects | Where-Object { $_.Operation -eq 'Delete' }).Count
    $otherCount = @($Objects | Where-Object { $_.Operation -notin @('Add', 'Update', 'Delete') }).Count
    $generated = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<!DOCTYPE html>')
    [void]$sb.AppendLine('<html lang="cs"><head><meta charset="utf-8" />')
    [void]$sb.AppendLine("<title>$(ConvertTo-HtmlEncoded $Title) - pending exporty</title>")
    [void]$sb.AppendLine(@'
<style>
  body { font-family: Segoe UI, Tahoma, Arial, sans-serif; margin: 24px; color: #222; }
  h1 { font-size: 22px; margin-bottom: 4px; }
  .meta { color: #555; margin-bottom: 16px; }
  .summary { margin: 0 0 20px 0; padding: 0; }
  .summary span { display: inline-block; margin-right: 14px; padding: 3px 8px; border-radius: 4px; }
  .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; color: #fff; font-size: 12px; font-weight: 600; }
  .op-add { background: #2e7d32; }
  .op-update { background: #ef6c00; }
  .op-delete { background: #c62828; }
  .op-other { background: #546e7a; }
  .obj { margin: 22px 0 28px 0; page-break-inside: avoid; }
  .obj h2 { font-size: 16px; margin: 0 0 6px 0; }
  .obj .dn { font-family: Consolas, monospace; font-size: 13px; word-break: break-all; }
  table { border-collapse: collapse; width: 100%; margin-top: 8px; }
  th, td { border: 1px solid #cfcfcf; padding: 6px 8px; vertical-align: top; text-align: left; }
  th { background: #f3f3f3; }
  td.old { background: #fff3f3; }
  td.new { background: #f1fff3; }
  .empty { color: #999; font-style: italic; }
  .none { color: #666; }
  .warn { background: #fff8e1; border: 1px solid #f0c36d; padding: 10px 12px; margin: 12px 0 20px 0; }
</style>
</head><body>
'@)

    [void]$sb.AppendLine("<h1>Pending exporty: $(ConvertTo-HtmlEncoded $Title)</h1>")
    [void]$sb.AppendLine("<div class=`"meta`">Vygenerovano $generated &nbsp;|&nbsp; objektu: $($Objects.Count)</div>")
    [void]$sb.AppendLine("<div class=`"summary`">")
    [void]$sb.AppendLine("<span class=`"badge op-add`">Add $addCount</span>")
    [void]$sb.AppendLine("<span class=`"badge op-update`">Update $updCount</span>")
    [void]$sb.AppendLine("<span class=`"badge op-delete`">Delete $delCount</span>")
    if ($otherCount -gt 0) {
        [void]$sb.AppendLine("<span class=`"badge op-other`">Ostatni $otherCount</span>")
    }
    [void]$sb.AppendLine('</div>')

    if ($Note) {
        [void]$sb.AppendLine("<div class=`"warn`">$(ConvertTo-HtmlEncoded $Note)</div>")
    }

    if ($Objects.Count -eq 0) {
        [void]$sb.AppendLine('<p class="none">Zadne pending Add / Update / Delete objekty nebyly nalezeny.</p>')
    }

    $opClass = @{
        Add       = 'op-add'
        Update    = 'op-update'
        Delete    = 'op-delete'
        DeleteAdd = 'op-other'
    }

    foreach ($obj in $Objects) {
        $cls = $opClass[$obj.Operation]
        if (-not $cls) { $cls = 'op-other' }
        $typeLabel = if ($obj.ObjectType) { $obj.ObjectType } else { '?' }
        [void]$sb.AppendLine('<div class="obj">')
        [void]$sb.AppendLine("<h2><span class=`"badge $cls`">$(ConvertTo-HtmlEncoded $obj.Operation)</span> $(ConvertTo-HtmlEncoded $typeLabel)</h2>")
        [void]$sb.AppendLine("<div class=`"dn`">$(ConvertTo-HtmlEncoded $obj.Identifier)</div>")

        if (-not $obj.Changes -or $obj.Changes.Count -eq 0) {
            [void]$sb.AppendLine('<p class="empty">Objekt je k odstraneni / pridani bez vypsanych atributu.</p>')
            [void]$sb.AppendLine('</div>')
            continue
        }

        [void]$sb.AppendLine('<table><thead><tr><th>Atribut</th><th>Zmena</th><th>Stara hodnota</th><th>Nova hodnota</th></tr></thead><tbody>')
        foreach ($ch in $obj.Changes) {
            $old = ConvertTo-HtmlEncoded (ConvertTo-DisplayValue $ch.OldValue)
            $new = ConvertTo-HtmlEncoded (ConvertTo-DisplayValue $ch.NewValue)
            if ([string]::IsNullOrEmpty($old)) { $old = '<span class="empty">&nbsp;</span>' }
            if ([string]::IsNullOrEmpty($new)) { $new = '<span class="empty">&nbsp;</span>' }
            [void]$sb.AppendLine("<tr><td>$(ConvertTo-HtmlEncoded $ch.Attribute)</td><td>$(ConvertTo-HtmlEncoded $ch.AttrOperation)</td><td class=`"old`">$old</td><td class=`"new`">$new</td></tr>")
        }
        [void]$sb.AppendLine('</tbody></table></div>')
    }

    [void]$sb.AppendLine('</body></html>')
    $utf8 = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($Path, $sb.ToString(), $utf8)
}

function Write-PendingExportsTxt {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Title,
        [Parameter(Mandatory = $true)]$Objects,
        [string] $Note
    )

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# Pending exporty: $Title")
    [void]$sb.AppendLine("# Vygenerovano: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    [void]$sb.AppendLine("# Objektu: $($Objects.Count)")
    if ($Note) {
        [void]$sb.AppendLine("# $Note")
    }
    [void]$sb.AppendLine('# Identifikator<TAB>Typ objektu<TAB>Operace<TAB>Atribut<TAB>Stara hodnota<TAB>Nova hodnota')
    [void]$sb.AppendLine("Identifikator`tTyp objektu`tOperace`tAtribut`tStara hodnota`tNova hodnota")

    foreach ($obj in $Objects) {
        if (-not $obj.Changes -or $obj.Changes.Count -eq 0) {
            $line = @(
                (ConvertTo-DisplayValue $obj.Identifier),
                (ConvertTo-DisplayValue $obj.ObjectType),
                $obj.Operation,
                '',
                '',
                ''
            ) -join "`t"
            [void]$sb.AppendLine($line)
            continue
        }
        foreach ($ch in $obj.Changes) {
            $line = @(
                (ConvertTo-DisplayValue $obj.Identifier),
                (ConvertTo-DisplayValue $obj.ObjectType),
                $obj.Operation,
                $ch.Attribute,
                (ConvertTo-DisplayValue $ch.OldValue),
                (ConvertTo-DisplayValue $ch.NewValue)
            ) -join "`t"
            [void]$sb.AppendLine($line)
        }
    }

    $utf8 = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($Path, $sb.ToString(), $utf8)
}

function Test-XmlReaderActive {
    param([Parameter(Mandatory = $true)][System.Xml.XmlReader] $Reader)
    return $Reader.ReadState -eq [System.Xml.ReadState]::Interactive
}

function Move-XmlPastElement {
    param([Parameter(Mandatory = $true)][System.Xml.XmlReader] $Reader)
    if ($Reader.IsEmptyElement) {
        return $Reader.Read()
    }
    # Skip() uz posune reader ZA element. Dalsi Read() by se preskocil uzel.
    $Reader.Skip()
    return (Test-XmlReaderActive -Reader $Reader)
}

function Read-XmlElementText {
    param([Parameter(Mandatory = $true)][System.Xml.XmlReader] $Reader)
    return [string]$Reader.ReadElementContentAsString()
}

function Add-RemovedMemberRow {
    param(
        $State,
        [string] $GroupDn,
        [string] $ObjectTypeName,
        [string] $OperationName,
        [string] $AttributeName,
        [string] $MemberDn
    )

    $line = @(
        (ConvertTo-DisplayValue $GroupDn),
        (ConvertTo-DisplayValue $ObjectTypeName),
        (ConvertTo-DisplayValue $OperationName),
        (ConvertTo-DisplayValue $AttributeName),
        (ConvertTo-DisplayValue $MemberDn)
    ) -join "`t"
    $State.Writer.WriteLine($line)
    $State.RemovalCount++
    if (($State.RemovalCount % 5000) -eq 0) {
        $State.Writer.Flush()
        Write-Log ("Odebranych clenu zatim: {0}" -f $State.RemovalCount)
    }

    $key = if ([string]::IsNullOrEmpty($GroupDn)) { '?' } else { $GroupDn }
    if ($State.GroupCounts.ContainsKey($key)) {
        $State.GroupCounts[$key] = $State.GroupCounts[$key] + 1
    }
    else {
        $State.GroupCounts[$key] = 1
    }
}

function Read-ReferenceValueDn {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlReader] $Reader
    )

    if ($Reader.IsEmptyElement) {
        [void]$Reader.Read()
        return $null
    }

    $depth = $Reader.Depth
    $memberDn = $null
    $more = $Reader.Read()
    while ($more) {
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $Reader.Depth -eq $depth) {
            [void]$Reader.Read()
            return $memberDn
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $Reader.LocalName -eq 'dn') {
            $memberDn = Read-XmlElementText -Reader $Reader
            if (-not (Test-XmlReaderActive -Reader $Reader)) { return $memberDn }
            continue
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $more = Move-XmlPastElement -Reader $Reader
            continue
        }
        $more = $Reader.Read()
    }
    return $memberDn
}

function Read-MemberAttributeValues {
    param(
        $State,
        [Parameter(Mandatory = $true)][System.Xml.XmlReader] $Reader,
        [string] $AttributeName,
        [string] $AttributeOperation
    )

    $sawDelete = $false
    if ($Reader.IsEmptyElement) {
        if ($AttributeOperation -eq 'delete') {
            Add-MemberAttributeDeleteWarning -State $State -AttributeName $AttributeName
        }
        return $Reader.Read()
    }

    $depth = $Reader.Depth
    $more = $Reader.Read()
    while ($more) {
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $Reader.Depth -eq $depth) {
            if ($AttributeOperation -eq 'delete' -and -not $sawDelete) {
                Add-MemberAttributeDeleteWarning -State $State -AttributeName $AttributeName
            }
            return $Reader.Read()
        }

        $isValue = $Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and (
            $Reader.LocalName -eq 'dn-value' -or $Reader.LocalName -eq 'value')
        if ($isValue) {
            $valueOp = $Reader.GetAttribute('operation')
            if ([string]::IsNullOrWhiteSpace($valueOp)) { $valueOp = $null }
            else { $valueOp = $valueOp.Trim().ToLowerInvariant() }
            if (-not $valueOp -and $AttributeOperation -eq 'delete') { $valueOp = 'delete' }

            $wanted = ($valueOp -eq $State.ValueOperation)

            if ($Reader.LocalName -eq 'dn-value') {
                if ($wanted) {
                    $memberDn = Read-ReferenceValueDn -Reader $Reader
                    $sawDelete = $true
                    if (-not [string]::IsNullOrWhiteSpace($memberDn)) {
                        Add-RemovedMemberRow -State $State -GroupDn $State.CurrentDn -ObjectTypeName $State.CurrentType `
                            -OperationName $State.CurrentOperation -AttributeName $AttributeName -MemberDn $memberDn
                    }
                    $more = Test-XmlReaderActive -Reader $Reader
                    continue
                }
                $more = Move-XmlPastElement -Reader $Reader
                continue
            }

            if ($wanted) {
                $memberDn = Read-XmlElementText -Reader $Reader
                $sawDelete = $true
                if (-not [string]::IsNullOrWhiteSpace($memberDn)) {
                    Add-RemovedMemberRow -State $State -GroupDn $State.CurrentDn -ObjectTypeName $State.CurrentType `
                        -OperationName $State.CurrentOperation -AttributeName $AttributeName -MemberDn $memberDn
                }
                $more = Test-XmlReaderActive -Reader $Reader
                continue
            }
            $more = Move-XmlPastElement -Reader $Reader
            continue
        }

        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $more = Move-XmlPastElement -Reader $Reader
            continue
        }
        $more = $Reader.Read()
    }

    if ($AttributeOperation -eq 'delete' -and -not $sawDelete) {
        Add-MemberAttributeDeleteWarning -State $State -AttributeName $AttributeName
    }
    return $false
}

function Add-MemberAttributeDeleteWarning {
    param($State, [string] $AttributeName)
    if ($State.AttributeDeleteWarnings.Count -ge 30) {
        $State.AttributeDeleteWarningsTruncated = $true
        return
    }
    $dn = $State.CurrentDn
    $State.AttributeDeleteWarnings.Add(
        ("{0}: atribut {1} ma operation=delete bez vypsanych hodnot. Jednotlivi cleni jsou jen v hologramu a do reportu se nedostanou." -f $dn, $AttributeName)
    ) | Out-Null
}

function Read-ExportDelta {
    param(
        $State,
        [Parameter(Mandatory = $true)][System.Xml.XmlReader] $Reader
    )

    $opRaw = $Reader.GetAttribute('operation')
    $State.CurrentOperation = ConvertTo-NormalizedOperation -Raw $opRaw
    $deltaDn = $Reader.GetAttribute('dn')
    if ([string]::IsNullOrWhiteSpace($State.CurrentDn) -and -not [string]::IsNullOrWhiteSpace($deltaDn)) {
        $State.CurrentDn = $deltaDn
    }

    if ($Reader.IsEmptyElement) {
        return $Reader.Read()
    }

    $depth = $Reader.Depth
    $more = $Reader.Read()
    while ($more) {
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $Reader.Depth -eq $depth) {
            return $Reader.Read()
        }
        $isAttr = $Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and (
            $Reader.LocalName -eq 'attr' -or $Reader.LocalName -eq 'dn-attr')
        if ($isAttr) {
            $attrName = $Reader.GetAttribute('name')
            if (-not [string]::IsNullOrWhiteSpace($attrName) -and $State.Attributes.Contains($attrName)) {
                $attrOp = $Reader.GetAttribute('operation')
                if (-not [string]::IsNullOrWhiteSpace($attrOp)) { $attrOp = $attrOp.Trim().ToLowerInvariant() }
                else { $attrOp = $null }
                $more = Read-MemberAttributeValues -State $State -Reader $Reader -AttributeName $attrName -AttributeOperation $attrOp
                continue
            }
            $more = Move-XmlPastElement -Reader $Reader
            continue
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $more = Move-XmlPastElement -Reader $Reader
            continue
        }
        $more = $Reader.Read()
    }
    return $false
}

function Read-UnappliedExportSection {
    param(
        $State,
        [Parameter(Mandatory = $true)][System.Xml.XmlReader] $Reader
    )

    if ($Reader.IsEmptyElement) {
        return $Reader.Read()
    }

    $depth = $Reader.Depth
    $more = $Reader.Read()
    while ($more) {
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $Reader.Depth -eq $depth) {
            return $Reader.Read()
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $Reader.LocalName -eq 'delta') {
            $more = Read-ExportDelta -State $State -Reader $Reader
            continue
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $more = Move-XmlPastElement -Reader $Reader
            continue
        }
        $more = $Reader.Read()
    }
    return $false
}

function Read-CsObjectMemberRemovals {
    param(
        $State,
        [Parameter(Mandatory = $true)][System.Xml.XmlReader] $Reader
    )

    $State.CurrentDn = $Reader.GetAttribute('cs-dn')
    $State.CurrentType = $Reader.GetAttribute('object-type')
    $State.CurrentOperation = $null

    $typeOk = [string]::IsNullOrWhiteSpace($State.ObjectType) -or
        [string]::Equals($State.CurrentType, $State.ObjectType, [StringComparison]::OrdinalIgnoreCase)
    if (-not $typeOk -or $Reader.IsEmptyElement) {
        if ($typeOk) { $State.MatchingTypeCount++ }
        return (Move-XmlPastElement -Reader $Reader)
    }

    $State.MatchingTypeCount++
    $depth = $Reader.Depth
    $more = $Reader.Read()
    while ($more) {
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $Reader.Depth -eq $depth) {
            return $Reader.Read()
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $Reader.LocalName -eq 'unapplied-export') {
            $more = Read-UnappliedExportSection -State $State -Reader $Reader
            continue
        }
        # hologram, unconfirmed-export, pending-import a zbytek objektu se nečte do paměti
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $more = Move-XmlPastElement -Reader $Reader
            continue
        }
        $more = $Reader.Read()
    }
    return $false
}

function Write-RemovedMembersSummaryHtml {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Title,
        $State,
        [string] $Note,
        [string] $DetailPath
    )

    $generated = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $rows = @(
        foreach ($kv in $State.GroupCounts.GetEnumerator()) {
            [pscustomobject]@{ Group = [string]$kv.Key; Count = [int]$kv.Value }
        }
    )
    if ($rows.Count -gt 1) {
        $rows = @($rows | Sort-Object @{ Expression = 'Count'; Descending = $true }, @{ Expression = 'Group'; Descending = $false })
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<!DOCTYPE html>')
    [void]$sb.AppendLine('<html lang="cs"><head><meta charset="utf-8" />')
    [void]$sb.AppendLine("<title>$(ConvertTo-HtmlEncoded $Title) - odebrani clenove</title>")
    [void]$sb.AppendLine(@'
<style>
  body { font-family: Segoe UI, Tahoma, Arial, sans-serif; margin: 24px; color: #222; }
  h1 { font-size: 22px; margin-bottom: 4px; }
  .meta { color: #555; margin-bottom: 16px; }
  .summary { margin: 0 0 20px 0; }
  .summary span { display: inline-block; margin-right: 14px; padding: 3px 8px; border-radius: 4px; background: #c62828; color: #fff; font-weight: 600; }
  table { border-collapse: collapse; width: 100%; margin-top: 8px; }
  th, td { border: 1px solid #cfcfcf; padding: 6px 8px; vertical-align: top; text-align: left; }
  th { background: #f3f3f3; }
  td.dn { font-family: Consolas, monospace; font-size: 13px; word-break: break-all; }
  td.num { text-align: right; }
  .warn { background: #fff8e1; border: 1px solid #f0c36d; padding: 10px 12px; margin: 12px 0 20px 0; }
  .none { color: #666; }
</style>
</head><body>
'@)
    [void]$sb.AppendLine("<h1>Odebrani clenove: $(ConvertTo-HtmlEncoded $Title)</h1>")
    [void]$sb.AppendLine("<div class=`"meta`">Vygenerovano $generated</div>")
    [void]$sb.AppendLine("<div class=`"summary`"><span>Skupin $($rows.Count)</span><span>Odebranych clenu $($State.RemovalCount)</span></div>")
    if ($Note) {
        [void]$sb.AppendLine("<div class=`"warn`">$(ConvertTo-HtmlEncoded $Note)</div>")
    }
    if ($DetailPath) {
        [void]$sb.AppendLine("<p>Kazdy odebrany clen je v souboru <span class=`"dn`">$(ConvertTo-HtmlEncoded $DetailPath)</span>.</p>")
    }
    foreach ($warning in @($State.AttributeDeleteWarnings)) {
        [void]$sb.AppendLine("<div class=`"warn`">$(ConvertTo-HtmlEncoded $warning)</div>")
    }
    if ($State.AttributeDeleteWarningsTruncated) {
        [void]$sb.AppendLine('<div class="warn">Dalsi skupiny se stejnym problemem byly zkraceny.</div>')
    }

    if ($rows.Count -eq 0) {
        [void]$sb.AppendLine('<p class="none">Zadne odebrane hodnoty atributu nebyly v unapplied-export nalezeny.</p>')
    }
    else {
        [void]$sb.AppendLine('<table><thead><tr><th>Skupina</th><th>Pocet odebranych clenu</th></tr></thead><tbody>')
        foreach ($row in $rows) {
            [void]$sb.AppendLine("<tr><td class=`"dn`">$(ConvertTo-HtmlEncoded $row.Group)</td><td class=`"num`">$($row.Count)</td></tr>")
        }
        [void]$sb.AppendLine('</tbody></table>')
    }
    [void]$sb.AppendLine('</body></html>')
    $utf8 = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($Path, $sb.ToString(), $utf8)
}

function Export-RemovedGroupMembers {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $TxtPath,
        [Parameter(Mandatory = $true)][string] $HtmlPath,
        [Parameter(Mandatory = $true)][string] $Title,
        [string] $ObjectTypeFilter,
        [Parameter(Mandatory = $true)][string[]] $AttributeNames
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "XML soubor neexistuje: $Path"
    }

    $attrSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $AttributeNames) {
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            [void]$attrSet.Add($name.Trim())
        }
    }
    if ($attrSet.Count -eq 0) {
        throw 'Neni zadany zadny atribut.'
    }

    $utf8 = New-Object System.Text.UTF8Encoding $true
    $writer = New-Object System.IO.StreamWriter($TxtPath, $false, $utf8)
    $state = [pscustomobject]@{
        Writer                         = $writer
        ObjectType                     = $ObjectTypeFilter
        Attributes                     = $attrSet
        ValueOperation                 = 'delete'
        TotalCsObjects                 = 0
        MatchingTypeCount              = 0
        RemovalCount                   = 0
        GroupCounts                    = (New-Object 'System.Collections.Generic.Dictionary[string,int]')
        AttributeDeleteWarnings        = (New-Object System.Collections.Generic.List[string])
        AttributeDeleteWarningsTruncated = $false
        CurrentDn                      = $null
        CurrentType                    = $null
        CurrentOperation               = $null
    }

    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.IgnoreWhitespace = $true
    $settings.IgnoreComments = $true
    $settings.XmlResolver = $null

    $stream = $null
    $reader = $null
    try {
        $writer.WriteLine("# Odebrani clenove skupin: $Title")
        $writer.WriteLine("# Vygenerovano: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
        $writer.WriteLine("# Filtr: object-type=$ObjectTypeFilter; atributy=$($AttributeNames -join ', '); jen operation=delete v unapplied-export")
        $writer.WriteLine('# Hologram (soucasni clenove) se do reportu nezapisuje.')
        $writer.WriteLine("Skupina`tTyp objektu`tOperace`tAtribut`tOdebrany clen")

        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $reader = [System.Xml.XmlReader]::Create($stream, $settings)

        $more = $reader.Read()
        while ($more) {
            if ($reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $reader.LocalName -eq 'cs-object') {
                $state.TotalCsObjects++
                if (($state.TotalCsObjects % 5000) -eq 0) {
                    Write-Log ("Zpracovano cs-object: {0}, odebranych clenu: {1}" -f $state.TotalCsObjects, $state.RemovalCount)
                }
                $more = Read-CsObjectMemberRemovals -State $state -Reader $reader
                continue
            }
            $more = $reader.Read()
        }
    }
    finally {
        if ($reader) { $reader.Close() }
        if ($stream) { $stream.Close() }
        $writer.Flush()
        $writer.Close()
    }

    $note = "csexport /f:x nelze omezit na atribut. Docasne XML obsahuje i hologram clenu; ten se pri cteni preskakuje. Report ma jen odebrane hodnoty ($($AttributeNames -join ', ')) z unapplied-export."
    Write-RemovedMembersSummaryHtml -Path $HtmlPath -Title $Title -State $state -Note $note -DetailPath $TxtPath

    return $state
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

$outputDirectory = Join-Path $PSScriptRoot 'Reports'
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$safeMa = ConvertTo-SafeFileName -Name $ManagementAgent
if (-not [string]::IsNullOrWhiteSpace($XmlPath)) {
    if (-not (Test-Path -LiteralPath $XmlPath)) {
        throw "XML soubor neexistuje: $XmlPath"
    }
    $xmlPath = $XmlPath
    Write-Log "Pouzivam existujici XML: $xmlPath"
}
else {
    Get-ChildItem -LiteralPath $outputDirectory -Filter ("{0}_pending_*.xml" -f $safeMa) -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Log "Mazu predchozi XML: $($_.FullName)"
            Remove-Item -LiteralPath $_.FullName -Force
        }
    $xmlPath = Join-Path $outputDirectory ("{0}_pending_{1}.xml" -f $safeMa, $stamp)
    $exe = Resolve-CsExportExe
    Invoke-CsExport -MaName $ManagementAgent -OutputXml $xmlPath -ExePath $exe
}

if ($RemovedGroupMembers) {
    if ([string]::IsNullOrWhiteSpace($ObjectType)) { $ObjectType = 'group' }
    if (-not $Attribute -or $Attribute.Count -eq 0) { $Attribute = @('member') }

    $title = "{0} / {1} / {2}" -f $ManagementAgent, $ObjectType, ($Attribute -join ', ')
    $safeType = ConvertTo-SafeFileName -Name $ObjectType
    $txtPath = Join-Path $outputDirectory ("{0}_{1}_member_delete_{2}.txt" -f $safeMa, $safeType, $stamp)
    $htmlPath = Join-Path $outputDirectory ("{0}_{1}_member_delete_{2}.html" -f $safeMa, $safeType, $stamp)

    Write-Log "csexport /f:x nelze omezit na atribut ani typ objektu. Hologram clenu se pri cteni preskakuje, report obsahuje jen odebrane hodnoty."
    Write-Log ("Filtr: object-type={0}, atribut={1}, operation=delete" -f $ObjectType, ($Attribute -join ', '))
    Write-Log "Ctu odebrane cleny ..."

    $memberState = Export-RemovedGroupMembers -Path $xmlPath -TxtPath $txtPath -HtmlPath $htmlPath -Title $title `
        -ObjectTypeFilter $ObjectType -AttributeNames $Attribute

    foreach ($warning in @($memberState.AttributeDeleteWarnings)) {
        Write-Log $warning -Level WARN
    }
    if ($memberState.AttributeDeleteWarningsTruncated) {
        Write-Log "Dalsi varovani o operation=delete bez hodnot byla zkracena." -Level WARN
    }

    Write-Log ("cs-object: {0}, typu {1}: {2}, skupin s odebranim: {3}, odebranych clenu: {4}" -f `
        $memberState.TotalCsObjects, $ObjectType, $memberState.MatchingTypeCount, $memberState.GroupCounts.Count, $memberState.RemovalCount)
    Write-Log "HTML: $htmlPath"
    Write-Log "TXT : $txtPath"
    Write-Log "XML ponechano: $xmlPath"
    Write-Log "Hotovo."
    return
}

Write-Log "Parsuji pending exporty ..."
$parseResult = Read-PendingExportsFromXml -Path $xmlPath
$pending = @($parseResult.Objects | Sort-Object Operation, ObjectType, Identifier)

$note = $null
if ($parseResult.TotalCsObjects -gt 0 -and $parseResult.PendingCsObjects -eq 0) {
    $note = "XML obsahuje $($parseResult.TotalCsObjects) objektu connector space, ale zadny nema sekci unapplied-export (delta Add/Update/Delete)."
    Write-Log $note -Level WARN
}
else {
    Write-Log ("V XML je {0} cs-object, z toho {1} s unapplied-export." -f $parseResult.TotalCsObjects, $parseResult.PendingCsObjects)
}

$skipNote = "Vynechane atributy: " + ($script:ExcludeAttributes -join ', ')
Write-Log $skipNote
if ($note) { $note = "$note $skipNote" } else { $note = $skipNote }

$groups = @($pending | Group-Object -Property {
        if ([string]::IsNullOrWhiteSpace($_.ObjectType)) { 'unknown' } else { $_.ObjectType }
    } | Sort-Object Name)

if ($groups.Count -eq 0) {
    Write-Log "Zadne pending zmeny k reportu." -Level WARN
}
else {
    foreach ($group in $groups) {
        $typeName = $group.Name
        $items = @($group.Group | Sort-Object Operation, Identifier)
        $safeType = ConvertTo-SafeFileName -Name $typeName
        $title = "{0} / {1}" -f $ManagementAgent, $typeName
        $htmlPath = Join-Path $outputDirectory ("{0}_{1}_pending_{2}.html" -f $safeMa, $safeType, $stamp)
        $txtPath  = Join-Path $outputDirectory ("{0}_{1}_pending_{2}.txt" -f $safeMa, $safeType, $stamp)

        Write-PendingExportsHtml -Path $htmlPath -Title $title -Objects $items -Note $note
        Write-PendingExportsTxt  -Path $txtPath  -Title $title -Objects $items -Note $note

        Write-Log ("Typ {0}: {1} objektu (Add={2}, Update={3}, Delete={4})" -f `
            $typeName, `
            $items.Count, `
            @($items | Where-Object { $_.Operation -eq 'Add' }).Count, `
            @($items | Where-Object { $_.Operation -eq 'Update' }).Count, `
            @($items | Where-Object { $_.Operation -eq 'Delete' }).Count)
        Write-Log "HTML: $htmlPath"
        Write-Log "TXT : $txtPath"
    }
}

Write-Log "XML ponechano: $xmlPath"
Write-Log ("Hotovo. Celkem objektu v reportech: {0}" -f $pending.Count