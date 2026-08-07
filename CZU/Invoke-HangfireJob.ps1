<#
.SYNOPSIS
    Spustí Hangfire Recurring Job externě z PowerShellu.

.DESCRIPTION
    Skript nabízí dva způsoby spuštění Hangfire recurring jobu:

    METODA 1 – HTTP (výchozí, -Method Http):
      Odešle POST na Hangfire dashboard endpoint /hangfire/recurring/trigger.
      Funguje bez přímého přístupu k databázi.
      Vyžaduje dostupný Hangfire dashboard.

    METODA 2 – SQL (záložní, -Method Sql):
      Nastaví NextExecution daného jobu na "teď" přímo v Hangfire DB.
      Hangfire worker ho vyzvedne při nejbližší kontrole (obvykle < 15 s).
      Funguje i bez přístupu k dashboardu.

.PARAMETER JobId
    ID recurring jobu přesně tak, jak je zobrazeno v Hangfire dashboardu
    (sloupec "Id" na stránce /hangfire/recurring).
    Výchozí: daily_account_status_calculator

.PARAMETER DashboardUrl
    Základní URL Hangfire dashboardu (bez lomítka na konci).
    Výchozí: http://localhost/hangfire

.PARAMETER Method
    Http (výchozí) nebo Sql.

.PARAMETER ConnectionString
    Connection string pro SQL metodu.
    Výchozí: Data Source=MIM;Database=EasyIDM;...

.PARAMETER HangfireSchema
    Schéma Hangfire tabulek (výchozí: HangFire).

.PARAMETER DashboardUser
    Uživatelské jméno pro Basic Auth na dashboardu (volitelné).
    Není-li zadáno, použije se Windows autentizace (aktuální uživatel).

.PARAMETER DashboardPassword
    Heslo pro Basic Auth na dashboardu (volitelné).

.EXAMPLE
    # Spustit výchozí job přes HTTP (nejjednodušší způsob)
    .\Invoke-HangfireJob.ps1

.EXAMPLE
    # Spustit konkrétní job přes HTTP
    .\Invoke-HangfireJob.ps1 -JobId "daily_account_status_calculator"

.EXAMPLE
    # Spustit job záložní metodou přes SQL (nevyžaduje web)
    .\Invoke-HangfireJob.ps1 -JobId "daily_account_status_calculator" -Method Sql

.EXAMPLE
    # Vypsat seznam všech recurring jobů z DB
    .\Invoke-HangfireJob.ps1 -ListJobs -Method Sql
#>

[CmdletBinding(DefaultParameterSetName = 'Trigger')]
param(
    [Parameter(ParameterSetName = 'Trigger')]
    [string] $JobId = 'daily_account_status_calculator',

    [Parameter(ParameterSetName = 'Trigger')]
    [Parameter(ParameterSetName = 'List')]
    [ValidateSet('Http', 'Sql')]
    [string] $Method = 'Http',

    # HTTP parametry
    [Parameter(ParameterSetName = 'Trigger')]
    [string] $DashboardUrl = 'http://localhost/hangfire',

    [Parameter(ParameterSetName = 'Trigger')]
    [string] $DashboardUser = '',

    [Parameter(ParameterSetName = 'Trigger')]
    [string] $DashboardPassword = '',

    # SQL parametry
    [Parameter(ParameterSetName = 'Trigger')]
    [Parameter(ParameterSetName = 'List')]
    [string] $ConnectionString = 'Data Source=MIM;Database=EasyIDM;Integrated Security=True;TrustServerCertificate=True;Trusted_Connection=True;MultipleActiveResultSets=true',

    [Parameter(ParameterSetName = 'Trigger')]
    [Parameter(ParameterSetName = 'List')]
    [string] $HangfireSchema = 'HangFire',

    # Přepínač pro výpis jobů
    [Parameter(ParameterSetName = 'List', Mandatory)]
    [switch] $ListJobs
)

# Nastavení kódování konzoly na UTF-8 (oprava diakritiky ve výstupu)
#[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding            = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

# ─── POMOCNÁ FUNKCE: SQL ──────────────────────────────────────────────────────

