<#
.SYNOPSIS
    Report zmen z MMSML exportu (exported_Changes.xml, step-type=export).

.PARAMETER Path
    Cesta k XML se strukturou mmsml / directory-entries / delta.

.EXAMPLE
    .\Export-MmsmlChanges.ps1 -Path .\exported_Changes.xml
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Log {
    param([string] $Message)
    Write-Host ("{0:yyyy-MM-dd HH:mm:ss} [INFO] {1}" -f (Get-Date), $Message) -ForegroundColor Cyan
}

function Test-XmlReaderActive {
    param($Reader)
    return $Reader.ReadState -eq [System.Xml.ReadState]::Interactive
}

function Move-XmlPastElement {
    param($Reader)
    if ($Reader.IsEmptyElement) { return $Reader.Read() }
    $Reader.Skip()
    return (Test-XmlReaderActive $Reader)
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
        try { $ticks = [Convert]::ToInt64($hex, 16) } catch { return $Text }
    }
    else { return $Text }
    if ($ticks -le 0 -or $ticks -eq [int64]::MaxValue) { return 'nikdy' }
    try { return [DateTime]::FromFileTime($ticks).ToString('yyyy-MM-dd HH:mm:ss') } catch { return $Text }
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
        try { $flags = [Convert]::ToInt64($hex, 16) } catch { return $Text }
    }
    else { return $Text }
    $state = if (($flags -band 2) -eq 2) { 'neaktivni' } else { 'aktivni' }
    return "$raw ($state)"
}

function ConvertTo-AttributeDisplay {
    param([string] $Attribute, [string] $Text)
    if ([string]::Equals($Attribute, 'accountExpires', [StringComparison]::OrdinalIgnoreCase)) {
        return ConvertTo-AccountExpiresDisplay $Text
    }
    if ([string]::Equals($Attribute, 'userAccountControl', [StringComparison]::OrdinalIgnoreCase)) {
        return ConvertTo-UserAccountControlDisplay $Text
    }
    return $Text
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
        default      { $Raw }
    }
}

function Read-DnValueText {
    param($Reader)
    if ($Reader.IsEmptyElement) { [void]$Reader.Read(); return $null }
    $depth = $Reader.Depth
    $dn = $null
    $more = $Reader.Read()
    while ($more) {
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $Reader.Depth -eq $depth) {
            [void]$Reader.Read()
            return $dn
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $Reader.LocalName -eq 'dn') {
            $dn = [string]$Reader.ReadElementContentAsString()
            if (-not (Test-XmlReaderActive $Reader)) { return $dn }
            continue
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $more = Move-XmlPastElement $Reader
            continue
        }
        $more = $Reader.Read()
    }
    return $dn
}

function Read-AttributeChangeValues {
    param($Reader)
    $list = New-Object System.Collections.Generic.List[object]
    if ($Reader.IsEmptyElement) {
        [void]$Reader.Read()
        return $list
    }
    $depth = $Reader.Depth
    $more = $Reader.Read()
    while ($more) {
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $Reader.Depth -eq $depth) {
            [void]$Reader.Read()
            return $list
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and ($Reader.LocalName -eq 'value' -or $Reader.LocalName -eq 'dn-value')) {
            $op = $Reader.GetAttribute('operation')
            if (-not [string]::IsNullOrWhiteSpace($op)) { $op = $op.Trim().ToLowerInvariant() } else { $op = $null }
            if ($Reader.LocalName -eq 'dn-value') {
                $text = Read-DnValueText $Reader
            }
            else {
                $text = [string]$Reader.ReadElementContentAsString()
            }
            $list.Add([pscustomobject]@{ Operation = $op; Value = $text })
            $more = Test-XmlReaderActive $Reader
            continue
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $more = Move-XmlPastElement $Reader
            continue
        }
        $more = $Reader.Read()
    }
    return $list
}

