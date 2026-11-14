<#
.SYNOPSIS
    U kazdeho management agenta vypise, jak dopadl posledni import.

.DESCRIPTION
    Skript se spousti na MIM Sync serveru. Pripojeni k databazi
    FIMSynchronizationService cte z registru sluzby FIMSynchronizationService.
    Za posledni import povazuje nejnovejsi beh, jehoz profil ma krok
    full-import nebo delta-import. Kdyz definice profilu uz neexistuje,
    pouzije nazev profilu (obsahuje "import", nebo FI/DI/FIFS/FIDS/DIDS).

    Vysledek kroku success je v poradku. completed-* je varovani.
    Ostatni vysledky (stopped-*, no-start-*) jsou chyba.
    Navratovy kod 1 znamena, ze aspon jeden posledni import nedopadl jako success,
    nebo ze se nepodarilo odeslat report.

    Reporty jsou HTML a odesilaji se primo ze skriptu.
    -Full posle uplny prehled vsech MA. Bez prepinace bezi monitor chyb
    a mail odchazi hned, jen kdyz se objevi novy vysledek
    z ImmediateAlertResults (zatim jen no-start-ma).

    SMTP, odesilatel a prijemci jsou promenne na zacatku skriptu.
    HTML se pred odeslanim ulozi do C:\batch\reports.

.PARAMETER Full
    Uplny prehled vsech MA. Tento report se pri spusteni vzdy odesle.

.PARAMETER Server
    SQL server. Bez nej se bere z registru.

.PARAMETER Database
    Databaze. Vychozi je hodnota z registru, jinak FIMSynchronizationService.

.EXAMPLE
    .\Get-LastMaImport.ps1 -Full

.EXAMPLE
    .\Get-LastMaImport.ps1
#>

[CmdletBinding()]
param(
    [switch] $Full,
    [string] $Server,
    [string] $Database
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Posta. Hodnoty se berou jen odsud, ne z parametru spusteni.
$SmtpServer         = 'smtp.example.local'
$SmtpPort           = 25
$SmtpUseSsl         = $false
$SmtpUser           = ''
$SmtpPassword       = ''
$MailFrom           = 'mim-sync@example.local'
$MailTo             = @('idm-admin@example.local')
$HtmlReportDir      = 'C:\batch\reports'
# Okamzita notifikace. Dalsi vysledky sem doplnte, az je budete chtit hlasit hned.
$ImmediateAlertResults = @('no-start-ma')

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
        throw "Na tomhle pocitaci neni sluzba FIM Synchronization Service. Skript spuste na MIM Sync serveru."
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
        [Parameter(Mandatory = $true)][string] $Sql
    )
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = $Sql
    $cmd.CommandTimeout = 120
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
    $table = New-Object System.Data.DataTable
    [void]$adapter.Fill($table)
    # Carka zabraní tomu, aby PowerShell rozbalil DataTable na radky.
    return ,$table
}

function ConvertTo-LocalTime {
    param($Value)
    if ($null -eq $Value -or $Value -is [DBNull]) { return $null }
    $dt = [datetime]$Value
    if ($dt.Kind -eq [DateTimeKind]::Unspecified) {
        $dt = [DateTime]::SpecifyKind($dt, [DateTimeKind]::Utc)
    }
    return $dt.ToLocalTime()
}

function Get-ResultKind {
    param([string] $Result)
    if ([string]::IsNullOrWhiteSpace($Result)) { return 'Chyba' }
    if ($Result -eq 'success') { return 'OK' }
    if ($Result -eq 'in-progress') { return 'Bezi' }
    if ($Result -like 'completed-*') { return 'Varovani' }
    return 'Chyba'
}