function Invoke-HangfireSql {
    param([string]$Sql, [switch]$Scalar)
    $conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Sql
        $cmd.CommandTimeout = 30
        if ($Scalar) {
            return $cmd.ExecuteScalar()
        }
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
        $dt = New-Object System.Data.DataTable
        [void]$adapter.Fill($dt)
        return ,$dt
    }
    finally { $conn.Close() }
}

# ─── VÝPIS JOBŮ (–ListJobs) ──────────────────────────────────────────────────

if ($ListJobs) {
    Write-Host "Nacitam seznam Hangfire recurring jobu ze schematu [$HangfireSchema]..." -ForegroundColor Cyan

    $sqlList = @"
SELECT
    s.[Value]                                                           AS [JobId],
    hCron.[Value]                                                       AS [Cron],
    hLast.[Value]                                                       AS [LastExecution],
    hState.[Value]                                                      AS [LastJobState],
    s.[Score]                                                           AS [NextExecution_Unix]
FROM [$HangfireSchema].[Set] s
LEFT JOIN [$HangfireSchema].[Hash] hCron
    ON hCron.[Key] = 'recurring-job:' + s.[Value] AND hCron.[Field] = 'Cron'
LEFT JOIN [$HangfireSchema].[Hash] hLast
    ON hLast.[Key] = 'recurring-job:' + s.[Value] AND hLast.[Field] = 'LastExecution'
LEFT JOIN [$HangfireSchema].[Hash] hState
    ON hState.[Key] = 'recurring-job:' + s.[Value] AND hState.[Field] = 'LastJobState'
WHERE s.[Key] = 'recurring-jobs'
ORDER BY s.[Value]
"@

    try {
        $dt = Invoke-HangfireSql -Sql $sqlList
        Write-Host "`nNalezeno $($dt.Rows.Count) recurring jobu:`n" -ForegroundColor Green
        foreach ($row in $dt.Rows) {
            $next = if ($row['NextExecution_Unix'] -ne [System.DBNull]::Value -and [long]$row['NextExecution_Unix'] -gt 0) {
                [System.DateTimeOffset]::FromUnixTimeSeconds([long]$row['NextExecution_Unix']).LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss")
            } else { "-" }
            $jobIdPadded = $row['JobId'].ToString().PadRight(55)
            $state = if ($row['LastJobState'] -ne [System.DBNull]::Value) { $row['LastJobState'] } else { "?" }
            Write-Host "  $jobIdPadded  next: $next  cron: $($row['Cron'])  lastState: $state" -ForegroundColor White
        }
        Write-Host ""
    }
    catch {
        Write-Warning "Nelze nacist seznam jobu: $($_.Exception.Message)"
    }
    return
}

# ─── METODA 1: HTTP ───────────────────────────────────────────────────────────

if ($Method -eq 'Http') {
    Write-Host "Spoustim job '$JobId' pres HTTP dashboard: $DashboardUrl" -ForegroundColor Cyan

    $recurringUrl = "$DashboardUrl/recurring"
    $triggerUrl   = "$DashboardUrl/recurring/trigger"

    # Sestav základní parametry pro Invoke-WebRequest
    $baseParams = @{ UseBasicParsing = $true; ErrorAction = 'Stop' }
    if ($DashboardUser -ne '') {
        $pair  = "$DashboardUser`:$DashboardPassword"
        $b64   = [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes($pair))
        $baseParams['Headers'] = @{ Authorization = "Basic $b64" }
    } else {
        $baseParams['UseDefaultCredentials'] = $true
    }

    try {
        # ── Krok 1: GET /recurring – získat antiforgery cookie + CSRF token z <meta> ──
        $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
        $getResp = Invoke-WebRequest @baseParams -Uri $recurringUrl -SessionVariable session

        $csrfToken = $null
        if ($getResp.Content -match 'name="csrf-token"\s+content="([^"]+)"') {
            $csrfToken = $Matches[1]
            Write-Verbose "CSRF token ziskan: $($csrfToken.Substring(0,20))..."
        }
        if (-not $csrfToken) {
            throw "Nepodarilo se ziskat CSRF token z Hangfire dashboardu ($recurringUrl)."
        }

        # ── Krok 2: POST /recurring/trigger s tokenem v hlavičce ──
        $postHeaders = @{
            'X-Requested-With'          = 'XMLHttpRequest'
            'RequestVerificationToken'  = $csrfToken
        }
        if ($DashboardUser -ne '') { $postHeaders['Authorization'] = $baseParams['Headers']['Authorization'] }

        $body     = "jobs%5B%5D=$([System.Uri]::EscapeDataString($JobId))"
        $response = Invoke-WebRequest `
            -Uri         $triggerUrl `
            -Method      POST `
            -Headers     $postHeaders `
            -Body        $body `
            -ContentType 'application/x-www-form-urlencoded' `
            -WebSession  $session `
            -UseBasicParsing `
            -ErrorAction Stop

        if ($response.StatusCode -in 200, 204, 302) {
            Write-Host "Job '$JobId' byl uspesne spusten (HTTP $($response.StatusCode))." -ForegroundColor Green
        } else {
            Write-Warning "Neocekavany HTTP status: $($response.StatusCode). Zkuste metodu SQL."
        }
    }
    catch {
        $resp       = $_.Exception.Response
        $statusCode = if ($resp) { [int]$resp.StatusCode } else { 0 }
        Write-Warning "HTTP trigger selhal (status $statusCode): $($_.Exception.Message)"
        Write-Host "Tip: Zkuste zalozni metodu:  -Method Sql" -ForegroundColor Yellow
        throw
    }
    return
}

