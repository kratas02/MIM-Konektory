[String]$BasePath = 'C:\Batch'
[String]$MailLogFile = "$BasePath\maillog.txt"
[String]$TmpPath = "C:\Batch\Vema\Input"
$VEMA_FILE_NAME = "VemLEAS"
$DATE_FORMAT = "yyyyMMdd"
#$INPUTPATH = 'C:\Batch\Vema\Input'
$INPUTPATH = '\\10.248.201.15\Vema_Prod\Input'
$PATH = "$BasePath\Vema"
[String]$MailLogFile = "$BasePath\maillog.txt"
$PROCESSED = "$PATH\Processed vems"
$last_person_count_path = "$PATH\last_person_count.txt"
# Rucne zadany prah. Skript ho neprepocitava z historie ani z velikosti posledni zmeny.
$MAXIMUM_PERSON_DIFFERENCE = 0.3
$VEMA_FILENAME = "C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\MaData\VEMA\VemLEAS.txt"
$srtutv_file = "SR_TUTV.csv"
$14Holiday_file = "VEMA_14Holiday_DAY_VEMA_SQL.csv"

$first_run = $false

function ConvertToDate($strDate)
{
    $items = $strDate.Split(".")
    return "$($items[2])-$($items[1])-$($items[0])" 
}

Function LogWrite
{
param
(
    $logstring,
    [switch] $err,
    [switch] $warn
)
    #$msg = "$((Get-Date).ToString("dd.MM.yyyy HH:mm")) $logstring"
    # Vteriny odlisi pad od dokonceni ve stejne minute.
    $msg = "$((Get-Date).ToString("dd.MM.yyyy HH:mm:ss")) $logstring"
    $msg | Out-File "$BasePath\$((Get-Date).ToString("yyyyMMdd"))synclog.txt" -Append unicode
    
    if($err)
    {
        "<font color='red'>$msg</font><br />" | Out-File $MailLogFile -Append unicode
    }
    elseif($warn)
    {
        "<font color='orange'>$msg</font><br />" | Out-File $MailLogFile -Append unicode
    }
    else
    {
        "<font color='black'>$msg</font><br />" | Out-File $MailLogFile -Append unicode
    }
}

# Konec behu vcetne trvani. Volani je pred kazdym exit, i kdyz se drop soubor nemeni.
function LogFinish([string]$reason)
{
    $elapsedSeconds = [int]((Get-Date) - $script:RunStarted).TotalSeconds
    LogWrite "Run finished pid=$PID reason=$reason elapsedSeconds=$elapsedSeconds"
}

# Zacatek behu. Pid spoji radek synclogu s procesem powershell.exe v Event Logu.
$script:RunStarted = Get-Date
LogWrite "Run started pid=$PID"

if (-not [System.IO.File]::Exists($last_person_count_path))
{
    Out-File -FilePath $last_person_count_path -InputObject 0 -Encoding UTF8
}
$last_person_count = Get-Content -Path $last_person_count_path

$files = Get-ChildItem -Path $INPUTPATH
$latest_file_date = [datetime]::parseexact("00010101", $DATE_FORMAT, $null)
$latest_file = ""


foreach ($file in $files)
{
    if (($file -Match $VEMA_FILE_NAME) -and ($file -like '*.txt'))
    {
        $file_date = [datetime]::parseexact(($file -split '_')[0], $DATE_FORMAT, $null)
        if ($file_date -gt $latest_file_date)
        {
            $latest_file_date = $file_date
            if ($latest_file.Length -gt 0)
            {
                #LogWrite "$($latest_file.Name) moved to processed folder"
                #Move-Item -Path "$PATH\$latest_file" -Destination "$PROCESSED\skipped_$latest_file"
            }
            $latest_file = $file
        }
        else
        {
            #LogWrite "$file moved to processed folder"
            #Move-Item -Path "$PATH\$file" -Destination "$PROCESSED\skipped_$file"
        }
    }
}
if ($latest_file.Length -eq 0)
{
    LogWrite "No new people"
    LogFinish "no-input"
    exit
}

$file_path = "$INPUTPATH\$latest_file"
# Nazev a velikost vybraneho vstupu, jeste pred ctenim obsahu.
LogWrite "Input file name=$($latest_file.Name) inputBytes=$($latest_file.Length)"
$header = "$PATH\header.txt"