function Test-ImportProfileName {
    param([string] $Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    if ($Name -match '(?i)import') { return $true }
    return [bool]($Name -match '(?i)(^|[^a-z])(fi|di|fifs|fids|dids)([^a-z]|$)')
}

function Get-ProfileImportSteps {
    param([string] $Xml, [string] $ProfileName)
    $found = [System.Collections.Generic.List[object]]::new()
    if ([string]::IsNullOrWhiteSpace($Xml) -or $Xml -notmatch 'step-type') { return @($found) }

    $doc = New-Object System.Xml.XmlDocument
    $doc.XmlResolver = $null
    $doc.LoadXml($Xml)

    $configs = @($doc.SelectNodes("//*[local-name()='run-configuration']"))
    $scope = $null
    if ($configs.Count -eq 0) {
        $scope = $doc.DocumentElement
    }
    else {
        foreach ($cfg in $configs) {
            $nameNode = $cfg.SelectSingleNode("./*[local-name()='name']")
            if ($nameNode -and [string]::Equals($nameNode.InnerText, $ProfileName, [StringComparison]::OrdinalIgnoreCase)) {
                $scope = $cfg
                break
            }
        }
        if (-not $scope) { $scope = $configs[0] }
    }

    $steps = @($scope.SelectNodes(".//*[local-name()='step']"))
    for ($i = 0; $i -lt $steps.Count; $i++) {
        $typeNode = $steps[$i].SelectSingleNode(".//*[local-name()='step-type']")
        $type = if ($typeNode) { $typeNode.GetAttribute('type') } else { '' }
        if ($type -eq 'full-import' -or $type -eq 'delta-import') {
            $found.Add([pscustomobject]@{ Number = ($i + 1); StepType = $type })
        }
    }
    return @($found)
}

function Find-RunProfileXmlColumn {
    param($Connection)
    $sample = Invoke-FimQuery -Connection $Connection -Sql "SELECT TOP 30 * FROM dbo.mms_run_profile WITH (NOLOCK)"
    foreach ($col in @($sample.Columns)) {
        $name = [string]$col.ColumnName
        if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { continue }
        if ($col.DataType -ne [string]) { continue }
        foreach ($row in $sample.Rows) {
            $text = [string]$row[$name]
            if ($text -match 'step-type') { return $name }
        }
    }
    return $null
}

function Get-Cell {
    param($Row, [string] $Name)
    if ($null -eq $Row -or $Row -is [DBNull]) { return $null }
    if ($Row[$Name] -is [DBNull]) { return $null }
    return $Row[$Name]
}

function ConvertTo-HtmlEncoded {
    param($Value)
    if ($null -eq $Value) { return '' }
    return [System.Net.WebUtility]::HtmlEncode([string]$Value)
}

function Format-ReportTime {
    param($Value)
    if ($null -eq $Value) { return '' }
    return '{0:yyyy-MM-dd HH:mm:ss}' -f $Value
}

function Get-StatusStyle {
    param([string] $Stav, [string] $Vysledek)
    $alert = $Vysledek -and ($script:ImmediateAlertResults -contains $Vysledek)
    if ($alert -or $Stav -eq 'Chyba') {
        return ,@{ Accent = '#c0392b'; BadgeBg = '#fdecea'; BadgeFg = '#922b21'; Row = '#fff8f7'; Label = $(if ($Stav) { $Stav } else { 'Chyba' }) }
    }
    switch ($Stav) {
        'OK'       { return ,@{ Accent = '#1e8449'; BadgeBg = '#e8f8ef'; BadgeFg = '#196f3d'; Row = '#ffffff'; Label = 'OK' } }
        'Varovani' { return ,@{ Accent = '#d68910'; BadgeBg = '#fef9e7'; BadgeFg = '#9a6b00'; Row = '#fffdf6'; Label = 'Varovani' } }
        'Bezi'     { return ,@{ Accent = '#1a5276'; BadgeBg = '#eaf3f8'; BadgeFg = '#1a5276'; Row = '#f8fbfd'; Label = 'Bezi' } }
        default    { return ,@{ Accent = '#c0392b'; BadgeBg = '#fdecea'; BadgeFg = '#922b21'; Row = '#fff8f7'; Label = $(if ($Stav) { $Stav } else { 'Chyba' }) } }
    }
}

function Format-CountHtml {
    param($Value, [switch] $Alert)
    if ($null -eq $Value -or "$Value" -eq '') { return '<span style="color:#98a2b3;">&mdash;</span>' }
    $n = 0
    if (-not [int]::TryParse([string]$Value, [ref]$n)) { return (ConvertTo-HtmlEncoded $Value) }
    if ($Alert -and $n -gt 0) { return "<span style=`"color:#c0392b;font-weight:700;`">$n</span>" }
    if ($n -eq 0) { return '<span style="color:#b0b8c1;">0</span>' }
    return "<span style=`"font-weight:600;color:#1f2933;`">$n</span>"
}

function Format-TimeHtml {
    param($Value)
    if ($null -eq $Value) { return '<span style="color:#98a2b3;">&mdash;</span>' }
    $day = '{0:yyyy-MM-dd}' -f $Value
    $clock = '{0:HH:mm:ss}' -f $Value
    return "<div style=`"white-space:nowrap;font-weight:600;`">$day</div><div style=`"color:#66788a;font-size:11px;white-space:nowrap;`">$clock</div>"
}

function New-SummaryCardHtml {
    param([string] $Caption, [int] $Number, [string] $Color)
    return @"
<td style="background:#f7f9fb;border:1px solid #e4e9ef;border-top:3px solid $Color;padding:12px 16px;min-width:120px;">
  <div style="font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:#66788a;font-weight:700;">$(ConvertTo-HtmlEncoded $Caption)</div>
  <div style="font-size:28px;line-height:1.2;font-weight:700;color:$Color;margin-top:2px;">$Number</div>
</td>
"@
}

function New-ImportReportHtml {
    param(
        [Parameter(Mandatory = $true)][string] $Title,
        [Parameter(Mandatory = $true)][string] $Intro,
        [Parameter(Mandatory = $true)] $Rows,
        [Parameter(Mandatory = $true)][string] $ServerName,
        [Parameter(Mandatory = $true)][string] $DatabaseName
    )
    $items = @($Rows)
    $generated = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $countOk = @($items | Where-Object { $_.Stav -eq 'OK' }).Count
    $countWarn = @($items | Where-Object { $_.Stav -eq 'Varovani' -or $_.Stav -eq 'Bezi' }).Count
    $countBad = @($items | Where-Object { $_.Stav -ne 'OK' -and $_.Stav -ne 'Varovani' -and $_.Stav -ne 'Bezi' }).Count
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<!DOCTYPE html>')
    [void]$sb.AppendLine('<html lang="cs"><head><meta charset="utf-8" />')
    [void]$sb.AppendLine("<title>$(ConvertTo-HtmlEncoded $Title)</title></head>")
    [void]$sb.AppendLine('<body style="margin:0;padding:0;background:#eef2f5;color:#1f2933;font-family:Segoe UI,Tahoma,Arial,sans-serif;">')
    [void]$sb.AppendLine('<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#1e3a5f;"><tr><td style="padding:22px 28px;">')
    [void]$sb.AppendLine('<div style="font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:#9ec1e6;font-weight:700;">MIM Synchronization</div>')
    [void]$sb.AppendLine("<div style=`"font-size:26px;line-height:1.25;color:#ffffff;font-weight:700;margin-top:4px;`">$(ConvertTo-HtmlEncoded $Title)</div>")
    [void]$sb.AppendLine("<div style=`"font-size:14px;line-height:1.45;color:#d6e4f0;margin-top:8px;`">$(ConvertTo-HtmlEncoded $Intro)</div>")
    [void]$sb.AppendLine('</td></tr></table>')
    [void]$sb.AppendLine('<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr><td style="padding:20px 28px 28px 28px;">')
    [void]$sb.AppendLine('<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 16px 0;"><tr>')
    [void]$sb.AppendLine((New-SummaryCardHtml -Caption 'Celkem' -Number $items.Count -Color '#1e3a5f'))
    [void]$sb.AppendLine('<td style="width:10px;font-size:0;line-height:0;">&nbsp;</td>')
    [void]$sb.AppendLine((New-SummaryCardHtml -Caption 'V poradku' -Number $countOk -Color '#1e8449'))
    [void]$sb.AppendLine('<td style="width:10px;font-size:0;line-height:0;">&nbsp;</td>')
    [void]$sb.AppendLine((New-SummaryCardHtml -Caption 'Varovani / bezi' -Number $countWarn -Color '#d68910'))
    [void]$sb.AppendLine('<td style="width:10px;font-size:0;line-height:0;">&nbsp;</td>')
    [void]$sb.AppendLine((New-SummaryCardHtml -Caption 'Chyba' -Number $countBad -Color '#c0392b'))
    [void]$sb.AppendLine('</tr></table>')
    [void]$sb.AppendLine("<p style=`"margin:0 0 14px 0;font-size:12px;color:#66788a;`">Server $(ConvertTo-HtmlEncoded $env:COMPUTERNAME) &nbsp;|&nbsp; SQL $(ConvertTo-HtmlEncoded $ServerName) &nbsp;|&nbsp; databaze $(ConvertTo-HtmlEncoded $DatabaseName) &nbsp;|&nbsp; vygenerovano $generated</p>")
    [void]$sb.AppendLine('<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;background:#ffffff;border:1px solid #e4e9ef;font-size:13px;">')
    [void]$sb.AppendLine('<thead><tr>')
    $heads = @(
        @{ Label = 'MA'; Align = 'left' },
        @{ Label = 'Stav'; Align = 'left' },
        @{ Label = 'Vysledek'; Align = 'left' },
        @{ Label = 'Typ'; Align = 'left' },
        @{ Label = 'Profil'; Align = 'left' },
        @{ Label = 'Start'; Align = 'left' },
        @{ Label = 'Konec'; Align = 'left' },
        @{ Label = 'Beze zmeny'; Align = 'right' },
        @{ Label = 'Add'; Align = 'right' },
        @{ Label = 'Update'; Align = 'right' },
        @{ Label = 'Delete'; Align = 'right' },
        @{ Label = 'Chyby'; Align = 'right' },
        @{ Label = 'Vysledek behu'; Align = 'left' }
    )
    foreach ($head in $heads) {
        [void]$sb.AppendLine("<th style=`"background:#1e3a5f;color:#ffffff;font-size:11px;letter-spacing:.04em;text-transform:uppercase;font-weight:700;text-align:$($head.Align);padding:10px 8px;border-bottom:1px solid #16304f;`">$(ConvertTo-HtmlEncoded $head.Label)</th>")
    }
    [void]$sb.AppendLine('</tr></thead><tbody>')
    if ($items.Count -eq 0) {
        [void]$sb.AppendLine('<tr><td colspan="13" style="padding:22px 12px;color:#66788a;text-align:center;">Zadny zaznam.</td></tr>')
    }
    $index = 0
    foreach ($row in $items) {
        $style = Get-StatusStyle -Stav $row.Stav -Vysledek $row.Vysledek
        $rowBg = $style.Row
        if ($row.Stav -eq 'OK' -and ($index % 2) -eq 1) { $rowBg = '#f7f9fb' }
        $td = "padding:9px 8px;border-bottom:1px solid #e8edf2;background:$rowBg;vertical-align:middle;"
        $badge = "<span style=`"display:inline-block;padding:3px 9px;background:$($style.BadgeBg);color:$($style.BadgeFg);font-size:11px;font-weight:700;letter-spacing:.04em;text-transform:uppercase;`">$(ConvertTo-HtmlEncoded $style.Label)</span>"
        [void]$sb.AppendLine('<tr>')
        [void]$sb.AppendLine("<td style=`"$td border-left:4px solid $($style.Accent);font-weight:700;white-space:nowrap;`">$(ConvertTo-HtmlEncoded $row.MA)</td>")
        [void]$sb.AppendLine("<td style=`"$td`">$badge</td>")
        [void]$sb.AppendLine("<td style=`"$td font-family:Consolas,Courier New,monospace;font-size:12px;`">$(ConvertTo-HtmlEncoded $row.Vysledek)</td>")
        [void]$sb.AppendLine("<td style=`"$td color:#41505f;`">$(ConvertTo-HtmlEncoded $row.Typ)</td>")
        [void]$sb.AppendLine("<td style=`"$td`">$(ConvertTo-HtmlEncoded $row.Profil)</td>")
        [void]$sb.AppendLine("<td style=`"$td`">$(Format-TimeHtml $row.Start)</td>")
        [void]$sb.AppendLine("<td style=`"$td`">$(Format-TimeHtml $row.Konec)</td>")
        [void]$sb.AppendLine("<td style=`"$td text-align:right;`">$(Format-CountHtml $row.BezeZmeny)</td>")
        [void]$sb.AppendLine("<td style=`"$td text-align:right;`">$(Format-CountHtml $row.Add)</td>")
        [void]$sb.AppendLine("<td style=`"$td text-align:right;`">$(Format-CountHtml $row.Update)</td>")
        [void]$sb.AppendLine("<td style=`"$td text-align:right;`">$(Format-CountHtml $row.Delete)</td>")
        [void]$sb.AppendLine("<td style=`"$td text-align:right;`">$(Format-CountHtml $row.Chyby -Alert)</td>")
        [void]$sb.AppendLine("<td style=`"$td font-family:Consolas,Courier New,monospace;font-size:12px;color:#41505f;`">$(ConvertTo-HtmlEncoded $row.Beh)</td>")
        [void]$sb.AppendLine('</tr>')
        $index++
    }
    [void]$sb.AppendLine('</tbody></table>')
    [void]$sb.AppendLine('<p style="margin:14px 0 0 0;font-size:11px;color:#98a2b3;">Posledni import kazdeho management agenta. Cas je mistni.</p>')
    [void]$sb.AppendLine('</td></tr></table></body></html>')
    return $sb.ToString()
}

function Read-MailState {
    param([string] $Path)
    $empty = @{ DailySentDate = ''; Alerted = @() }
    if (-not (Test-Path -LiteralPath $Path)) { return ,$empty }
    try {
        $obj = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return ,$empty
    }
    $alerted = @()
    if ($obj.PSObject.Properties['alerted'] -and $null -ne $obj.alerted) {
        $alerted = @($obj.alerted | ForEach-Object { [string]$_ })
    }
    $sent = ''
    if ($obj.PSObject.Properties['dailySentDate'] -and $null -ne $obj.dailySentDate) {
        $sent = [string]$obj.dailySentDate
    }
    return ,@{ DailySentDate = $sent; Alerted = $alerted }
}

function Save-MailState {
    param([string] $Path, [hashtable] $State)
    $payload = [pscustomobject]@{
        dailySentDate = [string]$State.DailySentDate
        alerted       = @($State.Alerted)
    }
    $json = $payload | ConvertTo-Json
    $utf8 = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($Path, $json, $utf8)
}

function Send-ReportMail {
    param(
        [Parameter(Mandatory = $true)][string] $Subject,
        [Parameter(Mandatory = $true)][string] $HtmlBody
    )
    $message = New-Object System.Net.Mail.MailMessage
    $client = New-Object System.Net.Mail.SmtpClient($script:SmtpServer, $script:SmtpPort)
    try {
        $message.From = $script:MailFrom
        foreach ($addr in @($script:MailTo)) {
            if (-not [string]::IsNullOrWhiteSpace($addr)) { [void]$message.To.Add($addr) }
        }
        if ($message.To.Count -eq 0) { throw 'V promenne MailTo neni zadny prijemce.' }
        $message.Subject = $Subject
        $message.SubjectEncoding = [System.Text.Encoding]::UTF8
        $message.BodyEncoding = [System.Text.Encoding]::UTF8
        $message.IsBodyHtml = $true
        $message.Body = $HtmlBody
        $client.EnableSsl = [bool]$script:SmtpUseSsl
        if (-not [string]::IsNullOrWhiteSpace($script:SmtpUser)) {
            $client.Credentials = New-Object System.Net.NetworkCredential($script:SmtpUser, $script:SmtpPassword)
        }
        $client.Send($message)
    }
    finally {
        $message.Dispose()
        $client.Dispose()
    }
}

function Get-AlertKey {
    param($Row)
    $start = Format-ReportTime $Row.Start
    return '{0}|{1}|{2}' -f $Row.MA, $start, $Row.Vysledek
}

$info = Get-FimSyncConnectionInfo
Write-Log ("Pripojuji se k {0}, databaze {1}" -f $info.Server, $info.Database)

$cs = "Server={0};Database={1};Integrated Security=True;Connection Timeout=15" -f $info.Server, $info.Database
$connection = New-Object System.Data.SqlClient.SqlConnection $cs
$connection.Open()
try {
    $xmlColumn = Find-RunProfileXmlColumn -Connection $connection
    if ($xmlColumn) {
        Write-Log "Definice profilu: mms_run_profile.$xmlColumn"
    }
    else {
        Write-Log "Definice profilu nema XML se step-type. Import poznavam podle nazvu profilu." -Level WARN
    }

    $profiles = @{}
    if ($xmlColumn) {
        $profileTable = Invoke-FimQuery -Connection $connection -Sql @"
SELECT ma_id, run_profile_name, [$xmlColumn] AS profile_xml
FROM dbo.mms_run_profile WITH (NOLOCK)
"@
        foreach ($row in $profileTable.Rows) {
            $key = '{0}|{1}' -f ([guid](Get-Cell $row 'ma_id')).ToString(), ([string](Get-Cell $row 'run_profile_name'))
            $profiles[$key] = [string](Get-Cell $row 'profile_xml')
        }
    }

    $runTable = Invoke-FimQuery -Connection $connection -Sql @"
SELECT ma.ma_id, ma.ma_name,
       rh.run_history_id, rh.run_profile_name, rh.run_result,
       rh.start_date, rh.end_date, rh.is_run_complete
FROM dbo.mms_management_agent AS ma WITH (NOLOCK)
LEFT JOIN (
    SELECT run_history_id, ma_id, run_profile_name, run_result, start_date, end_date, is_run_complete,
           ROW_NUMBER() OVER (PARTITION BY ma_id, run_profile_name ORDER BY start_date DESC) AS rn
    FROM dbo.mms_run_history WITH (NOLOCK)
) AS rh ON rh.ma_id = ma.ma_id AND rh.rn = 1
"@

    $stepTable = Invoke-FimQuery -Connection $connection -Sql @"
SELECT sh.run_history_id, sh.step_number, sh.step_result, sh.start_date, sh.end_date,
       sh.stage_no_change, sh.stage_add, sh.stage_update, sh.stage_rename,
       sh.stage_delete, sh.stage_deleteadd, sh.stage_failure
FROM dbo.mms_step_history AS sh WITH (NOLOCK)
INNER JOIN (
    SELECT run_history_id,
           ROW_NUMBER() OVER (PARTITION BY ma_id, run_profile_name ORDER BY start_date DESC) AS rn
    FROM dbo.mms_run_history WITH (NOLOCK)
) AS latest ON latest.run_history_id = sh.run_history_id AND latest.rn = 1
"@
}
finally {
    $connection.Close()
}

$stepsByRun = @{}
foreach ($row in $stepTable.Rows) {
    $id = [guid](Get-Cell $row 'run_history_id')
    $key = $id.ToString()
    if (-not $stepsByRun.ContainsKey($key)) { $stepsByRun[$key] = [System.Collections.Generic.List[object]]::new() }
    $stepsByRun[$key].Add([pscustomobject]@{
        Number      = [int](Get-Cell $row 'step_number')
        Result      = [string](Get-Cell $row 'step_result')
        Start       = ConvertTo-LocalTime (Get-Cell $row 'start_date')
        End         = ConvertTo-LocalTime (Get-Cell $row 'end_date')
        NoChange    = [int](Get-Cell $row 'stage_no_change')
        Add         = [int](Get-Cell $row 'stage_add')
        Update      = [int](Get-Cell $row 'stage_update')
        Rename      = [int](Get-Cell $row 'stage_rename')
        Delete      = [int](Get-Cell $row 'stage_delete')
        DeleteAdd   = [int](Get-Cell $row 'stage_deleteadd')
        Failure     = [int](Get-Cell $row 'stage_failure')
    })
}

$byMa = @{}
foreach ($row in $runTable.Rows) {
    $maName = [string](Get-Cell $row 'ma_name')
    if (-not $byMa.ContainsKey($maName)) { $byMa[$maName] = [System.Collections.Generic.List[object]]::new() }
    $historyId = Get-Cell $row 'run_history_id'
    if ($null -eq $historyId) { continue }
    $byMa[$maName].Add([pscustomobject]@{
        MaId        = [guid](Get-Cell $row 'ma_id')
        Profile     = [string](Get-Cell $row 'run_profile_name')
        RunResult   = [string](Get-Cell $row 'run_result')
        Start       = ConvertTo-LocalTime (Get-Cell $row 'start_date')
        End         = ConvertTo-LocalTime (Get-Cell $row 'end_date')
        Complete    = Get-Cell $row 'is_run_complete'
        HistoryId   = [guid]$historyId
    })
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($maName in ($byMa.Keys | Sort-Object)) {
    $chosen = $null
    $chosenStep = $null
    $chosenType = $null
    $runs = @($byMa[$maName] | Sort-Object Start -Descending)
    foreach ($run in $runs) {
        $profileKey = '{0}|{1}' -f $run.MaId.ToString(), $run.Profile
        $xml = if ($profiles.ContainsKey($profileKey)) { $profiles[$profileKey] } else { $null }
        $importSteps = @(Get-ProfileImportSteps -Xml $xml -ProfileName $run.Profile)
        $runSteps = @()
        $hid = $run.HistoryId.ToString()
        if ($stepsByRun.ContainsKey($hid)) { $runSteps = @($stepsByRun[$hid] | Sort-Object Number) }

        if ($importSteps.Count -gt 0) {
            $picked = $null
            foreach ($importStep in $importSteps) {
                $match = @($runSteps | Where-Object { $_.Number -eq $importStep.Number }) | Select-Object -First 1
                if ($match) { $picked = $match; $chosenType = $importStep.StepType }
            }
            if (-not $picked -and $runSteps.Count -gt 0) {
                $picked = $runSteps[0]
                $chosenType = $importSteps[0].StepType
            }
            if ($picked) {
                $chosen = $run
                $chosenStep = $picked
                break
            }
        }
        elseif ((Test-ImportProfileName $run.Profile) -and $runSteps.Count -gt 0) {
            $chosen = $run
            $chosenStep = $runSteps[0]
            $chosenType = 'import'
            break
        }
    }

    if (-not $chosen) {
        $results.Add([pscustomobject]@{
            MA = $maName; Stav = 'Chyba'; Vysledek = 'bez importu'; Typ = ''; Profil = ''
            Start = $null; Konec = $null; BezeZmeny = $null; Add = $null; Update = $null
            Delete = $null; Chyby = $null; Beh = ''
        })
        continue
    }

    $stepResult = $chosenStep.Result
    if ([string]::IsNullOrWhiteSpace($stepResult)) { $stepResult = $chosen.RunResult }
    $results.Add([pscustomobject]@{
        MA         = $maName
        Stav       = Get-ResultKind $stepResult
        Vysledek   = $stepResult
        Typ        = $chosenType
        Profil     = $chosen.Profile
        Start      = $chosenStep.Start
        Konec      = $chosenStep.End
        BezeZmeny  = $chosenStep.NoChange
        Add        = $chosenStep.Add
        Update     = $chosenStep.Update
        Delete     = $chosenStep.Delete
        Chyby      = $chosenStep.Failure
        Beh        = $chosen.RunResult
    })
}

$ordered = @($results | Sort-Object @{ Expression = { if ($_.Stav -eq 'OK') { 2 } elseif ($_.Stav -eq 'Varovani' -or $_.Stav -eq 'Bezi') { 1 } else { 0 } } }, MA)

$header = "{0,-28} {1,-10} {2,-28} {3,-14} {4,-32} {5,-19} {6,8} {7,8} {8,8} {9,8}" -f 'MA', 'Stav', 'Vysledek', 'Typ', 'Profil', 'Start', 'Add', 'Update', 'Delete', 'Chyby'
Write-Host ''
Write-Host $header
Write-Host ('-' * $header.Length)
foreach ($row in $ordered) {
    $color = switch ($row.Stav) {
        'OK'       { 'Green' }
        'Varovani' { 'Yellow' }
        'Bezi'     { 'Cyan' }
        default    { 'Red' }
    }
    $startText = if ($row.Start) { '{0:yyyy-MM-dd HH:mm}' -f $row.Start } else { '' }
    $line = "{0,-28} {1,-10} {2,-28} {3,-14} {4,-32} {5,-19} {6,8} {7,8} {8,8} {9,8}" -f `
        $row.MA, $row.Stav, $row.Vysledek, $row.Typ, $row.Profil, $startText, $row.Add, $row.Update, $row.Delete, $row.Chyby
    Write-Host $line -ForegroundColor $color
}

$ok = @($ordered | Where-Object { $_.Stav -eq 'OK' }).Count
$warn = @($ordered | Where-Object { $_.Stav -eq 'Varovani' -or $_.Stav -eq 'Bezi' }).Count
$bad = @($ordered | Where-Object { $_.Stav -eq 'Chyba' }).Count
Write-Host ''
Write-Log ("MA: {0}, v poradku: {1}, varovani nebo bezi: {2}, chyba nebo bez importu: {3}" -f $ordered.Count, $ok, $warn, $bad)

$outDir = Join-Path $PSScriptRoot 'Reports'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$utf8 = New-Object System.Text.UTF8Encoding $true
$txtPath = Join-Path $outDir ("LastImport_{0}.txt" -f $stamp)
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("MA`tStav`tVysledek`tTyp`tProfil`tStart`tKonec`tBeze zmeny`tAdd`tUpdate`tDelete`tChyby`tVysledek behu")
foreach ($row in $ordered) {
    $startText = Format-ReportTime $row.Start
    $endText = Format-ReportTime $row.Konec
    [void]$sb.AppendLine(("{0}`t{1}`t{2}`t{3}`t{4}`t{5}`t{6}`t{7}`t{8}`t{9}`t{10}`t{11}`t{12}" -f `
        $row.MA, $row.Stav, $row.Vysledek, $row.Typ, $row.Profil, $startText, $endText, `
        $row.BezeZmeny, $row.Add, $row.Update, $row.Delete, $row.Chyby, $row.Beh))
}
[System.IO.File]::WriteAllText($txtPath, $sb.ToString(), $utf8)
Write-Log "TXT: $txtPath"

$statePath = Join-Path $outDir 'LastImportMail.state.json'
$mailState = Read-MailState -Path $statePath
$today = Get-Date -Format 'yyyy-MM-dd'
$mailFailed = $false
if (-not (Test-Path -LiteralPath $HtmlReportDir)) { New-Item -ItemType Directory -Path $HtmlReportDir -Force | Out-Null }

if ($Full) {
    Write-Log "Rezim: uplny prehled, report se odesle."
    $dailyIntro = "Uplny prehled posledniho importu vsech management agentu."
    $dailyHtml = New-ImportReportHtml -Title 'Prehled importu MA' -Intro $dailyIntro -Rows $ordered -ServerName $info.Server -DatabaseName $info.Database
    $dailyPath = Join-Path $HtmlReportDir ("LastImport_full_{0}.html" -f $stamp)
    [System.IO.File]::WriteAllText($dailyPath, $dailyHtml, $utf8)
    Write-Log "HTML prehled: $dailyPath"
    try {
        Send-ReportMail -Subject ("[MIM] Prehled importu MA {0} ({1})" -f $today, $env:COMPUTERNAME) -HtmlBody $dailyHtml
        Write-Log ("Prehled odeslan na {0}" -f ($MailTo -join ', '))
    }
    catch {
        $mailFailed = $true
        Write-Log ("Prehled se nepodarilo odeslat: {0}" -f $_.Exception.Message) -Level ERROR
    }
}
else {
    Write-Log ("Rezim: monitor chyb ({0})." -f ($ImmediateAlertResults -join ', '))
    $alertRows = @($ordered | Where-Object { $ImmediateAlertResults -contains $_.Vysledek })
    $newAlerts = @($alertRows | Where-Object { $mailState.Alerted -notcontains (Get-AlertKey $_) })
    if ($newAlerts.Count -gt 0) {
        $names = @($newAlerts | ForEach-Object { $_.MA }) -join ', '
        $alertIntro = "Import skoncil vysledkem, ktery se hlasi hned ($($ImmediateAlertResults -join ', ')). MA: $names."
        $alertHtml = New-ImportReportHtml -Title 'Okamzite hlaseni importu MA' -Intro $alertIntro -Rows $newAlerts -ServerName $info.Server -DatabaseName $info.Database
        $alertPath = Join-Path $HtmlReportDir ("LastImport_alert_{0}.html" -f $stamp)
        [System.IO.File]::WriteAllText($alertPath, $alertHtml, $utf8)
        Write-Log "HTML hlaseni: $alertPath"
        try {
            $subjectNames = $names
            if ($subjectNames.Length -gt 120) { $subjectNames = $subjectNames.Substring(0, 117) + '...' }
            $kinds = (@($newAlerts | ForEach-Object { $_.Vysledek } | Select-Object -Unique) -join ', ')
            Send-ReportMail -Subject ("[MIM] {0} ({1}): {2}" -f $kinds, $env:COMPUTERNAME, $subjectNames) -HtmlBody $alertHtml
            foreach ($row in $newAlerts) {
                $mailState.Alerted += (Get-AlertKey $row)
            }
            Save-MailState -Path $statePath -State $mailState
            Write-Log ("Okamzite hlaseni odeslano na {0}" -f ($MailTo -join ', '))
        }
        catch {
            $mailFailed = $true
            Write-Log ("Okamzite hlaseni se nepodarilo odeslat: {0}" -f $_.Exception.Message) -Level ERROR
        }
    }
    elseif ($alertRows.Count -gt 0) {
        Write-Log ("Vysledek {0} uz byl hlasen, novy beh se neobjevil." -f ($ImmediateAlertResults -join ', '))
    }
    else {
        Write-Log ("Zadny MA nema vysledek {0}." -f ($ImmediateAlertResults -join ', '))
    }
}

if ($mailFailed -or $bad -gt 0 -or $warn -gt 0) { exit 1 }
exit 0
