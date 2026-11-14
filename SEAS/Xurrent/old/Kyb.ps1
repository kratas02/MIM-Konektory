. C:\Batch\xurrent\SQLFunctions.ps1
. C:\Batch\xurrent\XurrentFunctions.ps1

$DateStr =  (Get-Date).ToString("yyyyMMdd")
$logPath = "C:\Batch\xurrent\Log\$($DateStr)log.txt"
$StartDate = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")

Function Log($Text)
{
    Write-Host $Text
    $Text| Out-File $logPath -Append
}


Log -Text "---------------------------- ZACATEK KYB $StartDate ----------------------------"

UpdateKyBObjects -isDelta $true

Log -Text "---------------------------- KONEC KYB $((Get-Date).ToString("yyyy-MM-dd HH:mm:ss")) ----------------------------"