$content = $($($(Get-Content $file_path -Encoding Default -Raw) -split "<MD>")[1] -split "</MD>")[0]
# Delka textu mezi znackami MD. Kratky obsah zivy soubor pro MIM nemeni.
LogWrite "MD content length=$($content.Length) file=$($latest_file.Name)"
if ($content.length -lt 10) {
    LogWrite "MD content shorter than 10 characters"
    LogFinish "content-too-short"
    exit 0
}
$header = Get-Content $header -Encoding UTF8 -Raw
$content = $header + $content

$tmpfile =  "$TmpPath\$($latest_file.Name).tmp"
[IO.File]::WriteAllText($tmpfile, $content)





$14HolidayCSV = Import-Csv -Delimiter ";" -Path "$INPUTPATH\$14Holiday_file" -Encoding Default
$csv2 = Import-Csv -Delimiter "`t" -Path "$tmpfile" -Encoding UTF8
$srtutv = Import-Csv -Delimiter "," -Path "$INPUTPATH\$srtutv_file" -Encoding Default
# Pocty radku hned po importu. $null neni 1, protoze @($null) ma v PowerShellu 5.1 Count 1.
$vemaRowCount = 0
$holidayRowCount = 0
$srtutvRowCount = 0
if ($null -ne $csv2) { $vemaRowCount = @($csv2).Count }
if ($null -ne $14HolidayCSV) { $holidayRowCount = @($14HolidayCSV).Count }
if ($null -ne $srtutv) { $srtutvRowCount = @($srtutv).Count }
LogWrite "Imported rows vema=$vemaRowCount holiday=$holidayRowCount srtutv=$srtutvRowCount"

# Index svatku podle PersonalNumber. Nahrazuje Where-Object uvnitr cyklu pres kazdeho zamestnance.
$holidayByPerson = @{}
foreach ($holidayRow in $14HolidayCSV) {
    $personalNumber = [string]$holidayRow.PersonalNumber
    if (-not $holidayByPerson.ContainsKey($personalNumber)) {
        # Pri vice radcich se pouzije prvni. Puvodni Where-Object by vratil vsechny a ParseExact by na poli spadl.
        $holidayByPerson[$personalNumber] = $holidayRow
    }
}

# Index utvaru podle klice [int].ToString(), stejneho jako puvodni Where-Object.
$srtutvByUtvar = @{}
foreach ($utvarRow in $srtutv) {
    $utvarKey = $null
    try {
        $utvarKey = ([int]$utvarRow.utvar_cislo).ToString()
    }
    catch {
        # Neciselny utvar_cislo by puvodni pretypovani [int] stejne odmitlo.
        continue
    }
    if (-not $srtutvByUtvar.ContainsKey($utvarKey)) {
        # Pri duplicitnim cisle utvaru zustava prvni radek.
        $srtutvByUtvar[$utvarKey] = $utvarRow
    }
}

#$csv2 = $csv2 | Select-Object *,@{Name='Manager';Expression={''}}
#$csv2 = $csv2 | Select-Object *,@{Name='utvar_nazev';Expression={''}}
#$csv2 = $csv2 | Select-Object *,@{Name='isValid';Expression={$true}}
#$csv2 = $csv2 | Select-Object *,@{Name='dtValidFrom';Expression={$null}}
#$csv2 = $csv2 | Select-Object *,@{Name='hasManager';Expression={$false}}
#$csv2 = $csv2 | Select-Object *,@{Name='14DateFrom';Expression={''}}
#$csv2 = $csv2 | Select-Object *,@{Name='14DateTo';Expression={''}}
#$csv2 = $csv2 | Select-Object *,@{Name='14Status';Expression={$null}}
# Jedna kopie objektu misto osmi. Nazvy sloupcu a vychozi hodnoty jsou stejne jako u puvodnich Select-Object.
$csv2 = $csv2 | Select-Object *, @{Name='Manager';Expression={''}}, @{Name='utvar_nazev';Expression={''}}, @{Name='isValid';Expression={$true}}, @{Name='dtValidFrom';Expression={$null}}, @{Name='hasManager';Expression={$false}}, @{Name='14DateFrom';Expression={''}}, @{Name='14DateTo';Expression={''}}, @{Name='14Status';Expression={$null}}




