# Invoke-OracleQueryUI.ps1
# Windows Forms GUI pro Oracle dotazy - SQL editor + DataGrid

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Data

# ── Konfigurace Oracle ────────────────────────────────────────────────────────
$OracleDll  = "C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\Extensions\Oracle.ManagedDataAccess.dll"
$DataSource = "(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(COMMUNITY=tcp.world)(PROTOCOL=TCP)(Host=magi.oradb-t.czu.cz)(Port=1523)))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME=MACV.oradb.czu.cz)))"
$Username   = "IDM"

Add-Type -Path $OracleDll

# ── Heslo pri spusteni ────────────────────────────────────────────────────────
$pwdForm  = New-Object System.Windows.Forms.Form
$pwdForm.Text          = "Oracle prihlaseni"
$pwdForm.Size          = New-Object System.Drawing.Size(340, 140)
$pwdForm.StartPosition = "CenterScreen"
$pwdForm.FormBorderStyle = "FixedDialog"
$pwdForm.MaximizeBox   = $false

$lblPwd = New-Object System.Windows.Forms.Label
$lblPwd.Text     = "Heslo ($Username):"
$lblPwd.Location = New-Object System.Drawing.Point(12, 18)
$lblPwd.AutoSize = $true
$pwdForm.Controls.Add($lblPwd)

$txtPwd = New-Object System.Windows.Forms.TextBox
$txtPwd.Location      = New-Object System.Drawing.Point(12, 38)
$txtPwd.Width         = 300
$txtPwd.PasswordChar  = '*'
$pwdForm.Controls.Add($txtPwd)

$btnLogin = New-Object System.Windows.Forms.Button
$btnLogin.Text         = "Pripojit"
$btnLogin.Location     = New-Object System.Drawing.Point(217, 68)
$btnLogin.Width        = 95
$btnLogin.DialogResult = [System.Windows.Forms.DialogResult]::OK
$pwdForm.AcceptButton  = $btnLogin
$pwdForm.Controls.Add($btnLogin)

if ($pwdForm.ShowDialog() -ne "OK") { exit }
$PlainPass = $txtPwd.Text
$ConnStr   = "Data Source=$DataSource;User Id=$Username;Password=$PlainPass;Pooling=false;Connection Timeout=30;"

# ── Hlavni okno ───────────────────────────────────────────────────────────────
$form = New-Object System.Windows.Forms.Form
$form.Text          = "Oracle Query — Magion ($Username@MACV)"
$form.Size          = New-Object System.Drawing.Size(1200, 800)
$form.StartPosition = "CenterScreen"
$form.Font          = New-Object System.Drawing.Font("Segoe UI", 9)

# ── Toolbar ───────────────────────────────────────────────────────────────────
$toolStrip = New-Object System.Windows.Forms.ToolStrip
$toolStrip.Dock = "Top"

$btnRun = New-Object System.Windows.Forms.ToolStripButton
$btnRun.Text        = "Spustit  (F5)"
$btnRun.ToolTipText = "Spustit SQL dotaz"
$toolStrip.Items.Add($btnRun) | Out-Null

$toolStrip.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

$btnClear = New-Object System.Windows.Forms.ToolStripButton
$btnClear.Text = "Vymazat SQL"
$toolStrip.Items.Add($btnClear) | Out-Null

$toolStrip.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

$btnExport = New-Object System.Windows.Forms.ToolStripButton
$btnExport.Text        = "Export CSV"
$btnExport.ToolTipText = "Exportovat vysledky do CSV"
$toolStrip.Items.Add($btnExport) | Out-Null

$toolStrip.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

$lblStatus = New-Object System.Windows.Forms.ToolStripLabel
$lblStatus.Text = "Pripraveno"
$toolStrip.Items.Add($lblStatus) | Out-Null

$form.Controls.Add($toolStrip)

# ── SplitContainer ────────────────────────────────────────────────────────────
$split = New-Object System.Windows.Forms.SplitContainer
$split.Dock        = "Fill"
$split.Orientation = "Horizontal"
$split.SplitterDistance = 180
$form.Controls.Add($split)

# ── Horni panel: SQL editor ───────────────────────────────────────────────────
$lblSql = New-Object System.Windows.Forms.Label
$lblSql.Text     = "SQL dotaz:"
$lblSql.Location = New-Object System.Drawing.Point(4, 4)
$lblSql.AutoSize = $true
$split.Panel1.Controls.Add($lblSql)

