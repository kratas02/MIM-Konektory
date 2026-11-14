<#
.SYNOPSIS
    Z csexport XML (/f:e nebo /f:i) vypise typy chyb a pod kazdym typem seznam anchoru (cs-dn).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $XmlPath,

    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $XmlPath)) {
    throw "XML soubor neexistuje: $XmlPath"
}
$XmlPath = (Resolve-Path -LiteralPath $XmlPath).Path

if (-not $OutputPath) {
    $dir = Join-Path $PSScriptRoot 'Reports'
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $base = [System.IO.Path]::GetFileNameWithoutExtension($XmlPath)
    $OutputPath = Join-Path $dir ("{0}_by_type.txt" -f $base)
}

function Get-XmlText {
    param($Node, [string] $LocalName)
    if (-not $Node) { return $null }
    $n = $Node.SelectSingleNode('.//*[local-name()="' + $LocalName + '"]')
    if ($n) { return $n.InnerText.Trim() }
    return $null
}

function Get-AttrValue {
    param($CsObject, [string] $AttrName)
    $attr = $CsObject.SelectSingleNode('.//*[local-name()="attr"][@name="' + $AttrName + '"]/*[local-name()="value"]')
    if ($attr) { return $attr.InnerText.Trim() }
    return $null
}

function Get-ExportErrorType {
    param($CsObject)
    $literal = Get-XmlText $CsObject 'error-literal'
    if (-not $literal) { return '(bez detailu chyby)' }

    if ($literal -match 'Violation:\s*') {
        $parts = [regex]::Matches($literal, 'Violation:\s*([^\r\n"]+)') | ForEach-Object { $_.Groups[1].Value.Trim() }
        if ($parts.Count -gt 0) {
            $norm = foreach ($p in $parts) {
                if ($p -match '^(\S+)\s+(\S+)') { '{0} {1}' -f $Matches[1], $Matches[2] }
                else { $p }
            }
            return (($norm | Select-Object -Unique) -join ' + ')
        }
    }

    if ($literal -match '"code"\s*:\s*"([^"]+)"') {
        $code = $Matches[1]
        if ($literal -match '"message"\s*:\s*"([^"]+)"') {
            $msg = ($Matches[1] -replace '\\r\\n', ' / ' -replace '\s+', ' ').Trim()
            if ($msg.Length -gt 160) { $msg = $msg.Substring(0, 160) + '...' }
            return "$code / $msg"
        }
        return $code
    }

    $clip = ($literal -replace '\s+', ' ').Trim()
    if ($clip.Length -gt 180) { $clip = $clip.Substring(0, 180) + '...' }
    return $clip
}

function Get-ImportErrorType {
    param($CsObject)
    $detail = $CsObject.SelectSingleNode('.//*[local-name()="import-errordetail"]')
    if (-not $detail) { return '(bez import-errordetail)' }

    $errType = $detail.GetAttribute('error-type')
    if ([string]::IsNullOrWhiteSpace($errType)) { $errType = '(bez error-type)' }

    $text = $detail.InnerText
    if ($text -match 'String was not recognized as a valid DateTime') {
        return "$errType / FormatException DateTime.ParseExact (MVExtension.cs:668)"
    }
    if ($errType -eq 'mv-constraint-violation') {
        $src = Get-XmlText $detail 'src-attribute'
        $dst = $null
        $map = $detail.SelectSingleNode('.//*[local-name()="attribute-mapping"]')
        if ($map) { $dst = $map.GetAttribute('dest-attr') }
        if ($src -or $dst) { return "$errType / $src -> $dst" }
    }
    return $errType
}

$groups = [ordered]@{}
$objectCount = 0

$settings = New-Object System.Xml.XmlReaderSettings
$settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
$settings.IgnoreWhitespace = $true
$settings.XmlResolver = $null
$stream = [System.IO.File]::Open($XmlPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
$reader = [System.Xml.XmlReader]::Create($stream, $settings)
try {
    $keepReading = $reader.Read()
    while ($keepReading) {
        if ($reader.NodeType -ne [System.Xml.XmlNodeType]::Element -or $reader.LocalName -ne 'cs-object') {
            $keepReading = $reader.Read()
            continue
        }

        $objectCount++
        $outer = $reader.ReadOuterXml()
        $keepReading = ($reader.NodeType -ne [System.Xml.XmlNodeType]::None)

        $doc = New-Object System.Xml.XmlDocument
        $doc.XmlResolver = $null
        $doc.LoadXml($outer)
        $cs = $doc.DocumentElement

        $hasExport = $null -ne $cs.SelectSingleNode('.//*[local-name()="export-errordetail"]')
        $hasImport = $null -ne $cs.SelectSingleNode('.//*[local-name()="import-errordetail"]')
        $type = if ($hasImport -and -not $hasExport) {
            Get-ImportErrorType $cs
        } elseif ($hasExport) {
            Get-ExportErrorType $cs
        } elseif ($hasImport) {
            Get-ImportErrorType $cs
        } else {
            '(bez error detailu)'
        }

        $anchor = $cs.GetAttribute('cs-dn')
        $ot = $cs.GetAttribute('object-type')
        $label = Get-AttrValue $cs 'techName'
        if (-not $label) { $label = Get-AttrValue $cs 'AccountName' }
        if (-not $label) { $label = Get-AttrValue $cs 'accountName' }

        $line = $anchor
        if ($ot) { $line = "$line`t$ot" }
        if ($label) { $line = "$line`t$label" }

        if (-not $groups.Contains($type)) {
            $groups[$type] = New-Object System.Collections.Generic.List[string]
        }
        $groups[$type].Add($line)
    }
}
finally {
    $reader.Close()
    $stream.Close()
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# Zdroj: $XmlPath")
[void]$sb.AppendLine("# Vygenerovano: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$sb.AppendLine("# Objektu: $objectCount")
[void]$sb.AppendLine("# Typu chyb: $($groups.Count)")
[void]$sb.AppendLine('# Sloupce pod typem: Anchor (cs-dn) <TAB> object-type <TAB> techName/AccountName (pokud je)')
[void]$sb.AppendLine()

$sorted = $groups.GetEnumerator() | Sort-Object { $_.Value.Count } -Descending
foreach ($g in $sorted) {
    [void]$sb.AppendLine(('=' * 80))
    [void]$sb.AppendLine(("{0}  ({1})" -f $g.Key, $g.Value.Count))
    [void]$sb.AppendLine(('=' * 80))
    foreach ($line in ($g.Value | Sort-Object)) {
        [void]$sb.AppendLine($line)
    }
    [void]$sb.AppendLine()
}

$utf8 = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($OutputPath, $sb.ToString(), $utf8)
Write-Host "Hotovo: $objectCount objektu, $($groups.Count) typu -> $OutputPath"