#$csv = $csv | Select-Object *,@{Name='';Expression={''}}

$csv =  New-Object System.Collections.ArrayList
# Jeden zaznam na Employee ID. Nahrazuje Where-Object pres rostouci seznam v kazde iteraci.
$personsById = @{}


foreach($user in $csv2)
{

if($user.'Employee ID' -eq "17702031")
{
    $a = 1
}
    

    if($user.'Type of working contract' -eq "708")
    {
        $user.'End Out of Evidence'
        $user.'Beginning Out of Evidence' = $null
    }

    $found14Holiday = $null
    #$found14Holiday = $14HolidayCSV | where {$_.'PersonalNumber' -eq $user.'Employee ID'.Substring(4)} 
    # Stejne Substring(4) jako puvodni podminka, ale dotaz do hasmapy.
    $found14Holiday = $holidayByPerson[$user.'Employee ID'.Substring(4)]
    if($found14Holiday -ne $null)
    {
        $dtDateFrom =  [datetime]::parseexact($found14Holiday.DateFrom, 'dd.MM.yyyy', $null)
        $dtDateTo =  [datetime]::parseexact($found14Holiday.DateTo, 'dd.MM.yyyy', $null)
        $dtDateTo = $dtDateTo.AddHours(22)
        $dateNow = Get-Date

        if($dateNow -gt $dtDateFrom -and $dateNow -lt  $dtDateTo) 
        {
            $user.'14DateFrom' = "$(ConvertToDate($found14Holiday.DateFrom))T00:00:00.000";
            $user.'14DateTo' = "$(ConvertToDate($found14Holiday.DateTo))T23:59:00.000"
            $user.'14Status' = 4
        }
    }

    #$found = $csv | where {$_.'Employee ID' -eq $user.'Employee ID'} 
    # Nalezeny zaznam je ten, ktery uz v seznamu pro toto Employee ID je.
    $found = $personsById[$user.'Employee ID']

    $startDateTXT= $null
    $startDateTXT = $user.'Start Date (Action 49)'
    if($startDateTXT.length -eq 8)
    {
        try
        {
            $user.dtValidFrom =  [datetime]::parseexact($startDateTXT, 'yyyyMMdd', $null)
            
            if($user.dtValidFrom  -gt (Get-Date))
            {
                $user.isValid = $false  
            }
        }
        catch
        {
            $user.isValid = $false 
        }
    }

    if(![string]::IsNullOrEmpty($($user.'Leaving-Date (Action)')))
    {

        $dtValidTo = [datetime]::parseexact($user.'Leaving-Date (Action)', "yyyyMMdd",$null)
        if($dtValidTo -lt (Get-Date))
        {
            $user.isValid = $false 
        }
    }
    

    if(!$found)
    {
        $csv.Add($user)
        # Stejny objekt jako v seznamu, aby slo pozdeji porovnat referenci.
        $personsById[$user.'Employee ID'] = $user
    }
    else
    {
        if(($user.isValid -and $found.isValid -and $user.dtValidFrom -gt $found.dtValidFrom) -or $found.dtValidFrom -eq $null)
        {
            $csv.Remove($found)
            $csv.Add($user)
            # Seznam i mapa musi ukazovat na nove ponechany zaznam.
            $personsById[$user.'Employee ID'] = $user
        }
    }
}



$managers = $csv | Where-Object -Property "Record Type" -eq 2
$employers = $csv | Where-Object -Property "Record Type" -ne 2


