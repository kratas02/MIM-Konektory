$scriptName = $MyInvocation.MyCommand.Name
. C:\Projects\SE\Xurrent\Shared\logging.ps1


. C:\Projects\SE\Xurrent\Shared\sql.ps1
. C:\Projects\SE\Xurrent\old\XurrentFunctions_new.ps1

. C:\Projects\SE\Xurrent\Shared\eidma.ps1
$logfolder = "E:\Logs\Xurrent"
$logfile = "$($logfolder)\$((Get-Date).ToString("yyyyMMdd"))_Update.txt" 


function Log($text,$debug = $false)
{
    write-host $text
    "$((Get-Date).ToString("HH:mm:ss")): $text" | Out-File $logfile -Append

    if($debug){Debug -text $text}
}

$authtoken = $null
$error.Clear()

$authtoken = Get-AuthorizationHeader
$token = $authtoken.access_token

if($authtoken -eq $null)
{
    Write-Log -Message "Chyba Nacitani tiketu z EIDM" -source "EIDM" -Level "Fatal" -ex $error[0].exception.message
    return
}


$DateStr =  (Get-Date).ToString("yyyyMMdd")
$logPath = "$logfolder\$($DateStr)log.txt"
$StartDate = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")

Function Log($Text)
{
    Write-Host $Text
    $Text| Out-File $logPath -Append
}


Log -Text "---------------------------- ZACATEK $StartDate ----------------------------"


$error.Clear()
Write-Log -Message "Nacitani tiketu z EIDM" -source "EIDM"
$cas = Measure-Command{
    $IDMTicketsResponse = GetAllObjectsFromEIDM -url "$EIDMURL/api/mimsync/getSyncFullData/33"
    $IDMTickets = $IDMTicketsResponse.objects
    $IDMTickets = $IDMTickets | where {$_.attributes.ticketNumber -notmatch "^\d+$"}
    $IDMTickets = $IDMTickets | where {$_.Attributes.json -ne $null}
    $IDMTickets = $IDMTickets | where {$_.attributes.isSolved -ne $true}
    #$IDMTickets = $IDMTickets | where {$_.attributes.solvedText -ne "Error"}
    $IDMTickets = $IDMTickets | where {$_.IsArchived -ne "true"}
}

if($error.Count -gt 0 )
{
    Write-Log -Message "Chyba pri nacitani tiketu z portalu" -source "EIDM" -Level "Fatal" -ex $error[0].exception.message
    return
}
Write-Log -Message "Nacteno $($IDMTickets.Count) tiketu" -source "EIDM" -duration $cas.TotalMilliseconds

$templatesToProcess = @(
"new-identity-externalUserTestWithParentTemplate",
"new-identity-externalAdminRISTemplate",
"new-identity-userIdentityRISTemplate",
"new-identity-ext-adm-predecessor"
)

$priviledgeSYWIND = @(
"SY-RIS-008",
"SY-RIS-015",
"SY-WIND-021",
"SY-WIND-022",
"SY-RIS-007",
"SY-RIS-009",
"SY-RIS-010",
"SY-RIS-014",
"SY-RIS-016"
)
$nonPriviledgeSYWIND = @("SY-RIS-001","SY-RIS-020")


$allowedActions = @(
"create",
"add",
"update",
"change",
"terminate",
"deactivate",
"activate"
)