# ─── METODA 2: SQL ────────────────────────────────────────────────────────────

Write-Host "Spoustim job '$JobId' pres SQL (schema: [$HangfireSchema])..." -ForegroundColor Cyan

# Ověření existence jobu v recurring-jobs setu
$sqlCheck = "SELECT COUNT(*) FROM [$HangfireSchema].[Set] WHERE [Key] = 'recurring-jobs' AND [Value] = '$($JobId -replace "'","''")'"
$count = Invoke-HangfireSql -Sql $sqlCheck -Scalar

if ([int]$count -eq 0) {
    Write-Warning "Job '$JobId' nebyl nalezen v [$HangfireSchema].[Set] (Key='recurring-jobs')."
    Write-Host "Dostupne joby:" -ForegroundColor Yellow
    $sqlList = "SELECT [Value] FROM [$HangfireSchema].[Set] WHERE [Key] = 'recurring-jobs' ORDER BY [Value]"
    try {
        $dt = Invoke-HangfireSql -Sql $sqlList
        foreach ($row in $dt.Rows) { Write-Host "  - $($row['Value'])" }
    } catch { }
    throw "Job '$JobId' nenalezen. Viz seznam vyse."
}

# Nastavit NextExecution na "teď" – Score = aktuální Unix timestamp
# Hangfire worker joby vyzvedne při příštím polling cyklu (obvykle do 15 s)
$unixNow = [long][System.DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

$sqlTrigger = @"
UPDATE [$HangfireSchema].[Set]
SET    [Score] = $unixNow
WHERE  [Key]   = 'recurring-jobs'
  AND  [Value] = '$($JobId -replace "'","''")'
"@

$rows = Invoke-HangfireSql -Sql $sqlTrigger
Write-Host "NextExecution nastaven na: $(Get-Date) (Unix: $unixNow)" -ForegroundColor Green
# Volitelně: aktualizovat i NextExecution v Hash záznamu (pro starší Hangfire verze)
$sqlHashCheck = "SELECT COUNT(*) FROM [$HangfireSchema].[Hash] WHERE [Key] = 'recurring-job:$($JobId -replace "'","''")' AND [Field] = 'NextExecution'"
$hashCount = [int](Invoke-HangfireSql -Sql $sqlHashCheck -Scalar)

if ($hashCount -gt 0) {
    $nextExecStr = [System.DateTimeOffset]::UtcNow.ToString("o")
    $sqlHashUpdate = @"
UPDATE [$HangfireSchema].[Hash]
SET    [Value] = '$nextExecStr'
WHERE  [Key]   = 'recurring-job:$($JobId -replace "'","''")'
  AND  [Field] = 'NextExecution'
"@
    Invoke-HangfireSql -Sql $sqlHashUpdate | Out-Null
}

Write-Host "Job '$JobId' byl naplanovane ke spusteni (Hangfire worker ho vyzvedne do ~15 s)." -ForegroundColor Green