foreach ($employee in $employers)
{
    #$manager = $srtutv | Where-Object -Property "utvar_cislo" -eq $([int]$($employee."External Organizational Unit Key")).ToString()
    # Klic utvaru se pocita stejne jako v puvodnim Where-Object.
    $manager = $srtutvByUtvar[$([int]$($employee."External Organizational Unit Key")).ToString()]
    $employee.utvar_nazev = $manager.utvar_nazev
    $utvar_manager = "1770$($manager.'utvar_manager')"
    if ($utvar_manager -eq $employee.'Employee ID')
    {
        $employee.'Record Type' = 2;
        #$manager = $srtutv | Where-Object -Property "utvar_cislo" -eq $([int]$($manager."utvar_nadrizena")).ToString()
        # Nadrizeny utvar se bere ze stejne hasmapy.
        $manager = $srtutvByUtvar[$([int]$($manager."utvar_nadrizena")).ToString()]
        if ($manager.length -eq 0)
        {
            $employee."Manager" = ""
        }
        else
        {
            $employee."Manager" = "1770$($manager.'utvar_manager')"
        }
    }
    else
    {
        $employee."Manager" = $utvar_manager
    }
    
        if($employee."Manager"){$employee.hasManager = $true}
}
$csv = $employers

# Pocet osob po slouceni Employee ID a po vyrazeni Record Type 2. To je pocet pro prah 30 %.
$invalidPersonCount = 0
foreach ($builtPerson in $csv) {
    if (-not $builtPerson.isValid) {
        $invalidPersonCount++
    }
}
$wrong_users = $csv | Where-Object -Property "Employee ID" -le 0

$person_count = ($csv | Select-Object).Length
# Stejny pocet, jaky vstupuje do prahu. Count na jednom objektu by byl prazdny.
LogWrite "Persons built count=$person_count invalid=$invalidPersonCount"

# Rozdil proti poslednimu prijatemu poctu. Prah je rucni konstanta MAXIMUM_PERSON_DIFFERENCE.
$personCountDelta = [System.Math]::Abs([double]$last_person_count - [double]$person_count)
$personCountLimit = [double]$person_count * $MAXIMUM_PERSON_DIFFERENCE
LogWrite "Person count check last=$last_person_count new=$person_count delta=$personCountDelta limit=$personCountLimit"

#not allowed to import, send a warning
if (-not $first_run -and $last_person_count -ne 0 -and [System.Math]::Abs($last_person_count - $person_count) -gt $person_count * $MAXIMUM_PERSON_DIFFERENCE)
{
    LogWrite "Number of changes is higher than allowed limit $MAXIMUM_PERSON_DIFFERENCE"
    # Zamitnuty beh drop soubor ani last_person_count.txt nemeni.
    LogWrite "Person count check rejected, drop file left unchanged"
    LogFinish "count-rejected"
    exit
}

# Kontrola prosla. Ulozeny pocet se prepisuje az po uspesne nahrade souboru pro MIM.
LogWrite "Person count check passed"
#LogWrite "Person count changed"
#Out-File -FilePath $last_person_count_path -InputObject $person_count -Encoding UTF8
$employers = $employers | Where-Object -Property "Employee group" -ne 'N'
#Copy-Item -Path $tmpfile -Destination $VEMA_FILENAME -Force
#LogWrite "$($latest_file.Name) copied to VEMA folder"

#$objduplicities = $employers | Group-Object -Property 'Employee ID' | where {$_.count -gt 1} 
#
#foreach($objduplicity in $objduplicities)
#{
#    $sorted = $objduplicity.Group | Sort-Object -Property 'Start Date (Action 49)'
#    for($i=0; $i -lt $sorted.Count-1; $i++)
#    {
#        $employers = $employers | Where {$_ -ne $sorted[$i]}
#    }
#
#}

# Deduplikace Employee ID bez opakovaneho skladani celeho pole. Zustava zaznam s nejvyssim Start Date (Action 49).
$bestByEmployeeId = @{}
$employeeIdHits = @{}
foreach ($employee in $employers) {
    $employeeId = [string]$employee.'Employee ID'
    if ($employeeIdHits.ContainsKey($employeeId)) {
        # Druhy a dalsi vyskyt stejneho Employee ID.
        $employeeIdHits[$employeeId] = $employeeIdHits[$employeeId] + 1
    }
    else {
        $employeeIdHits[$employeeId] = 1
    }

    $currentBest = $bestByEmployeeId[$employeeId]
    if (-not $currentBest) {
        $bestByEmployeeId[$employeeId] = $employee
    }
    else {
        $newStart = [string]$employee.'Start Date (Action 49)'
        $oldStart = [string]$currentBest.'Start Date (Action 49)'
        # Pri shode data vyhrava pozdejsi radek, stejne jako posledni prvek po vzestupnem Sort-Object.
        if ($newStart -ge $oldStart) {
            $bestByEmployeeId[$employeeId] = $employee
        }
    }
}
$duplicateEmployeeIdGroups = 0
foreach ($hitCount in $employeeIdHits.Values) {
    if ($hitCount -gt 1) {
        $duplicateEmployeeIdGroups++
    }
}
# Stav po filtru Employee group a pred fyzickym zapisem. Prazdny Where-Object je $null, ne jeden radek.
$employerCountBeforeDedup = 0
if ($null -ne $employers) { $employerCountBeforeDedup = @($employers).Count }
LogWrite "After Employee group filter rows=$employerCountBeforeDedup duplicateEmployeeIdGroups=$duplicateEmployeeIdGroups"