function GetTicketProperties($IDMTicket, $IDMUser, $manager, $IDMTicketJson)
{

        $IDMTicketJson = $IDMTicket.Attributes.json | ConvertFrom-Json

        $TicketDetails = New-Object PSObject
        $TicketDetails | Add-Member NoteProperty "IDMTicket" $IDMTicket
        $TicketDetails | Add-Member NoteProperty "template_id" $null
        $TicketDetails | Add-Member NoteProperty "typ_uctu" $null
        $TicketDetails | Add-Member NoteProperty "isPriviledge" $null
        $TicketDetails | Add-Member NoteProperty "isAdd" $false
        $TicketDetails | Add-Member NoteProperty "blockingAction" $null
        $TicketDetails | Add-Member NoteProperty "title" $null
        $TicketDetails | Add-Member NoteProperty "category" "rfc"
        $TicketDetails | Add-Member NoteProperty "service_instance_id" "950352"
        $TicketDetails | Add-Member NoteProperty "custom_fields" (New-Object PSObject)
        $TicketDetails | Add-Member NoteProperty "TicketTechName" $IDMTicket.TechName

        $TicketDetails.custom_fields | Add-Member NoteProperty "meno" $null
        $TicketDetails.custom_fields | Add-Member NoteProperty "priezvisko" $null
        $TicketDetails.custom_fields | Add-Member NoteProperty "osobne_cislo" $null
        $TicketDetails.custom_fields | Add-Member NoteProperty "manager" $null
        $TicketDetails.custom_fields | Add-Member NoteProperty "Employee_ID" $null
        $TicketDetails.custom_fields | Add-Member NoteProperty "Display_name" $null
        $TicketDetails.custom_fields | Add-Member NoteProperty "sy_wind" $null
        $TicketDetails.custom_fields | Add-Member NoteProperty "stav_uctu" $null
        #$TicketDetails.custom_fields | Add-Member NoteProperty "dovod" $null


        if($IDMTicketJson -ne $null -and $IDMTicketJson.userId -ne $null)
        {
            if($IDMUser.parentObject -ne $null)
            {
                $IDMparent = $null
                $IDMparent = GetIDMUser -IdmObjectId $IDMUser.parentObject
            }

            $TicketDetails.custom_fields.meno = ($IDMUser.firstName, $IDMparent.firstName -ne $null)[0]
            $TicketDetails.custom_fields.priezvisko = ($IDMUser.lastName, $IDMparent.lastName -ne $null)[0]
            $TicketDetails.custom_fields.osobne_cislo = ($IDMUser.employeeID, $IDMparent.employeeID -ne $null)[0]
            $TicketDetails.custom_fields.Employee_ID = ($IDMUser.userPrincipalName.Split("@")[0])
            $TicketDetails.custom_fields.Display_name = $IDMUser.name
            $TicketDetails.custom_fields.sy_wind =  ($IDMUser.syWInd, $IDMUser.syRis -ne $null)[0]
            $TicketDetails.custom_fields.stav_uctu = $IDMUser.computedAccountStatus
            #$TicketDetails.custom_fields | Add-Member NoteProperty "cislo_zmluvy" $null
            #$TicketDetails.custom_fields.dovod = 



            $managerID = ($IDMUser.managerObjectId, $IDMparent.managerObjectId -ne $null)[0]
            if($managerID -ne $null)
            {
                $IDMmanager = GetIDMUser -IdmObjectId $managerID
                if($IDMmanager -ne $null)
                {
                    $TicketDetails.custom_fields.manager = "$($IDMmanager.name)($($IDMmanager.employeeID))"
                }
            }

            $organizationExternalID = ($IDMUser.organizationExternal, $IDMparent.organizationExternal -ne $null)[0]
            if($organizationExternalID -ne $null)
            {
                $IDMorganizationExternal = GetIDMUser -IdmObjectId $organizationExternalID
                if($IDMorganizationExternal -ne $null)
                {
                    #$TicketDetails.custom_fields.cislo_zmluvy = 
                    $TicketDetails.custom_fields | Add-Member NoteProperty "cislo_zmluvy" $IDMorganizationExternal.name

                    
                }
            }
            else
            {
                $dep = ""
                if( $IDMUser.department -ne $null)
                {
                    $dep = $IDMUser.department + " "
                }
                if( $IDMUser.departmentCode -ne $null)
                {
                   
                    $dep = $dep + $IDMUser.departmentCode
                }
                if($dep -ne "")
                {
                    $TicketDetails.custom_fields | Add-Member NoteProperty "cislo_zmluvy" $dep
                }
            }




        }





        $sywind= $IDMUser.syRis
        if($sywind -eq $null)
        {
            $sywind= $IDMUser.syWind
        }

        if($sywind -eq $null)
        {
            return @{isError=$true;status="Nenalezen SYWIND / SYRIS: $sywind"}
        }
        $isPriviledge = $null

        if($priviledgeSYWIND.Contains($sywind)){$isPriviledge = $true}
        if($nonPriviledgeSYWIND.Contains($sywind)){$isPriviledge = $false}

        if($isPriviledge -eq $null)
        {
            return @{isError=$true;status="Neocekavany SYWIND: $sywind"}
        }

        $TicketDetails.isPriviledge = ($IDMTicketJson.privileged -eq "True")


        if($IDMTicketJson.action -eq "add" -or $IDMTicketJson.action -eq "create")
        {
            $TicketDetails.isAdd = $true
        }
        
        if($IDMTicketJson.action -eq "activate"){$TicketDetails.blockingAction = "odblokovanie"}
        if($IDMTicketJson.action -eq "deactivate"){$TicketDetails.blockingAction = "blokovanie"}
        if($IDMTicketJson.action -eq "terminate"){$TicketDetails.blockingAction = "ukončenie"}

        if($TicketDetails.custom_fields.sy_wind -like "*RIS*")
        {
            $TicketDetails.typ_uctu = "RIS"
        }
        else
        {
            $TicketDetails.typ_uctu = "ENTRA"
        }

        $action = $null
        if($TicketDetails.isAdd)
        {
            $action = "vytvorenie"
        }
        else
        {
            $action = "zmenu"
        }

        #blockingAction

        if($TicketDetails.isPriviledge)
        {
        

            #Vytvorenie /zmena identity  privilegovaný  (rozlišovať vytvorenie alebo zmenu rozlišovať bude IDM portál)
            #"Žiadosť o vytvorenie / zmena / administrátorského konta (RIS alebo ENTRA podľa SY- )"
            if($TicketDetails.blockingAction)
            {
                # Blokovanie/aktivovanie identity privilegovaný (rozlišovať blokovanie alebo odblokovanie rozlišovať bude IDM portál)
                $TicketDetails.title = "Žiadosť o $($TicketDetails.blockingAction) administrátorského konta $($TicketDetails.typ_uctu) "
                $TicketDetails.template_id = "273265"
            }
            else
            {
                $TicketDetails.title = "Žiadosť o $action administrátorského konta $($TicketDetails.typ_uctu)"
                $TicketDetails.template_id = "273266"

            }

        }
        else
        {

            if($TicketDetails.blockingAction)
            {
                #Blokovanie/aktivovanie identity neprivilegovaný
                $TicketDetails.title = "Žiadosť o $($TicketDetails.blockingAction) konta RIS"
                $TicketDetails.template_id = "273267"
            }
            else
            {
                #Vytvorenie /zmena identity  neprivilegovaný
                $TicketDetails.title = "Žiadosť o $action konta  RIS"
                $TicketDetails.template_id = "273263"
            }


        }

      <#  
    $TicketDetails.custom_fields.meno = $converter.EscapeIt($TicketDetails.custom_fields.meno).Trim('"')
    $TicketDetails.custom_fields.priezvisko = $converter.EscapeIt($TicketDetails.custom_fields.priezvisko).Trim('"')
    $TicketDetails.custom_fields.Display_name = $converter.EscapeIt($TicketDetails.custom_fields.Display_name).Trim('"')
    $TicketDetails.custom_fields.stav_uctu = $converter.EscapeIt($TicketDetails.custom_fields.stav_uctu).Trim('"')
    $TicketDetails.custom_fields.manager = $converter.EscapeIt($TicketDetails.custom_fields.manager).Trim('"')
    #>
    return $TicketDetails
   

}