function Add-Change {
    param($Changes, [string] $Attribute, [string] $AttrOperation, [string] $OldValue, [string] $NewValue)
    $Changes.Add([pscustomobject]@{
        Attribute     = $Attribute
        AttrOperation = $AttrOperation
        OldValue      = ConvertTo-AttributeDisplay $Attribute $OldValue
        NewValue      = ConvertTo-AttributeDisplay $Attribute $NewValue
    })
}

function Read-ExportDelta {
    param($Reader)
    $rawOp = $Reader.GetAttribute('operation')
    $dn = $Reader.GetAttribute('dn')
    $newDn = $Reader.GetAttribute('newdn')
    $operation = ConvertTo-NormalizedOperation $rawOp
    $objectClass = $null
    $changes = New-Object System.Collections.Generic.List[object]

    if (-not [string]::IsNullOrWhiteSpace($newDn) -and $newDn -ne $dn) {
        Add-Change $changes 'DN' 'replace' $dn $newDn
    }
    if ($Reader.IsEmptyElement) {
        [void]$Reader.Read()
        return [pscustomobject]@{ Dn = $dn; Operation = $operation; ObjectClass = $objectClass; Changes = $changes }
    }

    $depth = $Reader.Depth
    $more = $Reader.Read()
    while ($more) {
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $Reader.Depth -eq $depth) {
            [void]$Reader.Read()
            break
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $Reader.LocalName -eq 'primary-objectclass') {
            $objectClass = [string]$Reader.ReadElementContentAsString()
            $more = Test-XmlReaderActive $Reader
            continue
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and ($Reader.LocalName -eq 'attr' -or $Reader.LocalName -eq 'dn-attr')) {
            $attrName = $Reader.GetAttribute('name')
            $attrOp = $Reader.GetAttribute('operation')
            if (-not [string]::IsNullOrWhiteSpace($attrOp)) { $attrOp = $attrOp.Trim().ToLowerInvariant() } else { $attrOp = $null }
            $multi = ($Reader.GetAttribute('multivalued') -eq 'true')
            $values = @(Read-AttributeChangeValues $Reader)
            $added = @($values | Where-Object { $_.Operation -eq 'add' } | ForEach-Object { $_.Value })
            $removed = @($values | Where-Object { $_.Operation -eq 'delete' } | ForEach-Object { $_.Value })
            $plain = @($values | Where-Object { -not $_.Operation } | ForEach-Object { $_.Value })
            if ($attrOp -eq 'delete' -and $added.Count -eq 0 -and $removed.Count -eq 0 -and $plain.Count -eq 0) {
                Add-Change $changes $attrName 'delete' '(cely atribut, export neuvadi jednotlive hodnoty)' $null
            }
            elseif (-not $multi -and (($added.Count + $plain.Count) -gt 0) -and $removed.Count -gt 0) {
                $newValue = $(if ($added.Count) { $added } else { $plain }) -join '; '
                Add-Change $changes $attrName 'update' ($removed -join '; ') $newValue
            }
            else {
                if ($attrOp -eq 'delete' -and $added.Count -eq 0 -and $removed.Count -eq 0) { $removed = $plain; $plain = @() }
                foreach ($v in $removed) { Add-Change $changes $attrName 'delete' $v $null }
                foreach ($v in $added) { Add-Change $changes $attrName 'add' $null $v }
                foreach ($v in $plain) { Add-Change $changes $attrName 'add' $null $v }
            }
            $more = Test-XmlReaderActive $Reader
            continue
        }
        if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $more = Move-XmlPastElement $Reader
            continue
        }
        $more = $Reader.Read()
    }

    return [pscustomobject]@{
        Dn          = $dn
        Operation   = $operation
        ObjectClass = $objectClass
        Changes     = $changes
    }
}

