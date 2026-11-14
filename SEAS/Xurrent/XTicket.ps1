# Zpracuje otevrene tikety z EasyIDM a zalozi odpovidajici zadosti v Xurrent.
# Identity (ucet: vytvoreni, zmena, blokovani) a role (prideleni, prodlouzeni, odebrani) se zpracovavaji zvlast.
#
# Parametry:
#   -UpdateKyB   Aktualizuje KyB ciselnik standardu z Xurrent do SQL (IDMSupport.X_Ciselnik_KyB).
#                Normalni zpracovani tiketu se v tomto rezimu neprovede.

param(
    [switch]$UpdateKyB,
    [switch]$Debug
)

$scriptName = $MyInvocation.MyCommand.Name
$isProd = $true
#region Konfigurace

$logFolder = "C:\Batch\Logs\Script"
$logFile = Join-Path $logFolder ("{0}_XTicket.log" -f (Get-Date -Format "yyyyMMdd"))

$xurrentAccount = "seas-it"
$xurrentProxy = "http://proxy.seas.sk:8080"
$xurrentCategory = "rfc"
$xurrentTokenSkewSeconds = 50

$eidmTypeTicket = 33
$eidmTypeUser = 3
$eidmTypeRole = 6
$eidmTypeApplication = 7

# Poradi znaku v URL identity, od ktereho zacina id uzivatele.
$userIdUrlStartIndex = 20

$allowedIdentityActions = @(
    "create",
    "add",
    "update",
    "change",
    "terminate",
    "deactivate",
    "activate"
)