# Poradi ponechanych radku odpovida puvodnimu poli, ne poradi klicu hasmapy.
$employersKept = New-Object System.Collections.Generic.List[object]
foreach ($employee in $employers) {
    $employeeId = [string]$employee.'Employee ID'
    if ($bestByEmployeeId[$employeeId] -eq $employee) {
        $employersKept.Add($employee)
    }
}
$employers = $employersKept

# Velikost stavajiciho drop souboru, dokud je jeste puvodni verze cela.
$previousDropBytes = 0
if ([System.IO.File]::Exists($VEMA_FILENAME)) {
    $previousDropBytes = (Get-Item -LiteralPath $VEMA_FILENAME).Length
}
LogWrite "After dedup rows=$($employers.Count) existingDropBytes=$previousDropBytes"

# Prazdny vystup zivy soubor nenahradi. Last count zustava na poslednim prijatym poctu.
if ($employers.Count -lt 1) {
    LogWrite "No rows to write, drop file left unchanged" -err
    LogFinish "empty-output"
    exit 1
}

# Docasny soubor je mimo MaData a neni to txt, ktere by MIM mohl nacist.
$stagedFile = Join-Path $TmpPath 'VemLEAS.building'
LogWrite "Writing staging file path=$stagedFile rows=$($employers.Count)"
$employers | ConvertTo-Csv -Delimiter "`t" -NoTypeInformation | ForEach-Object { $_ -Replace '"', "" } | Out-File -FilePath $stagedFile -Encoding utf8
# Velikost docasneho souboru. Zivy VemLEAS.txt je v tuto chvili porad puvodni.
$stagedBytes = (Get-Item -LiteralPath $stagedFile).Length
LogWrite "Staging file ready path=$stagedFile bytes=$stagedBytes"
if ($stagedBytes -le 0) {
    LogWrite "Staging file empty, drop file left unchanged" -err
    LogFinish "empty-staging"
    exit 1
}

# Nahrada na stejnem svazku. Reader vidi bud cely puvodni, nebo cely novy soubor.
try {
    if ([System.IO.File]::Exists($VEMA_FILENAME)) {
        # NullString, ne $null: PowerShell by z $null udelal prazdnou cestu a Replace by selhal.
        [System.IO.File]::Replace($stagedFile, $VEMA_FILENAME, [NullString]::Value)
    }
    else {
        # Prvni beh, cilovy soubor jeste neexistuje.
        [System.IO.File]::Move($stagedFile, $VEMA_FILENAME)
    }
}
catch {
    LogWrite "Drop file replace failed: $($_.Exception.Message)" -err
    LogFinish "replace-failed"
    exit 1
}
$newDropBytes = (Get-Item -LiteralPath $VEMA_FILENAME).Length
LogWrite "Drop file replaced newBytes=$newDropBytes previousBytes=$previousDropBytes"

# Posun ulozeneho poctu az po dokoncene nahrade. Pri padu pred timto radkem se zakladna nemeni.
Out-File -FilePath $last_person_count_path -InputObject $person_count -Encoding UTF8
#$employers | ConvertTo-Csv -Delimiter "`t" -NoTypeInformation | % { $_ -Replace '"', ""} | Out-File -FilePath $VEMA_FILENAME -Encoding utf8
LogWrite "$latest_file moved to processed folder"
LogFinish "replaced"


#Move-Item -Path "$PATH\$latest_file" -Destination "$PROCESSED\$latest_file"
