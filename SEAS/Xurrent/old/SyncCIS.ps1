. C:\Batch\xurrent\MIMFunctions.ps1
. C:\Batch\xurrent\XurrentFunctions.ps1

$appCodes = "S_EMO34","Q_EMO34", "cfs"


$logfolder = "C:\batch\xurrent\Log"
$logfile = "$($logfolder)\$((Get-Date).ToString("yyyyMMdd"))_xurrent.txt" 

$debugFile = "$($logfolder)\$((Get-Date).DayOfWeek)_debug.txt"
$NextdebugFile = "$($logfolder)\$((Get-Date).AddDays(1).DayOfWeek)_debug.txt"

if([System.IO.File]::Exists($NextdebugFile))
{
    Remove-Item $NextdebugFile
}



function Log($text,$debug = $true)
{
    write-host $text
    "$((Get-Date).ToString("HH:mm:ss")): $text" | Out-File $logfile -Append
    if($debug){Debug -text $text}
}

function Debug($text)
{
    "$((Get-Date).ToString("HH:mm:ss")): $text" | Out-File $debugFile -Append

}

#$MIMApps = getApps -appCodes $appCodes



log -text "------------- spuštění skriptu: nalezeno $($MIMApps.Count) aplikací-------------"

foreach($MIMApp in $MIMApps)
{
    $MIMowner = GetOwnerMail -owners $MIMApp.Owner
    $MIMprocessOwner = GetOwnerMail -owners $MIMApp.seasprocessOwner
    $MIMictGarant = GetOwnerMail -owners $MIMApp.seasictGarant

    $XOwner = GetXUserByMail -mail $MIMowner
    $XprocessOwner = GetXUserByMail -mail $MIMprocessOwner
    $XictGarant = GetXUserByMail -mail $MIMictGarant

   
    $product_id = 543232
    if($MIMApp.seasapplicationCode -eq "S_EMO34" -or $MIMApp.seasapplicationCode -eq "Q_EMO34")
    {
        $product_id = 547031
    }

    if($MIMApp.seasexternalID -eq $null)
    {

    #kod aplikace=cfs => product_id=543232
    #kod aplikace=S_EMO34 || Q_EMO34 => product_id=547031


        $response= CreateCIS -name $($MIMApp.DisplayName) -XictGarant $XictGarant -XOwner $XOwner -XprocessOwner $XprocessOwner -product_id $product_id
        if($response -ne $null)
        {
            UpdateAPP -MIMApp $MIMApp -XResponse $response
        }
    }
    else
    {
        $response= UpdateCIS -name $($MIMApp.DisplayName) -XictGarant $XictGarant -XOwner $XOwner -XprocessOwner $XprocessOwner -product_id $product_id -id $($MIMApp.seasexternalID)
    }
}

#10764624 - problem  diakritika