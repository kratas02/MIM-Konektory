# Najde nejnovejsi SAP_TO_MIM_OrgData_yyyyMMddHHmmss.txt a jeho obsah
# zapise do orgdata.txt. Cilovy soubor se nemaze, prepise se jen obsah.

$ErrorActionPreference = 'Stop'

$dir = 'C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\MaData\SAP HANA'
$destPath = Join-Path $dir 'orgdata.txt'
$namePattern = '^SAP_TO_MIM_OrgData_(\d{14})\.txt$'

if (-not (Test-Path -LiteralPath $dir)) {
    throw "Adresar neexistuje: $dir"
}

$latest = Get-ChildItem -LiteralPath $dir -File |
    Where-Object { $_.Name -match $namePattern } |
    Sort-Object { [int64]($_.Name -replace '^SAP_TO_MIM_OrgData_(\d{14})\.txt$', '$1') } -Descending |
    Select-Object -First 1

if (-not $latest) {
    throw "V adresari neni zadny soubor SAP_TO_MIM_OrgData_yyyyMMddHHmmss.txt"
}

$bytes = [System.IO.File]::ReadAllBytes($latest.FullName)
if ($bytes.Length -eq 0) {
    throw "Nejnovejsi soubor je prazdny, orgdata.txt se nemeni: $($latest.Name)"
}

$stream = [System.IO.File]::Open(
    $destPath,
    [System.IO.FileMode]::OpenOrCreate,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::Read)
try {
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.SetLength($bytes.Length)
    $stream.Flush($true)
}
finally {
    $stream.Dispose()
}

$written = Get-Item -LiteralPath $destPath
if (-not $written -or $written.Length -eq 0) {
    throw "orgdata.txt chybi nebo je prazdny"
}

Write-Output "Zapsano $($written.Length) bajtu z $($latest.Name) do $($written.FullName)"