$privilegedSyCodes = @(
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
$standardSyCodes = @(
    "SY-RIS-001",
    "SY-RIS-020"
)



if($isProd)
{
    $identityTemplatePrivilegedBlocking = "270788"
    $identityTemplatePrivilegedChange = "270789"
    $identityTemplateStandardBlocking = "270790"
    $identityTemplateStandardChange = "270787"
    $serviceInstanceId = "923491"
    $xurrentClientId = "clientid"
    $xurrentClientSecret = "clientsecret"
    $xurrentTokenUrl = "https://oauth.xurrent.com/token"
    $xurrentApiUrl = "https://api.xurrent.com/v1"

}
else
{
    $identityTemplatePrivilegedBlocking = "273265"
    $identityTemplatePrivilegedChange = "273266"
    $identityTemplateStandardBlocking = "273267"
    $identityTemplateStandardChange = "273263"
    $serviceInstanceId = "950352"
    $xurrentClientId = "aCms9haD4EC0U6io3hjkpOZ0ZJ9U2pvt5Y8nVVWS8vmKFtTn"
    $xurrentClientSecret = "a"
    $xurrentTokenUrl = "https://oauth.xurrent.qa/token"
    $xurrentApiUrl = "https://api.xurrent.qa/v1"

}




$blockingActionLabels = @{
    activate   = "odblokovanie"
    deactivate = "blokovanie"
    terminate  = "ukončenie"
}

if($isProd)
{
    $roleTemplateId = "217288"
}
else
{
    $roleTemplateId = "218724"
}
$roleActionProlong = "3301"
$roleActionRemove = "3302"
$maxRequestsWithoutAttachment = 3
$roleValidityHourOffset = 3
$applicationsWithoutAttachment = @(
    "VPN_EXT_ZAM",
    "VPN_EXT_ADMIN",
    "KEP INT",
    "KEP OVM",
    "KEP eOVM"
)

$jsonEscapeDll = "C:\Batch\xurrent\JsonEscape.dll"
$roleExcelTemplate = "C:\Batch\xurrent\hromadny_tiket_sablona.xlsx"
$roleExcelFolder = "C:\Batch\xurrent\Requests"
$excelSheetPassword = "741852"
$roleAttachmentDescription = "Žiadosť pre viacerých používateľov podľa priloženého súboru. Po spracovaní prístupu aplikačnej roly používateľovi doplňte do priloženého súboru výsledok Pridelené/Nepridelené/Odobraté. V prípade nepridelenia doplňte dôvod."

#region Konfigurace KyB
# Ciselnik standardu – samostatne SQL pripojeni na IDMSupport (mimsyncdb)
$kybConnectionString  = "Server=mimsyncdb;Database=IDMSupport;Integrated Security=True;"
$kybXurrentCollection = "custom_collection_elements?custom_collection=%c5%a1tandardy&fields=id,name,reference,description,created_at,updated_at,custom_fields"
#endregion

. C:\Batch\script\shared\logging.ps1

#endregion

#region Funkce

#region Logovani

function Write-ScriptLog {
    param(
        [string]$Message,
        [string]$Level = "Information",
        [string]$Source = "Xurrent",
        $Exception,
        [string]$Detail,
        $Duration
    )

    $params = @{
        Message = $Message
        Level   = $Level
        source  = $Source
        logFile = $logFile
    }
    if ($PSBoundParameters.ContainsKey("Exception")) { $params.ex = $Exception }
    if ($PSBoundParameters.ContainsKey("Detail")) { $params.detail = $Detail }
    if ($PSBoundParameters.ContainsKey("Duration")) { $params.duration = [int][Math]::Round($Duration) }
    Write-Log @params
}

function Get-RestErrorMessage {
    param($ErrorRecord)

    if ($ErrorRecord.ErrorDetails -and -not [string]::IsNullOrEmpty($ErrorRecord.ErrorDetails.Message))
    {
        return $ErrorRecord.ErrorDetails.Message
    }

    $response = $ErrorRecord.Exception.Response
    if ($null -ne $response)
    {
        try
        {
            $stream = $response.GetResponseStream()
            if ($null -ne $stream)
            {
                $reader = New-Object System.IO.StreamReader($stream)
                $body = $reader.ReadToEnd()
                $reader.Close()
                if (-not [string]::IsNullOrEmpty($body)) { return $body }
            }
        }
        catch { }
    }

    return $ErrorRecord.Exception.Message
}

#endregion

#region EIDM

function Get-EidmObjects {
    param(
        [int]$ObjectType,
        [string]$Description
    )

    $url = "$EIDMURL/api/mimsync/getSyncFullData/$ObjectType"
    Write-ScriptLog -Message "Nacitani $Description z EIDM" -Source "EIDM"
    $response = $null
    $elapsed = Measure-Command {
        $response = GetAllObjectsFromEIDM -url $url
    }

    if ($response.isError)
    {
        Write-ScriptLog -Message "Chyba pri nacitani $Description z portalu" -Source "EIDM" -Level "Fatal" -Exception $response.exception -Duration $elapsed.TotalMilliseconds
        return $null
    }

    if ($null -eq $response.objects)
    {
        $objects = @()
    }
    else
    {
        $objects = @($response.objects)
    }

    Write-ScriptLog -Message "Nacteno $($objects.Count) $Description" -Source "EIDM" -Duration $elapsed.TotalMilliseconds
    return ,$objects
}

function Update-IdmTicket {
    param(
        $Ticket,
        $ErrorText,
        $TicketNumber,
        $SolvedText,
        $IsSolved
    )

    addAttribute $Ticket.Attributes "ticketNumber"
    addAttribute $Ticket.Attributes "solvedText"
    addAttribute $Ticket.Attributes "error"
    addAttribute $Ticket.Attributes "isSolved"

    $Ticket.Attributes.ticketNumber = $TicketNumber
    $Ticket.Attributes.solvedText = $SolvedText
    $Ticket.Attributes.isSolved = $IsSolved
    $Ticket.Attributes.error = $ErrorText

    $json = "[$($Ticket | ConvertTo-Json -Compress)]"
    $result = ExportToPortal -json $json -objectType $eidmTypeTicket
    if ($result.isError)
    {
        Write-ScriptLog -Message "Chyba zapisu tiketu $($Ticket.TechName) do EIDM" -Source "EIDM" -Level "Error" -Exception $result.exception -Detail $result.status
    }
    return $result
}

function Get-RoleSolvedText {
    param($Action, $TicketNumber)

    if ($Action -in @("ADD", "PROLONG", "REMOVE"))
    {
        return "Úspešné vytvorenie tiketu $TicketNumber"
    }
    return $null
}

function Set-RoleTicketResult {
    param(
        $Requests,
        $TicketNumber,
        $ErrorText
    )

    foreach ($request in @($Requests))
    {
        addAttribute $request.Attributes "solvedText"
        addAttribute $request.Attributes "isSolved"
        addAttribute $request.Attributes "error"

        if ($null -eq $TicketNumber)
        {
            $request.Attributes.error = "{0} {1}" -f (Get-Date -Format "dd.MM.yyyy HH:mm:ss"), $ErrorText
            $request.Attributes.isSolved = $false
        }
        else
        {
            $request.Attributes.ticketNumber = $TicketNumber
            $request.Attributes.solvedText = Get-RoleSolvedText -Action $request.Action -TicketNumber $TicketNumber
            $request.Attributes.isSolved = $true
            $request.Attributes.error = $null
        }

        $json = "[$($request | ConvertTo-Json -Compress)]"
        $result = ExportToPortal -json $json -objectType $eidmTypeTicket
        if ($result.isError)
        {
            Write-ScriptLog -Message "Chyba zapisu tiketu $($request.TechName) do EIDM" -Source "EIDM" -Level "Error" -Exception $result.exception -Detail $result.status
        }
    }
}

#endregion

#region Xurrent

function Connect-Xurrent {
    $body = @{
        client_id     = $xurrentClientId
        client_secret = $xurrentClientSecret
        grant_type    = "client_credentials"
    }
    $authToken = Invoke-RestMethod -Method Post -Uri $xurrentTokenUrl -Body $body -Proxy $xurrentProxy -UseBasicParsing
    if ($null -ne $authToken.access_token)
    {
        $expiration = [datetime]::MaxValue
        if ($null -ne $authToken.expires_in)
        {
            $lifetime = [int]$authToken.expires_in
            $skew = $xurrentTokenSkewSeconds
            if ($lifetime -le $skew) { $skew = 0 }
            $expiration = (Get-Date).AddSeconds($lifetime - $skew)
        }
        $authToken | Add-Member NoteProperty "expirationTime" $expiration
    }
    return $authToken
}

function Update-XurrentToken {
    $stillValid = $null -ne $script:xurrentAuth -and
        -not [string]::IsNullOrEmpty($script:xurrentAuth.access_token) -and
        $null -ne $script:xurrentAuth.expirationTime -and
        $script:xurrentAuth.expirationTime -gt (Get-Date)

    if ($stillValid) { return $true }

    $hadToken = $null -ne $script:xurrentAuth
    try
    {
        $script:xurrentAuth = Connect-Xurrent
    }
    catch
    {
        $script:xurrentAuthError = $_.Exception.Message
        return $false
    }

    if ($null -eq $script:xurrentAuth -or [string]::IsNullOrEmpty($script:xurrentAuth.access_token))
    {
        return $false
    }

    $script:xurrentHeaders = @{
        Authorization       = "Bearer $($script:xurrentAuth.access_token)"
        "Content-Type"      = "application/json; charset=utf-8"
        "X-Xurrent-Account" = $xurrentAccount
    }

    if ($hadToken)
    {
        Write-ScriptLog -Message "Obnoveni tokenu Xurrent, platnost $($script:xurrentAuth.expires_in) s"
    }
    return $true
}

function Invoke-XurrentRequest {
    param(
        [string]$Method,
        [string]$RelativeUrl,
        $Body
    )

    if (-not (Update-XurrentToken))
    {
        throw "Chyba prihlaseni k Xurrent: $script:xurrentAuthError"
    }

    $params = @{
        Uri             = "$xurrentApiUrl/$RelativeUrl"
        Method          = $Method
        Headers         = $script:xurrentHeaders
        Proxy           = $xurrentProxy
        UseBasicParsing = $true
    }
    if ($null -ne $Body)
    {
        $params.Body = $Body
    }
    return Invoke-RestMethod @params
}

#endregion

#region Identity

function Get-IdmObject {
    param($Id)
    if ([string]::IsNullOrEmpty($Id)) { return $null }
    return getIDMUser -IdmObjectId $Id
}

function Select-FirstValue {
    param($Primary, $Fallback)
    if ($null -ne $Primary) { return $Primary }
    return $Fallback
}

function Get-IdentityTicketDetail {
    param($Ticket, $User, $Json)

    $syCode = $User.syRis
    if ($null -eq $syCode) { $syCode = $User.syWind }

    if ([string]::IsNullOrEmpty($syCode))
    {
        return [PSCustomObject]@{ IsError = $true; Status = "Nenalezen SYWIND / SYRIS: $syCode" }
    }

    $isKnownCode = $privilegedSyCodes.Contains($syCode) -or $standardSyCodes.Contains($syCode)
    if (-not $isKnownCode)
    {
        return [PSCustomObject]@{ IsError = $true; Status = "Neocekavany SYWIND: $syCode" }
    }

    $fields = [PSCustomObject]@{
        meno          = $null
        priezvisko    = $null
        osobne_cislo  = $null
        manager       = $null
        Employee_ID   = $null
        Display_name  = $null
        sy_wind       = $null
        stav_uctu     = $null
    }

    if ($null -ne $Json.userId)
    {
        $parent = $null
        if ($null -ne $User.parentObject)
        {
            $parent = Get-IdmObject -Id $User.parentObject
        }

        $fields.meno = Select-FirstValue $User.firstName $parent.firstName
        $fields.priezvisko = Select-FirstValue $User.lastName $parent.lastName
        $fields.osobne_cislo = Select-FirstValue $User.employeeID $parent.employeeID
        $fields.Display_name = $User.name
        $fields.sy_wind = $syCode
        $fields.stav_uctu = $User.computedAccountStatus

        if (-not [string]::IsNullOrEmpty($User.userPrincipalName))
        {
            $fields.Employee_ID = $User.userPrincipalName.Split("@")[0]
        }

        $managerId = Select-FirstValue $User.managerObjectId $parent.managerObjectId
        $manager = Get-IdmObject -Id $managerId
        if ($null -ne $manager)
        {
            $fields.manager = "$($manager.name)($($manager.employeeID))"
        }

        $organizationId = Select-FirstValue $User.organizationExternal $parent.organizationExternal
        if ($null -ne $organizationId)
        {
            $organization = Get-IdmObject -Id $organizationId
            if ($null -ne $organization)
            {
                $fields | Add-Member NoteProperty "cislo_zmluvy" $organization.name
            }
        }
        else
        {
            $department = ""
            if ($null -ne $User.department) { $department = "$($User.department) " }
            if ($null -ne $User.departmentCode) { $department = "$department$($User.departmentCode)" }
            if ($department -ne "")
            {
                $fields | Add-Member NoteProperty "cislo_zmluvy" $department
            }
        }
    }

    $isAdd = $Json.action -eq "add" -or $Json.action -eq "create"
    $changeWord = $(if ($isAdd) { "vytvorenie" } else { "zmenu" })
    $blockingAction = $blockingActionLabels[[string]$Json.action]
    $accountType = $(if ($syCode -like "*RIS*") { "RIS" } else { "ENTRA" })
    $isPrivileged = ($Json.privileged -eq "True")

    if ($isPrivileged)
    {
        if ($blockingAction)
        {
            $title = "Žiadosť o $blockingAction administrátorského konta $accountType "
            $templateId = $identityTemplatePrivilegedBlocking
        }
        else
        {
            $title = "Žiadosť o $changeWord administrátorského konta $accountType"
            $templateId = $identityTemplatePrivilegedChange
        }
    }
    elseif ($blockingAction)
    {
        $title = "Žiadosť o $blockingAction konta RIS"
        $templateId = $identityTemplateStandardBlocking
    }
    else
    {
        $title = "Žiadosť o $changeWord konta  RIS"
        $templateId = $identityTemplateStandardChange
    }

    return [PSCustomObject]@{
        IsError           = $false
        SourceTicket      = $Ticket
        TechName          = $Ticket.TechName
        Title             = $title
        TemplateId        = $templateId
        ServiceInstanceId = $serviceInstanceId
        Category          = $xurrentCategory
        CustomFields      = $fields
    }
}

function Publish-IdentityTicket {
    param($Detail)

    $payload = [PSCustomObject]@{
        subject             = $Detail.Title
        template_id         = $Detail.TemplateId
        service_instance_id = $Detail.ServiceInstanceId
        category            = $Detail.Category
        custom_fields       = @(
            foreach ($property in $Detail.CustomFields.PSObject.Properties)
            {
                [PSCustomObject]@{
                    id    = $property.Name
                    value = $property.Value
                }
            }
        )
    }

    $rawJson = $payload | ConvertTo-Json -Depth 10
    $jsonText = [System.Text.Encoding]::UTF8.GetString(
        [System.Text.Encoding]::Convert(
            [System.Text.Encoding]::Unicode,
            [System.Text.Encoding]::UTF8,
            [System.Text.Encoding]::Unicode.GetBytes($rawJson)
        )
    )
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($jsonText)

    try
    {
        $response = Invoke-XurrentRequest -Method "Post" -RelativeUrl "requests" -Body $bodyBytes
        if ($null -ne $response.id)
        {
            Write-ScriptLog -Message "Uspesne vytvoreni tiketu $($Detail.Title): $($response.id)" -Detail "IDM TechName: $($Detail.TechName) --------------Tiket detail: $jsonText"
            Update-IdmTicket -Ticket $Detail.SourceTicket -TicketNumber $response.id -IsSolved $true -SolvedText "Vytvorenie tiketu: $($response.id) "
            return
        }

        $solvedText = "Xurrent nevratil cislo tiketu"
        Write-ScriptLog -Message "Chyba vytvareni tiketu $($Detail.Title): $solvedText" -Level "Error" -Detail $jsonText
        Update-IdmTicket -Ticket $Detail.SourceTicket -TicketNumber $null -IsSolved $false -SolvedText $solvedText
    }
    catch
    {
        $message = Get-RestErrorMessage -ErrorRecord $_
        Write-ScriptLog -Message "Chyba vytvareni tiketu $($Detail.Title): $message" -Level "Error" -Detail $jsonText
        Update-IdmTicket -Ticket $Detail.SourceTicket -TicketNumber $null -IsSolved $false -SolvedText $message
    }
}

function Invoke-IdentityTicket {
    param($Ticket)

    $json = $null
    try
    {
        $json = $Ticket.Attributes.json | ConvertFrom-Json
    }
    catch
    {
        $message = "Nelze nacist detail tiketu: $($_.Exception.Message)"
        Write-ScriptLog -Message "Nelze nacist detail tiketu" -Level "Error" -Exception $_.Exception.Message -Detail $Ticket.Attributes.json
        Update-IdmTicket -Ticket $Ticket -ErrorText $message -TicketNumber $null -IsSolved $false -SolvedText $null
        return
    }

    if (-not $allowedIdentityActions.Contains([string]$json.action)) { return }
    if ($json.type -eq "cast") { return }

    $ticketKey = [string]$Ticket.idmObjectID
    if ([string]::IsNullOrEmpty($ticketKey)) { $ticketKey = [string]$Ticket.TechName }
    if (-not [string]::IsNullOrEmpty($ticketKey) -and $null -ne $script:handledIdentityTickets)
    {
        $script:handledIdentityTickets[$ticketKey] = $true
    }

    try
    {
        $json.UserId = $json.url.Substring($userIdUrlStartIndex)
    }
    catch
    {
        $message = "Nelze nacist detail tiketu: $($_.Exception.Message)"
        Write-ScriptLog -Message "Nelze nacist detail tiketu" -Level "Error" -Exception $_.Exception.Message -Detail $Ticket.Attributes.json
        Update-IdmTicket -Ticket $Ticket -ErrorText $message -TicketNumber $null -IsSolved $false -SolvedText $null
        return
    }

    $user = Get-IdmObject -Id $json.UserId
    if ($null -eq $user)
    {
        $message = "Nelze najit uzivatele $($json.UserId)"
        Write-ScriptLog -Message $message -Level "Error" -Detail $Ticket.Attributes.json
        Update-IdmTicket -Ticket $Ticket -ErrorText $message -TicketNumber $null -IsSolved $false -SolvedText "Error"
        return
    }

    $detail = Get-IdentityTicketDetail -Ticket $Ticket -User $user -Json $json
    if ($detail.IsError)
    {
        Write-ScriptLog -Message $detail.Status -Level "Error" -Detail $Ticket.Attributes.json
        Update-IdmTicket -Ticket $Ticket -ErrorText $detail.Status -TicketNumber $null -IsSolved $false -SolvedText "Error"
        return
    }

    Publish-IdentityTicket -Detail $detail
}

#endregion

#region Role

function Find-IdmUser {
    param($Users, $Id)
    return $Users | Where-Object { $_.idmObjectID -eq $Id } | Select-Object -First 1
}

function ConvertTo-EidmDate {
    param($Value)
    if ([string]::IsNullOrEmpty($Value)) { return $null }

    $text = (($Value -replace "Z", "").Split("."))[0]
    try
    {
        return [datetime]::ParseExact($text, "yyyy-MM-ddTHH:mm:ss", $null)
    }
    catch
    {
        return $null
    }
}

function Get-RoleOperation {
    param($ActionType)
    switch ([string]$ActionType)
    {
        $roleActionProlong { return [PSCustomObject]@{ Label = "Predľženie"; Code = "ADD" } }
        $roleActionRemove  { return [PSCustomObject]@{ Label = "Odobratie"; Code = "REMOVE" } }
        default            { return [PSCustomObject]@{ Label = "Pridelenie"; Code = $null } }
    }
}

function New-RoleTicketWithoutAttachment {
    param($Requests, $Note, $Role, $Users)

    $requests = @($Requests)
    $externalRoleCode = $Role.Attributes.externalRoleCode
    $teamId = $Role.Attributes.itsmCode

    if ([string]::IsNullOrEmpty($externalRoleCode))
    {
        Write-ScriptLog -Message "AR neni dolpnen atribut externalRoleCode" -Level "Error"
        return
    }
    if ([string]::IsNullOrEmpty($teamId))
    {
        Write-ScriptLog -Message "Nelze zjistit teamID - ITSM atribut neni doplnen nebo ma spatny format" -Level "Error"
        return
    }

    $title = "Žiadosť o pridelenie / predĺženie / odobratie aplikačnej role '$externalRoleCode'"
    $fieldParts = New-Object System.Collections.Generic.List[string]
    $index = 0
    $actionCode = $null

    foreach ($request in $requests)
    {
        $index++
        $targetUser = Find-IdmUser -Users $Users -Id $request.Attributes.requestedFor
        $requestorUser = Find-IdmUser -Users $Users -Id $request.Attributes.requestedBy
        $accountName = $targetUser.TechName
        $displayName = "$($targetUser.Name) ($accountName)"
        $requestor = "$($requestorUser.Name) ($($requestorUser.TechName))"
        $operation = Get-RoleOperation -ActionType $request.Attributes.actionType
        if ($operation.Code) { $actionCode = $operation.Code }

        $validFrom = ConvertTo-EidmDate -Value $request.Attributes.requestedFrom
        $validTo = ConvertTo-EidmDate -Value $request.Attributes.requestedTo
        $validFromText = ""
        $validToText = ""
        if ($null -ne $validFrom) { $validFromText = $validFrom.AddHours($roleValidityHourOffset).ToString("dd.MM.yyyy") }
        if ($null -ne $validTo) { $validToText = $validTo.AddHours($roleValidityHourOffset).ToString("dd.MM.yyyy") }
        $validity = "od: $validFromText do: $validToText)"

        $fieldParts.Add("{""id"":""ziadost_popis"",""value"":$($jsonConverter.EscapeIt($Note))}")
        $fieldParts.Add("{""id"":""requester_$index"",""value"":$($jsonConverter.EscapeIt($requestor))}")
        $fieldParts.Add("{""id"":""meno_priezvisko_$index"",""value"":$($jsonConverter.EscapeIt($displayName))}")
        if (-not [string]::IsNullOrEmpty($validToText))
        {
            $fieldParts.Add("{""id"":""date_$index"",""value"":""$validity""}")
        }
        $fieldParts.Add("{""id"":""operation_$index"",""value"":$($jsonConverter.EscapeIt($operation.Label))}")
        $fieldParts.Add("{""id"":""r_reason_$index"",""value"":$($jsonConverter.EscapeIt($request.Attributes.reason))}")
    }

    $body = "{""custom_fields"":[$($fieldParts -join ',')],""subject"":$($jsonConverter.EscapeIt($title)),""template_id"":$roleTemplateId,""service_instance_id"":$serviceInstanceId,""team_id"":$teamId,""category"":""$xurrentCategory""}"

    try
    {
        $response = Invoke-XurrentRequest -Method "Post" -RelativeUrl "requests" -Body $body
        if ($null -ne $response.id)
        {
            Write-ScriptLog -Message "Uspesne vytvoreni $actionCode tiketu $($response.id)" -Detail $body
            Set-RoleTicketResult -Requests $requests -TicketNumber $response.id
            return
        }

        $errorText = "Xurrent nevratil cislo tiketu"
        Write-ScriptLog -Message "Chyba vytvareni $actionCode tiketu: $errorText" -Level "Error" -Detail $body
        Set-RoleTicketResult -Requests $requests -ErrorText $errorText
    }
    catch
    {
        $message = Get-RestErrorMessage -ErrorRecord $_
        Write-ScriptLog -Message "Chyba vytvareni $actionCode tiketu: $message" -Level "Error" -Detail $body
        Set-RoleTicketResult -Requests $requests -ErrorText $message
    }
}

function Send-XurrentAttachment {
    param([string]$FilePath)

    $storage = Invoke-XurrentRequest -Method "Get" -RelativeUrl "attachments/storage"
    $curlArgs = New-Object System.Collections.Generic.List[string]
    $curlArgs.Add("-s")
    $curlArgs.Add("-F")
    $curlArgs.Add("`"Content-Type=plain/text`"")

    foreach ($item in ($storage.s3 | Get-Member | Where-Object { $_.MemberType -eq "NoteProperty" }))
    {
        $propertyName = $item.Name
        $curlArgs.Add("-F")
        $curlArgs.Add("`"$propertyName=$($storage.s3.$propertyName)`"")
    }

    $curlArgs.Add("-F")
    $curlArgs.Add("`"file=@$FilePath`"")
    $curlArgs.Add([string]$storage.upload_uri)
    $curlArgs.Add("--ssl-no-revoke")
    $curlArgs.Add("-x")
    $curlArgs.Add($xurrentProxy)

    $rawResult = & curl.exe @($curlArgs.ToArray())
    $xml = [xml]$rawResult
    Write-ScriptLog -Message "Nahrani prilohy" -Detail $xml.OuterXml
    return $xml.PostResponse.Key
}

function New-RoleTicketWithAttachment {
    param($Requests, $Note, $Role, $Users)

    $requests = @($Requests)
    $externalRoleCode = $Role.Attributes.externalRoleCode
    $teamId = $Role.Attributes.itsmCode
    if ($requests.Count -eq 0 -or [string]::IsNullOrEmpty($externalRoleCode))
    {
        Write-ScriptLog -Message "AR neni dolpnen atribut externalRoleCode" -Level "Error"
        return
    }

    $roleName = $Role.Name
    $title = "Hromadná žiadosť o pridelenie / predĺženie / odobratie aplikačnej role '$roleName'"
    $fileName = "{0}_{1}.xlsx" -f $roleName, (Get-Date -Format "yyyyMMdd_HH_mm_ss")

    if (-not (Test-Path $roleExcelFolder))
    {
        New-Item -ItemType Directory -Path $roleExcelFolder -Force | Out-Null
    }

    $targetPath = Join-Path $roleExcelFolder $fileName
    Copy-Item -Path $roleExcelTemplate -Destination $targetPath

    Import-Module ImportExcel -ErrorAction Stop
    $package = $null
    $package = Open-ExcelPackage $targetPath
    $actionCode = $null
    try
    {
        $sheet = $package.Workbook.Worksheets[1]
        $row = 2

        foreach ($request in $requests)
        {
            $row++
            $targetUser = Find-IdmUser -Users $Users -Id $request.Attributes.requestedFor
            $requestorUser = Find-IdmUser -Users $Users -Id $request.Attributes.requestedBy
            $accountName = $targetUser.TechName
            $displayName = "$($targetUser.Name) ($accountName)"
            $requestor = $request.requestor
            if ([string]::IsNullOrEmpty($requestor))
            {
                $requestor = "$($requestorUser.Name) ($($requestorUser.TechName))"
            }

            $operation = Get-RoleOperation -ActionType $request.Attributes.actionType
            if ($operation.Code) { $actionCode = $operation.Code }
            $validTo = ConvertTo-EidmDate -Value $request.Attributes.requestedTo

            Set-ExcelRange -Address $sheet.Cells[$row, 1] -Value $requestor
            Set-ExcelRange -Address $sheet.Cells[$row, 2] -Value $displayName
            Set-ExcelRange -Address $sheet.Cells[$row, 3] -Value $accountName
            if (-not [string]::IsNullOrEmpty($request.ValidTo) -and $null -ne $validTo)
            {
                Set-ExcelRange -Address $sheet.Cells[$row, 4] -Value $validTo.ToString("dd.MM.yyyy")
            }
            Set-ExcelRange -Address $sheet.Cells[$row, 5] -Value $request.Attributes.reason
            Set-ExcelRange -Address $sheet.Cells[$row, 6] -Value $operation.Label
        }

        $lastRow = $requests.Count + 2
        Set-ExcelRange -Range $sheet.Cells["G3:H$lastRow"] -Locked:$false
        $sheet.Protection.SetPassword($excelSheetPassword)
        Set-ExcelRange -Range $sheet.Cells["G3:H$lastRow"] -AutoSize
    }
    finally
    {
        if ($null -ne $package) { Close-ExcelPackage $package }
    }

    $attachmentKey = Send-XurrentAttachment -FilePath $targetPath
    if ([string]::IsNullOrEmpty($attachmentKey))
    {
        Write-ScriptLog -Message "Chyba nahrani prilohy pro roli $roleName" -Level "Error"
        return
    }

    $body = "{""custom_fields"":[{""id"":""ziadost_popis"",""value"":$($jsonConverter.EscapeIt($Note))}],""subject"":$($jsonConverter.EscapeIt($title)),""template_id"":$roleTemplateId,""service_instance_id"":$serviceInstanceId,""team_id"":$teamId,""category"":""$xurrentCategory"",""note"":$($jsonConverter.EscapeIt($roleAttachmentDescription)),""note_attachments"":[{""key"":""$attachmentKey""}]}"

    try
    {
        $response = Invoke-XurrentRequest -Method "Post" -RelativeUrl "requests" -Body $body
        if ($null -ne $response.id)
        {
            Write-ScriptLog -Message "Uspesne vytvoreni $actionCode tiketu $($response.id)" -Detail $body
            Set-RoleTicketResult -Requests $requests -TicketNumber $response.id
            return
        }

        $errorText = "Xurrent nevratil cislo tiketu"
        Write-ScriptLog -Message "Chyba vytvareni $actionCode tiketu: $errorText" -Level "Error" -Detail $body
        Set-RoleTicketResult -Requests $requests -ErrorText $errorText
    }
    catch
    {
        $message = Get-RestErrorMessage -ErrorRecord $_
        Write-ScriptLog -Message "Chyba vytvareni $actionCode tiketu: $message" -Level "Error" -Detail $body
        Set-RoleTicketResult -Requests $requests -ErrorText $message
    }
}

function Publish-RoleRequests {
    param($Requests, $Role, $Application, $Users)

    $requests = @($Requests)
    if ($requests.Count -eq 0) { return }

    $techNames = ($requests | ForEach-Object { $_.TechName }) -join ", "
    Write-ScriptLog -Message "Zadosti ke zpracovani: $techNames"

    $applicationCode = [string]$Application.Attributes.applicationCodeAP
    $moduleCode = [string]$Application.Attributes.applicationModulCode
    $externalRoleCode = [string]$Role.Attributes.externalRoleCode
    $note = "Kód aplikácie: **$applicationCode**`nKód modulu aplikácie: **$moduleCode**`nExterný názov role: **$externalRoleCode**"

    # Tyto aplikace nesmi jit do hromadneho tiketu ani do prilohy – kazda zadost vlastni tiket.
    if ($applicationsWithoutAttachment.Contains($applicationCode))
    {
        foreach ($request in $requests)
        {
            New-RoleTicketWithoutAttachment -Requests @($request) -Note $note -Role $Role -Users $Users
        }
        return
    }

    if ($requests.Count -gt $maxRequestsWithoutAttachment)
    {
        New-RoleTicketWithAttachment -Requests $requests -Note $note -Role $Role -Users $Users
        return
    }

    New-RoleTicketWithoutAttachment -Requests $requests -Note $note -Role $Role -Users $Users
}

function Complete-RoleGroup {
    param($RoleId, $Requests, $Roles, $Applications, $Users)

    $role = $Roles | Where-Object { $_.idmObjectID -eq $RoleId } | Select-Object -First 1
    if ($null -eq $role)
    {
        Write-ScriptLog -Message "Nenalezena role" -Level "Error" -Detail $RoleId
        Set-RoleTicketResult -Requests $Requests -ErrorText "Rola nie je nájdená"
        return
    }

    if ($null -eq $role.Attributes.application)
    {
        Write-ScriptLog -Message "Role $($role.Techname) nema nastavenou aplikaci" -Level "Error"
        Set-RoleTicketResult -Requests $Requests -ErrorText "Rola nemá nastavenú aplikáciu"
        return
    }

    $application = $Applications | Where-Object { $_.idmObjectID -eq $role.Attributes.application } | Select-Object -First 1
    if ($null -eq $application)
    {
        Write-ScriptLog -Message "Nenalezena aplikace pro roli $($role.Techname)" -Level "Error"
        Set-RoleTicketResult -Requests $Requests -ErrorText "Nenájdená aplikácia pre rolu $($role.Techname)"
        return
    }

    if ($role.Attributes.itsmTicket -ne $true -or $null -eq $role.Attributes.itsmCode)
    {
        Write-ScriptLog -Message "Pre rolu $($role.Techname) neni nastavena resitelska skupina" -Level "Error"
        Set-RoleTicketResult -Requests $Requests -ErrorText "Pro roli $($role.Techname) nie je nastavená resitelska skupina"
        return
    }

    Publish-RoleRequests -Requests $Requests -Role $role -Application $application -Users $Users
}

#endregion

#endregion

#region KyB

# Stránkované volání Xurrent – vrátí všechny položky ze všech stránek.
# Používá Invoke-WebRequest (oproti Invoke-RestMethod) aby byly dostupné hlavičky
# x-pagination-current-page / x-pagination-total-pages.
function Invoke-XurrentPagedRequest {
    param([string]$RelativeUrl)

    if (-not (Update-XurrentToken))
    {
        throw "Chyba prihlaseni k Xurrent: $script:xurrentAuthError"
    }

    $allItems = @()
    $uri = "$xurrentApiUrl/$RelativeUrl"

    # Vynuti TLS 1.2 – Invoke-WebRequest muze defaultne zkusit TLS 1.0/1.1
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

    do
    {
        $params = @{
            Uri             = $uri
            Method          = "Get"
            Headers         = $script:xurrentHeaders
            Proxy           = $xurrentProxy
            UseBasicParsing = $true
        }
        try
        {
            $response = Invoke-WebRequest @params
        }
        catch
        {
            $message = Get-RestErrorMessage -ErrorRecord $_
            throw "Chyba Xurrent strankovani ($uri): $message"
        }

        $page  = $response.Content | ConvertFrom-Json
        if ($null -ne $page) { $allItems += $page }

        $currentPage = [int]$response.Headers["x-pagination-current-page"]
        $totalPages  = [int]$response.Headers["x-pagination-total-pages"]

        # Nalezeni odkazu na dalsi stranku z hlavicky Link
        $uri = $null
        $linkHeader = $response.Headers["link"]
        if (-not [string]::IsNullOrEmpty($linkHeader))
        {
            $nextPart = ($linkHeader -split ",") | Where-Object { $_ -like '*rel="next"*' }
            if ($nextPart)
            {
                $trimmed = $nextPart.Trim()
                $uri = $trimmed.Substring(1, $trimmed.IndexOf(">") - 1)
            }
        }
    }
    while ($null -ne $uri -and $currentPage -lt $totalPages)

    return $allItems
}

function Invoke-KybNonQuery {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$Query
    )
    Write-ScriptLog -Message $Query -Source "KyB-SQL"
    try
    {
        $command = $Connection.CreateCommand()
        $command.CommandText = $Query
        return $command.ExecuteNonQuery()
    }
    catch
    {
        Write-ScriptLog -Message "Chyba KyB SQL: $($_.Exception.Message)" -Level "Error" -Source "KyB-SQL" -Detail $Query
        return 0
    }
}

function Invoke-KybReader {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$Query
    )
    try
    {
        $command = $Connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 0
        $result = $command.ExecuteReader()
        $table = New-Object System.Data.DataTable
        $table.Load($result)
        return $table
    }
    catch
    {
        Write-ScriptLog -Message "Chyba KyB SQL: $($_.Exception.Message)" -Level "Error" -Source "KyB-SQL" -Detail $Query
        return $null
    }
}

function Update-KyBObjects {
    # Otevreni dedikovaného SQL pripojeni pro IDMSupport (KyB ciselnik)
    $kybConn = New-Object System.Data.SqlClient.SqlConnection
    $kybConn.ConnectionString = $kybConnectionString
    try
    {
        $kybConn.Open()
    }
    catch
    {
        Write-ScriptLog -Message "Chyba pripojeni k IDMSupport (KyB): $($_.Exception.Message)" -Level "Fatal" -Source "KyB"
        return
    }

    try
    {
        # 1. Stažení všech položek číselníku z Xurrent
        $elapsed = Measure-Command {
            $items = Invoke-XurrentPagedRequest -RelativeUrl $kybXurrentCollection
        }
        Write-ScriptLog -Message "Nacteno $($items.Count) KyB zaznamu z Xurrent" -Source "KyB" -Duration $elapsed.TotalMilliseconds

        # 2. Upsert do SQL
        foreach ($item in $items)
        {
            $dateUpdatedStr = "2000-01-01"
            if ($null -ne $item.updated_at)
            {
                $dateUpdatedStr = ($item.updated_at.Replace("T", " ") -split "\+")[0]
            }

            $adKontoStandard = ($item.custom_fields | Where-Object { $_.id -eq "ad_konto_standard" }).value

            # Apostrofy v textech se escapuji zdvojením
            $safeName        = ([string]$item.name).Replace("'", "''")
            $safeRef         = ([string]$item.reference).Replace("'", "''")
            $safeDesc        = ([string]$item.description).Replace("'", "''")
            $safeAdKonto     = ([string]$adKontoStandard).Replace("'", "''")

            $sqlUpsert = @"
IF EXISTS (SELECT [id] FROM [IDMSupport].[dbo].[X_Ciselnik_KyB] WHERE [id] = '$($item.id)')
BEGIN
    UPDATE [IDMSupport].[dbo].[X_Ciselnik_KyB]
    SET    [reference]          = '$safeRef',
           [name]               = '$safeName',
           [description]        = '$safeDesc',
           [DateUpdatedInXurrent] = '$dateUpdatedStr',
           [ad_konto_standard]  = '$safeAdKonto'
    WHERE  [id] = '$($item.id)'
END
ELSE
BEGIN
    INSERT INTO [IDMSupport].[dbo].[X_Ciselnik_KyB]
        ([id], [reference], [name], [description], [DateUpdatedInXurrent], [ad_konto_standard])
    VALUES
        ('$($item.id)', '$safeRef', '$safeName', '$safeDesc', '$dateUpdatedStr', '$safeAdKonto')
END
"@
            Invoke-KybNonQuery -Connection $kybConn -Query $sqlUpsert | Out-Null
        }

        Write-ScriptLog -Message "Upsert $($items.Count) KyB zaznamu dokoncen" -Source "KyB"

        # 3. Nacti zaznamy, ktere maji novejsi datum v Xurrent nez v SQL (nebo SQL datum chybi)
        $toUpdate = Invoke-KybReader -Connection $kybConn -Query @"
SELECT [id], [name], [KybID]
FROM   [IDMSupport].[dbo].[X_Ciselnik_KyB]
WHERE  ([DateUpdatedInSQL] < [DateUpdatedInXurrent])
   OR  [DateUpdatedInSQL] IS NULL
"@
        if ($null -eq $toUpdate) { return }
        Write-ScriptLog -Message "KyB zaznamu ke zpracovani (aktualizace KybID): $($toUpdate.Rows.Count)" -Source "KyB"

        # 4. Pro kazdy zaznam stáhni detail z Xurrent a aktualizuj KybID
        foreach ($row in $toUpdate)
        {
            $id = $row.id
            try
            {
                $detail = Invoke-XurrentRequest -Method "Get" -RelativeUrl "custom_collection_elements/$id"
                Start-Sleep -Milliseconds 500

                if ($null -ne $detail -and $null -ne $detail.custom_fields)
                {
                    $vplyv = $detail.custom_fields | Where-Object { $_.id -eq "vplyv_na_kyb" }
                    if ($null -ne $vplyv)
                    {
                        $oldValue = $row.KybID
                        if ($oldValue -ne $vplyv.value)
                        {
                            Invoke-KybNonQuery -Connection $kybConn -Query @"
UPDATE [IDMSupport].[dbo].[X_Ciselnik_KyB]
SET    KybID          = '$($vplyv.value)',
       oldKybID       = '$oldValue',
       DateUpdatedInSQL = GETDATE()
WHERE  id = '$id'
"@ | Out-Null
                        }
                    }
                }
            }
            catch
            {
                Write-ScriptLog -Message "Chyba zpracovani KyB zaznamu ${id}: $($_.Exception.Message)" -Level "Error" -Source "KyB"
            }
        }

        Write-ScriptLog -Message "KyB aktualizace dokoncena" -Source "KyB"
    }
    finally
    {
        $kybConn.Close()
    }
}

#endregion

#endregion

#region Beh

if (-not (Test-Path $logFolder))
{
    New-Item -ItemType Directory -Path $logFolder -Force | Out-Null
}

$startDate = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Write-ScriptLog -Message "---------------------------- ZACATEK $startDate ----------------------------"


try
{
    . C:\Batch\script\shared\sql.ps1
    . C:\Batch\script\shared\eidma.ps1

    # ----------------------------------------------------------------
    # Rezim -UpdateKyB: synchronizace KyB ciselniku standardu z Xurrent
    # ----------------------------------------------------------------
    if ($UpdateKyB)
    {
        if (-not (Update-XurrentToken))
        {
            Write-ScriptLog -Message "Chyba prihlaseni k Xurrent" -Level "Fatal" -Exception $script:xurrentAuthError
            return
        }
        Update-KyBObjects
        return
    }

    # ----------------------------------------------------------------
    # Standardni rezim: zpracovani identity a role tiketu z EIDM
    # ----------------------------------------------------------------

    $script:handledIdentityTickets = @{}

    if (-not (Update-EidmToken))
    {
        $authError = $null
        if ($error.Count -gt 0) { $authError = $error[0].Exception.Message }
        Write-ScriptLog -Message "Chyba prihlaseni k EIDM" -Source "EIDM" -Level "Fatal" -Exception $authError
        return
    }

    if (-not (Update-XurrentToken))
    {
        Write-ScriptLog -Message "Chyba prihlaseni k Xurrent" -Level "Fatal" -Exception $script:xurrentAuthError
        return
    }

    $allTickets = Get-EidmObjects -ObjectType $eidmTypeTicket -Description "tiketu"
    if ($null -eq $allTickets) { return }

    $identityTickets = @(
        $allTickets | Where-Object {
            $_.Attributes.ticketNumber -notmatch '^\d+$' -and
            -not [string]::IsNullOrEmpty($_.Attributes.json) -and
            $_.Attributes.isSolved -ne $true -and
            "$($_.IsArchived)" -ne "true"
        }
    )

    Write-ScriptLog -Message "Identity tiketu ke zpracovani: $($identityTickets.Count)"
    foreach ($ticket in $identityTickets)
    {
        try
        {
            Invoke-IdentityTicket -Ticket $ticket
        }
        catch
        {
            Write-ScriptLog -Message "Chyba zpracovani identity tiketu $($ticket.TechName)" -Level "Error" -Exception $_.Exception.Message
        }
    }

    $roleTickets = @(
        $allTickets | Where-Object {
            $ticketKey = [string]$_.idmObjectID
            if ([string]::IsNullOrEmpty($ticketKey)) { $ticketKey = [string]$_.TechName }
            $_.Attributes.isSolved -ne $true -and
            -not [string]::IsNullOrEmpty([string]$_.Attributes.roleRequested) -and
            -not ($script:handledIdentityTickets -and $script:handledIdentityTickets.ContainsKey($ticketKey))
        }
    )

    if ($roleTickets.Count -eq 0)
    {
        Write-ScriptLog -Message "Zadne role tikety ke zpracovani"
        return
    }

    Add-Type -Path $jsonEscapeDll
    $jsonConverter = [JsonEscape.Escape]::new()

    $idmUsers = Get-EidmObjects -ObjectType $eidmTypeUser -Description "uzivatelu"
    $idmRoles = Get-EidmObjects -ObjectType $eidmTypeRole -Description "roli"
    $idmApplications = Get-EidmObjects -ObjectType $eidmTypeApplication -Description "aplikaci"
    if ($null -eq $idmUsers -or $null -eq $idmRoles -or $null -eq $idmApplications) { return }

    $roleIds = @($roleTickets | ForEach-Object { $_.Attributes.roleRequested } | Sort-Object -Unique)
    Write-ScriptLog -Message "Roli ke zpracovani: $($roleIds.Count), zadosti: $($roleTickets.Count)"

    foreach ($roleId in $roleIds)
    {
        $batch = @($roleTickets | Where-Object { $_.Attributes.roleRequested -eq $roleId })
        try
        {
            Complete-RoleGroup -RoleId $roleId -Requests $batch -Roles $idmRoles -Applications $idmApplications -Users $idmUsers
        }
        catch
        {
            Write-ScriptLog -Message "Chyba zpracovani role $roleId" -Level "Error" -Exception $_.Exception.Message
            Set-RoleTicketResult -Requests $batch -ErrorText $_.Exception.Message
        }
    }
}
catch
{
    Write-ScriptLog -Message "Beh skriptu skoncil chybou" -Level "Fatal" -Exception $_.Exception.Message
}
finally
{
    $endDate = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-ScriptLog -Message "---------------------------- KONEC $endDate ----------------------------"
}

#endregion