foreach($IDMTicket in $IDMTickets)
{
    $IDMUser = $null
    $error.Clear()
    $ticketType=$null


    if($IDMTicket.Attributes.json -eq $null)
    {
        continue
    }

    $ticketType = $IDMTicket.Attributes.json | ConvertFrom-Json

    if(!$allowedActions.Contains($ticketType.action))
    {
        continue
    }



    $IDMTicketJson = $IDMTicket.Attributes.json | ConvertFrom-Json
    
    $IDMTicketJson.UserId = $IDMTicketJson.url.Substring(20)

    if($error.Count -gt 0)
    {
        write-log -Message "Nelze nacist detail tiketu" -Level "Error" -ex $error[0].exception.message -source "Xurrent" -detail $IDMTicket.Attributes.json
        
        UpdateIDMTicket -IDMTicket $IDMTicket -err "Nelze nacist detail tiketu: $($error[0].exception.message)" -ticketNr $null -isSolved $false -solvedText $null
        continue
    }


    if($ticketType.type -ne "cast")
    {
        $IDMUser = $null
        $IDMUser = GetIDMUser -IdmObjectId $IDMTicketJson.UserId 
        if($IDMUser -eq $null)
        {
            write-log -Message "Nelze najit uzivatele $($IDMTicketJson.UserId)" -Level "Error" -source "Xurrent" -detail $IDMTicket.Attributes.json
            UpdateIDMTicket -IDMTicket $IDMTicket -err "Nelze najit uzivatele $($IDMTicketJson.UserId )" -ticketNr $null -isSolved $false -solvedText "Error"
            continue
        }
        $TicketDetail = $null
        $TicketDetail = GetTicketProperties -IDMTicket $IDMTicket -IDMUser $IDMUser -IDMTicketJson $IDMTicketJson
        if($TicketDetail.isError)
        {
            write-log -Message "$($TicketDetail.status)" -Level "Error" -source "Xurrent" -detail $IDMTicket.Attributes.json
            UpdateIDMTicket -IDMTicket $IDMTicket -err "$($TicketDetail.status)" -ticketNr $null -isSolved $false -solvedText "Error"
            continue
        }
        CreateWFTicket -TicketDetail $TicketDetail

    }
    else
    {
        #write-log -Message "Neznamy tiket: $templateName" -Level "Error" -source "Xurrent" -detail $IDMTicket.Attributes.json
        #UpdateIDMTicket -IDMTicket $IDMTicket -err "Neznamy tiket: $templateName" -ticketNr $null -isSolved $false -solvedText $null

        continue
    }


}


#$EntraTickets = $IDMTickets | where {$_.ActForApprovalTechName -like "*new-identity*" -and $_.ActForApprovalTechName -notlike "*RIS*" }




#$IDMUsersResponse = GetAllObjectsFromEIDM -url "$EIDMURL/api/mimsync/getSyncFullData/3"
#$IDMUsers = $IDMUsersResponse.objects


#$groupsURL = "$EIDMURL/api/mimsync/getSyncFullData/6"
#$IDMRolesResponse = GetAllObjectsFromEIDM -url $groupsURL
#$IDMRoles = $IDMRolesResponse.objects


#$IDMAppsResponse = GetAllObjectsFromEIDM -url "$EIDMURL/api/mimsync/getSyncFullData/7"
#$IDMApps = $IDMAppsResponse.objects




#region role-cast


<#
$RoleCastTickets = $IDMTickets | where {$_.ActForApprovalTechName -eq "role-cast"}
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

    $requestObjects = [object[]]($RoleCastTickets | where {$_.Attributes.roleRequested -eq $uniqueRole})

 
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
#>
#endregion



Log -Text "---------------------------- KONEC $((Get-Date).ToString("yyyy-MM-dd HH:mm:ss")) ----------------------------"