$txtSql = New-Object System.Windows.Forms.TextBox
$txtSql.Multiline   = $true
$txtSql.ScrollBars  = "Both"
$txtSql.WordWrap    = $false
$txtSql.Font        = New-Object System.Drawing.Font("Consolas", 10)
$txtSql.Dock        = "Fill"
$txtSql.AcceptsTab  = $true
$txtSql.Text        = "SELECT * FROM IDM.V_IDM_KOMISE WHERE ROWNUM <= 100"
$split.Panel1.Controls.Add($txtSql)

# ── Spodni panel: DataGridView ────────────────────────────────────────────────
$grid = New-Object System.Windows.Forms.DataGridView
$grid.Dock                  = "Fill"
$grid.ReadOnly              = $true
$grid.AllowUserToAddRows    = $false
$grid.AllowUserToDeleteRows = $false
$grid.AutoSizeColumnsMode   = "DisplayedCells"
$grid.ColumnHeadersHeightSizeMode = "AutoSize"
$grid.SelectionMode         = "FullRowSelect"
$grid.BackgroundColor       = [System.Drawing.Color]::White
$grid.BorderStyle           = "None"
$grid.RowHeadersVisible     = $false
$grid.AlternatingRowsDefaultCellStyle.BackColor = [System.Drawing.Color]::FromArgb(245, 245, 255)
$split.Panel2.Controls.Add($grid)

# ── Funkce: spustit dotaz ─────────────────────────────────────────────────────
$script:LastData = $null

function Run-Query {
    $sql = $txtSql.Text.Trim()
    if ([string]::IsNullOrEmpty($sql)) { return }

    $lblStatus.Text = "Probiha dotaz..."
    $grid.DataSource = $null
    [System.Windows.Forms.Application]::DoEvents()

    try {
        $conn = New-Object Oracle.ManagedDataAccess.Client.OracleConnection($ConnStr)
        $conn.Open()

        $cmd = New-Object Oracle.ManagedDataAccess.Client.OracleCommand($sql, $conn)
        $cmd.CommandTimeout = 120

        $adapter = New-Object Oracle.ManagedDataAccess.Client.OracleDataAdapter($cmd)
        $table   = New-Object System.Data.DataTable
        $rows    = $adapter.Fill($table)

        $conn.Close()

        $script:LastData = $table
        $grid.DataSource = $table
        $grid.AutoResizeColumns([System.Windows.Forms.DataGridViewAutoSizeColumnsMode]::DisplayedCells)

        $lblStatus.Text = "Nalezeno radku: $rows  |  Sloupcu: $($table.Columns.Count)"

    } catch {
        $lblStatus.Text = "CHYBA: $_"
        [System.Windows.Forms.MessageBox]::Show(
            $_.ToString(), "Chyba dotazu",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    } finally {
        if ($conn -and $conn.State -eq 'Open') { $conn.Close() }
    }
}

# ── Funkce: export CSV ────────────────────────────────────────────────────────
function Export-Csv {
    if ($null -eq $script:LastData -or $script:LastData.Rows.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("Zadna data k exportu.") | Out-Null
        return
    }
    $dlg = New-Object System.Windows.Forms.SaveFileDialog
    $dlg.Filter   = "CSV soubory (*.csv)|*.csv"
    $dlg.FileName = "oracle_export.csv"
    if ($dlg.ShowDialog() -ne "OK") { return }

    $lines = @()
    $cols  = $script:LastData.Columns | ForEach-Object { $_.ColumnName }
    $lines += ($cols -join ";")
    foreach ($row in $script:LastData.Rows) {
        $vals = $cols | ForEach-Object { '"' + "$($row[$_])".Replace('"','""') + '"' }
        $lines += ($vals -join ";")
    }
    $lines | Out-File $dlg.FileName -Encoding UTF8
    $lblStatus.Text = "Exportovano: $($dlg.FileName)"
}

# ── Eventy ────────────────────────────────────────────────────────────────────
$btnRun.Add_Click({ Run-Query })
$btnClear.Add_Click({ $txtSql.Clear(); $txtSql.Focus() })
$btnExport.Add_Click({ Export-Csv })

$form.Add_KeyDown({
    param($s, $e)
    if ($e.KeyCode -eq "F5") { Run-Query; $e.Handled = $true }
})

$form.KeyPreview = $true

# ── Spustit ───────────────────────────────────────────────────────────────────
$form.Add_Shown({ $txtSql.Focus() })
[System.Windows.Forms.Application]::Run($form)