function Read-MmsmlExport {
    param([string] $XmlPath)
    if (-not (Test-Path -LiteralPath $XmlPath)) { throw "XML soubor neexistuje: $XmlPath" }
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.IgnoreWhitespace = $true
    $settings.IgnoreComments = $true
    $settings.XmlResolver = $null
    $objects = New-Object System.Collections.Generic.List[object]
    # Soubor ma v deklaraci UTF-16, obsah je ale UTF-8 bez BOM. TextReader
    # dekodovani vyridi a XmlReader deklaraci encoding uz neprepina.
    $stream = [System.IO.File]::Open($XmlPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $text = New-Object System.IO.StreamReader($stream, [System.Text.UTF8Encoding]::new($false), $true)
    $reader = [System.Xml.XmlReader]::Create($text, $settings)
    try {
        $more = $reader.Read()
        while ($more) {
            if ($reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $reader.LocalName -eq 'delta') {
                $objects.Add((Read-ExportDelta $reader))
                $more = Test-XmlReaderActive $reader
                continue
            }
            $more = $reader.Read()
        }
    }
    finally {
        $reader.Close()
        $text.Close()
        $stream.Close()
    }
    return $objects
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

function Write-ChangeReport {
    param($Objects, [string] $HtmlPath, [string] $TxtPath, [string] $SourcePath)
    $addCount = @($Objects | Where-Object { $_.Operation -eq 'Add' }).Count
    $updCount = @($Objects | Where-Object { $_.Operation -eq 'Update' }).Count
    $delCount = @($Objects | Where-Object { $_.Operation -eq 'Delete' }).Count
    $generated = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

    $utf8 = New-Object System.Text.UTF8Encoding $true
    $txt = New-Object System.IO.StreamWriter($TxtPath, $false, $utf8)
    $html = New-Object System.Text.StringBuilder
    try {
        $txt.WriteLine("# Zmeny exportu: $SourcePath")
        $txt.WriteLine("# Vygenerovano: $generated")
        $txt.WriteLine("# Objektu: $($Objects.Count)  Add=$addCount  Update=$updCount  Delete=$delCount")
        $txt.WriteLine("Identifikator`tTyp objektu`tOperace`tAtribut`tZmena`tStara hodnota`tNova hodnota")

        [void]$html.AppendLine('<!DOCTYPE html><html lang="cs"><head><meta charset="utf-8" />')
        [void]$html.AppendLine('<title>Zmeny exportu</title>')
        [void]$html.AppendLine(@'
<style>
 body { font-family: Segoe UI, Tahoma, Arial, sans-serif; margin: 24px; color: #222; }
 h1 { font-size: 22px; } .meta { color: #555; margin-bottom: 16px; }
 .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; color: #fff; font-size: 12px; font-weight: 600; }
 .op-add { background: #2e7d32; } .op-update { background: #ef6c00; } .op-delete { background: #c62828; } .op-other { background: #546e7a; }
 .obj { margin: 22px 0; } .dn { font-family: Consolas, monospace; font-size: 13px; word-break: break-all; }
 table { border-collapse: collapse; width: 100%; margin-top: 8px; }
 th, td { border: 1px solid #cfcfcf; padding: 6px 8px; vertical-align: top; text-align: left; }
 th { background: #f3f3f3; } td.old { background: #fff3f3; } td.new { background: #f1fff3; }
</style></head><body>
'@)
        [void]$html.AppendLine('<h1>Zmeny exportu</h1>')
        [void]$html.AppendLine("<div class=`"meta`">$(ConvertTo-HtmlEncoded $SourcePath)<br>Vygenerovano $generated &nbsp;|&nbsp; objektu $($Objects.Count)</div>")
        [void]$html.AppendLine("<p><span class=`"badge op-add`">Add $addCount</span> <span class=`"badge op-update`">Update $updCount</span> <span class=`"badge op-delete`">Delete $delCount</span></p>")

        $opClass = @{ Add = 'op-add'; Update = 'op-update'; Delete = 'op-delete'; DeleteAdd = 'op-other' }
        foreach ($obj in $Objects) {
            $cls = $opClass[$obj.Operation]
            if (-not $cls) { $cls = 'op-other' }
            $typeLabel = if ($obj.ObjectClass) { $obj.ObjectClass } else { '' }
            [void]$html.AppendLine('<div class="obj">')
            [void]$html.AppendLine("<h2><span class=`"badge $cls`">$(ConvertTo-HtmlEncoded $obj.Operation)</span> $(ConvertTo-HtmlEncoded $typeLabel)</h2>")
            [void]$html.AppendLine("<div class=`"dn`">$(ConvertTo-HtmlEncoded $obj.Dn)</div>")
            if (-not $obj.Changes -or $obj.Changes.Count -eq 0) {
                [void]$html.AppendLine('<p>Bez vypsanych atributu.</p></div>')
                $txt.WriteLine(("{0}`t{1}`t{2}`t`t`t`t" -f (ConvertTo-DisplayValue $obj.Dn), (ConvertTo-DisplayValue $typeLabel), $obj.Operation))
                continue
            }
            [void]$html.AppendLine('<table><thead><tr><th>Atribut</th><th>Zmena</th><th>Stara hodnota</th><th>Nova hodnota</th></tr></thead><tbody>')
            foreach ($ch in $obj.Changes) {
                $old = ConvertTo-HtmlEncoded (ConvertTo-DisplayValue $ch.OldValue)
                $new = ConvertTo-HtmlEncoded (ConvertTo-DisplayValue $ch.NewValue)
                [void]$html.AppendLine("<tr><td>$(ConvertTo-HtmlEncoded $ch.Attribute)</td><td>$(ConvertTo-HtmlEncoded $ch.AttrOperation)</td><td class=`"old`">$old</td><td class=`"new`">$new</td></tr>")
                $txt.WriteLine(("{0}`t{1}`t{2}`t{3}`t{4}`t{5}`t{6}" -f `
                    (ConvertTo-DisplayValue $obj.Dn), (ConvertTo-DisplayValue $typeLabel), $obj.Operation, `
                    $ch.Attribute, $ch.AttrOperation, (ConvertTo-DisplayValue $ch.OldValue), (ConvertTo-DisplayValue $ch.NewValue)))
            }
            [void]$html.AppendLine('</tbody></table></div>')
        }
        [void]$html.AppendLine('</body></html>')
    }
    finally {
        $txt.Flush(); $txt.Close()
    }
    [System.IO.File]::WriteAllText($HtmlPath, $html.ToString(), $utf8)
}

if (-not (Test-Path -LiteralPath $Path)) { throw "XML soubor neexistuje: $Path" }
$fullPath = (Resolve-Path -LiteralPath $Path).Path
Write-Log "Ctu $fullPath"
$objects = @(Read-MmsmlExport -XmlPath $fullPath | Sort-Object Operation, ObjectClass, Dn)

$outDir = Join-Path $PSScriptRoot 'Reports'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$htmlPath = Join-Path $outDir ("exported_Changes_{0}.html" -f $stamp)
$txtPath = Join-Path $outDir ("exported_Changes_{0}.txt" -f $stamp)
Write-ChangeReport -Objects $objects -HtmlPath $htmlPath -TxtPath $txtPath -SourcePath $fullPath

$changeCount = 0
foreach ($o in $objects) { if ($o.Changes) { $changeCount += $o.Changes.Count } }
Write-Log ("Objektu: {0} (Add={1}, Update={2}, Delete={3}), radku zmen: {4}" -f `
    $objects.Count, `
    @($objects | Where-Object Operation -eq 'Add').Count, `
    @($objects | Where-Object Operation -eq 'Update').Count, `
    @($objects | Where-Object Operation -eq 'Delete').Count, `
    $changeCount)
Write-Log "HTML: $htmlPath"
Write-Log "TXT : $txtPath"
