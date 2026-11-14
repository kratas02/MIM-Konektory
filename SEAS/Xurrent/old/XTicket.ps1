. C:\Projects\SE\Xurrent\old\SQLFunctions.ps1
. C:\Projects\SE\Xurrent\old\XurrentFunctions.ps1

. C:\Projects\SE\Xurrent\old\eidma.ps1



$logfolder = "E:\Logs\Xurrent"
$logfile = "$($logfolder)\$((Get-Date).ToString("yyyyMMdd"))_Update.txt" 


function Log($text,$debug = $false)
{
    write-host $text
    "$((Get-Date).ToString("HH:mm:ss")): $text" | Out-File $logfile -Append

    if($debug){Debug -text $text}
}
$authtoken = Get-AuthorizationHeader
$token = $authtoken.access_token



$DateStr =  (Get-Date).ToString("yyyyMMdd")
$logPath = "$logfolder\$($DateStr)log.txt"
$StartDate = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")

Function Log($Text)
{
    Write-Host $Text
    $Text| Out-File $logPath -Append
}


Log -Text "---------------------------- ZACATEK $StartDate ----------------------------"



$IDMTicketsResponse = GetAllObjectsFromEIDM -url "$EIDMURL/api/mimsync/getSyncFullData/33"
$IDMTickets = $IDMTicketsResponse.objects
#$IDMTickets = $IDMTickets | where {$_.attributes.ticketNumber -notmatch "^\d+$"}

$IDMTickets = $IDMTickets | where {$_.attributes.isSolved -ne $true}


$IDMUsersResponse = GetAllObjectsFromEIDM -url "$EIDMURL/api/mimsync/getSyncFullData/3"
$IDMUsers = $IDMUsersResponse.objects


$groupsURL = "$EIDMURL/api/mimsync/getSyncFullData/6"
$IDMRolesResponse = GetAllObjectsFromEIDM -url $groupsURL
$IDMRoles = $IDMRolesResponse.objects


$IDMAppsResponse = GetAllObjectsFromEIDM -url "$EIDMURL/api/mimsync/getSyncFullData/7"
$IDMApps = $IDMAppsResponse.objects



$uniqueRoles =  $IDMTickets.Attributes.roleRequested| Sort-Object | Get-Unique -AsString

$NoAttachementRoles = @()
$NoAttachementRoles += "VPN_EXT_ZAM"
$NoAttachementRoles += "VPN_EXT_ADMIN"
$NoAttachementRoles += "KEP INT"
$NoAttachementRoles += "KEP OVM"
$NoAttachementRoles += "KEP eOVM" 


$maxRequestForNoAttachementRoles = 3

#$uniqueRoles = $null
foreach($uniqueRole in $uniqueRoles)
{

    $requestObjects = [object[]]($IDMTickets | where {$_.Attributes.roleRequested -eq $uniqueRole})

 
    #$MIMrole = GetMIMObject -filter "/Group[ObjectID='$($role)']"
    $IDMRole = $IDMRoles | Where {$_.idmObjectID -eq $uniqueRole}
    if($IDMRole -eq $null) 
    {
        SetTicketResult -requests $requestObjects -ticketNr $null -errDescription "Rola nie je nájdená"
        Log -Text "Nenalezena role"
        continue
    }

    if($IDMRole.Attributes.application -eq $null)
    {
        Log -Text "Role $($IDMRole.Techname) nema nastavenou aplikaci"
        SetTicketResult -requests $requestObjects -ticketNr $null -errDescription "Rola nemá nastavenú aplikáciu"
        continue
    }

    $IDMApp = $IDMApps | Where {$_.idmObjectID -eq $IDMRole.Attributes.application}

    if($IDMApp -eq $null)
    {
        Log -Text "Nenalezena aplikace pro roli $($IDMRole.Techname)"
        SetTicketResult -requests $requestObjects -ticketNr $null -errDescription "Nenájdená aplikácia pre rolu $($IDMRole.Techname)"

        continue
    }


    if($IDMRole.Attributes.itsmTicket -ne $true -or $IDMRole.Attributes.itsmCode -eq $null)
    {
        Log -Text "Pre rolu $($IDMRole.Techname) neni nastavena resitelska skupina"
        SetTicketResult -requests $requestObjects -ticketNr $null -errDescription "Pro roli $($IDMRole.Techname) nie je nastavená resitelska skupina"
        continue

    }

    #$IDMTickets[0].Attributes.requestedFor


    CreateTicket -PSRequests $requestObjects -IDMRole $IDMRole -IDMApp $IDMApp
    
}


<#
$requestsToCheck = getTicketsToCheck
$ticketNumbers = $requestsToCheck | Sort-Object | Select-Object ticketNr | Get-Unique -AsString

foreach($ticketNr in $ticketNumbers.ticketNr)
{

    $requests = $requestsToCheck | where {$_.ticketNr -eq $ticketNr}
    $ticketState = [System.Array](CheckTicketState -ticketNr $ticketNr -PSRequests $requests)

       
}
#>

#UpdateKyBObjects -isDelta $true

Log -Text "---------------------------- KONEC $((Get-Date).ToString("yyyy-MM-dd HH:mm:ss")) ----------------------------"

