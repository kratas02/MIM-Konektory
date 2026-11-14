Add-Type -Path "C:\Batch\xurrent\JsonEscape.dll"
$converter = [JsonEscape.Escape]::new()

add-type @"
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    public class TrustAllCertsPolicy : ICertificatePolicy {
        public bool CheckValidationResult(
            ServicePoint srvPoint, X509Certificate certificate,
            WebRequest request, int certificateProblem) {
            return true;
        }
    }
"@
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy

$baseURL="https://api.xurrent.com/v1"
$template_id="217288"
$service_instance_id="923491"

Function Get-AuthorizationHeader {
   
    $Uri = "https://oauth.xurrent.com/token"
    $Body = @{
        client_id = "wOFVY69HNvOF7B72l8HeqnU8kHf5kbxWMfcOP8llOyGzHLBw"
        client_secret = "QNq1BEYqcqjL3b1i7PDuyWjYGNWqDLXsjZBIZMcDZ7hoI5QPgxUeJjXs3Mb88iWV"
        grant_type="client_credentials"
    }
    return Invoke-RestMethod -Method Post -Uri $Uri -Body $Body -Proxy "http://proxy.seas.sk:8080" -UseBasicParsing
}


#if($AuthResult -eq $null)
#{
    $AuthResult  = Get-AuthorizationHeader
    $token = $AuthResult.access_token
#}

$authHeader = @{
'Authorization'="Bearer $token"
'Content-Type'='application/json; charset=utf-8'
'X-Xurrent-Account'='seas-it'
}


#$AllItems =  New-Object System.Collections.Generic.List[PSObject]
$AllItems = @()


function GetPage($uri)
{

    $Params = @{
        "URI"    = "$uri"
        "Method" = 'Get'
        "Headers" = $authHeader
    }

    try
    {
        $ret = Invoke-WebRequest @Params -Proxy "http://proxy.seas.sk:8080" -UseBasicParsing
        return $ret
    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd()
            Log -text $respBody
            Log -text "$($error[0].Exception.Message)"
            Log -text $error[0].ErrorDetails.Message
        }
    catch
    {
        Log -Text "$($error[0].Exception.Message)"
        Log -text $error[0].ErrorDetails.Message
        Log -Text $Body
    }

}


Function GetAllObjects($obj)
{
     $uri = "$($baseURL)/$($obj)"
     $response = GetPage -uri $uri
     $AllItems+=($response.Content | ConvertFrom-Json)
     $currentPage = [int]$response.Headers["x-pagination-current-page"]
     $totalPages = [int]$response.Headers["x-pagination-total-pages"]
     #$link = $response.Headers["link"].Substring(1) 
     while($currentPage -lt $totalPages + 1)
     {
        Write-Host $currentPage
         $linkitems = $response.Headers["link"].split(",")
         $link = $linkitems| Where-Object { $_ -like '*rel="next"*' }
         if($link -eq $null)
         {
            break
         }
         $link = $link.Trim().Substring(1)
         $link = $link.substring(0,$link.indexOf(">"))
         #$link = $link.split("<")[0]
         #$link = "$($baseURL)/$($obj)&per_page=25&search_after=$($currentPage+1)"

         $response = GetPage -uri $link
         if($Error.count -gt 0)
         {
            $a = 1
         }
         #$link = $response.Headers["link"].Substring(1)
            $AllItems+=($response.Content | ConvertFrom-Json)
         $currentPage = [int]$response.Headers["x-pagination-current-page"]
         $totalPages = [int]$response.Headers["x-pagination-total-pages"]

         Start-Sleep -Seconds 1
    }

    return $AllItems
}

Function GetAllObjects_old($obj)
{
     $uri = "$baseURL/$obj"
     $response = GetPage -uri $uri
     $AllItems+=($response.Content | ConvertFrom-Json)
     $currentPage = [int]$response.Headers["x-pagination-current-page"]
     $totalPages = [int]$response.Headers["x-pagination-total-pages"]
     while($currentPage -lt $totalPages + 1)
     {
         #$link = $notes.Headers["link"].Substring(1)
         #$link = $link.split("<")[0]
         $link = "https://api.xurrent.com/v1/notes?per_page=25&search_after=$($currentPage+1)"

         $response = GetPage -uri $link
            $AllItems+=($response.Content | ConvertFrom-Json)
         $currentPage = [int]$response.Headers["x-pagination-current-page"]
         $totalPages = [int]$response.Headers["x-pagination-total-pages"]

 
    }

    return $AllItems
}

function GetItems($obj, $id)
{
    #https://developer.xurrent.com/v1/requests/

    $uri = "$baseURL/$obj"
    if($id -ne $null)
    {
        $uri = "$uri/$id"
    }

    #$uri = $uri + "?per_page=10"

    $Params = @{
        "URI"    = "$baseURL/$obj"
        "Method" = 'Get'
        "Headers" = $authHeader
    }

    try
    {
        $ret = Invoke-RestMethod @Params -Proxy "http://proxy.seas.sk:8080" -UseBasicParsing
        return $ret
    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd()
            Log -text $respBody
            Log -text "$($error[0].Exception.Message)"
            Log -text $error[0].ErrorDetails.Message
        }
    catch
    {
        Log -Text "$($error[0].Exception.Message)"
        Log -text $error[0].ErrorDetails.Message
        Log -Text $Body
    }

}


function GetXUserByMail($mail)
{
    if([string]::IsNullOrEmpty($mail))
    {
        return $null
    }

    $XUser = GetItems -obj "people?primary_email=$mail"
    return $XUser
}

function GetCISByDisplayName($dn)
{
    if([string]::IsNullOrEmpty($dn))
    {
        return $null
    }
    $XApp = GetItems -obj "cis?label=$dn"
    return $XApp
}


function GetRequests($id)
{
    #https://developer.xurrent.com/v1/requests/

    $Params = @{
        "URI"    = "$baseURL/requests/$id"
        "Method" = 'Get'
        "Headers" = $authHeader
    }

    try
    {
        $ret = Invoke-RestMethod @Params -Proxy "http://proxy.seas.sk:8080" -UseBasicParsing
        return $ret
    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd()
            Log -text $respBody
            Log -text "$($error[0].Exception.Message)"
            Log -text $error[0].ErrorDetails.Message
        }
    catch
    {
        Log -Text "$($error[0].Exception.Message)"
        Log -text $error[0].ErrorDetails.Message
        Log -Text $Body
    }

}

function CreateRequest()
{
    $Params = @{
        "URI"    = "$baseURL/requests"
        "Method" = 'Post'
        "Headers" = $authHeader
    }


   $BodyObj = @{
        subject = "TestJK MIM1"
        category = "incident"
        impact = "high"
        service_instance_id=939504
        team_id=38429
    }
    $Body = $BodyObj | ConvertTo-Json

    try
    {
        $response = Invoke-RestMethod @Params -body $Body -Proxy "http://10.98.246.68:8080"  -UseBasicParsing
        return response
    }
    catch [System.Net.WebException] {   
            if($_.Exception.Response -ne $null)
            {
                $respStream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($respStream)
                $respBody = $reader.ReadToEnd()
                Log -text $respBody
            }
            Log -text "$($error[0].Exception.Message)"
            Log -text $error[0].ErrorDetails.Message
        }
    catch
    {
        Log -Text "$($error[0].Exception.Message)"
        Log -text $error[0].ErrorDetails.Message
        Log -Text $Body
    }


}


function CreateRequestWithAttachment($filepath)
{
    $Params = @{
        "URI"    = "$baseURL/requests"
        "Method" = 'Post'
        "Headers" = $authHeader
    }


    $subject =  "ARSOZ_DOZ_Administrator"
    $jmeno =  "Mihal David (u812816)"
    $operation= "Predlzenie"

<#

   $BodyObj = @{
        category = "rfc"
        service_instance_id=$service_instance_id
        team_id=38419
        template_id=$template_id
        custom_fields=$vals
        subject=$subject
    }

    #>
    #$Body = '{"custom_fields":[{"id":"meno_priezvisko_1","value":"Mih\u00e1l D\u00e1vid (u812816)"},{"id":"login_1","value":"u109246"},{"id":"date_1","value":"2026-01-24"},{"id": "operation_1", "value": "Pred\u013e\u017eenie"},{"id":"meno_priezvisko_2","value":"Nov\u00e1k Jan (u111111)"},{"id":"login_2","value":"u111111"},{"id":"date_2","value":"2025-03-04"},{"id": "operation_2", "value": "Odobratie"},{"id":"meno_priezvisko_3","value":"Petr Radek (u222222)"},{"id":"login_3","value":"u222222"},{"id":"date_3","value":"2026-01-01"},{"id": "operation_3", "value": "Pridelenie"},{"id":"meno_priezvisko_4","value":"Pavel Stejskal"},{"id":"login_4","value":"u333333"},{"id":"date_4","value":"2025-10-01"},{"id": "operation_4", "value": "Pridelenie"},{"id":"meno_priezvisko_5","value":"David Kocman"},{"id":"login_5","value":"u444444"},{"id":"date_5","value":"2026-01-01"},{"id": "operation_5", "value": "Pridelenie"}],"subject":"ARSOZ_DOZ_Administr\u00e1tor","template_id":218724,"service_instance_id":940230,"team_id":38419,"category":"rfc"}'
    #$Body = '{"custom_fields":[{"id":"meno_priezvisko_1","value":"Mih\u00e1l D\u00e1vid (u812816)"},{"id":"login_1","value":"u109246"},{"id":"date_1","value":"2026-01-24"},{"id": "operation_1", "value": "Pred\u013e\u017eenie"},{"id":"meno_priezvisko_2","value":"Nov\u00e1k Jan (u111111)"},{"id":"login_2","value":"u111111"},{"id":"date_2","value":"2025-03-04"},{"id": "operation_2", "value": "Odobratie"},{"id":"meno_priezvisko_3","value":"Petr Radek (u222222)"},{"id":"login_3","value":"u222222"},{"id":"date_3","value":"2026-01-01"},{"id": "operation_3", "value": "Pridelenie"},{"id":"meno_priezvisko_4","value":"Pavel Stejskal"},{"id":"login_4","value":"u333333"},{"id":"date_4","value":"2025-10-01"},{"id": "operation_4", "value": "Pridelenie"},{"id":"meno_priezvisko_5","value":"David Kocman"},{"id":"login_5","value":"u444444"},{"id":"date_5","value":"2026-01-01"},{"id": "operation_5", "value": "Pridelenie"}],"subject":"ARSOZ_DOZ_Administr\u00e1tor","template_id":218724,"service_instance_id":940230,"team_id":38419,"category":"rfc","note":"$name","note_attachments":[{"key":"$path"}]}'

    $Body = '{"custom_fields":[{"id":"meno_priezvisko_1","value":"Mih\u00e1l D\u00e1vid (u812816)"},{"id":"login_1","value":"u109246"},{"id":"date_1","value":"2026-01-24"},{"id": "operation_1", "value": "Pred\u013e\u017eenie"},{"id": " r_reason_1", "value": "Potrebuji pracovat"}],"subject":"ARSOZ_DOZ_Administr\u00e1tor","template_id":218724,"service_instance_id":940230,"team_id":38419,"category":"rfc","note":"Hromadn\u00e1 \u017eiados\u0165 - zoznam pou\u017e\u00edvate\u013eov pre pr\u00edstup k aplik\u00e1ci\u00e1m\u00a0v\u00a0pr\u00edlohe","note_attachments":[{"key":"XXXpathXXX"}]}'
    $Body= $Body -replace "XXXpathXXX", $filepath

    try
    {
        Log -text $Body
        $response = Invoke-RestMethod @Params -body $Body -Proxy "http://10.98.246.68:8080" -UseBasicParsing
        Log -text $response
        if($response.id -ne $null)
        {
            Log -Text "Uspesne vytvoreni $($action) tiketu $($response.id)"
            SetRequestsStatus -requests $PSRequests -ticketNr $ret.ticketNumber -errDescription $null -description "Vytvorenie tiketu: $($response.id)"
        }
        else
        {
            Log -Text "Chyba vytvareni $($action) tiketu: $($ret.errCode): $($ret.errDescription)"
            SetRequestsStatus -requests $PSRequests -ticketNr $null -errDescription "$($ret.errCode): $($ret.errDescription)"
        }
    }
    catch [System.Net.WebException] {   
            if($_.Exception.Response -ne $null)
            {
                $respStream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($respStream)
                $respBody = $reader.ReadToEnd()
                Log -text $respBody
            }
            Log -text $respBody
            Log -text "$($error[0].Exception.Message)"
            Log -text $error[0].ErrorDetails.Message
        }
    catch
    {
        Log -Text "$($error[0].Exception.Message)"
        Log -text $error[0].ErrorDetails.Message
        Log -Text $Body
    }


}


function uploadfile($filename)
{
    $storage = GetItems -obj "attachments/storage"
    $curlargs = @()
    $curlargs+= "-F"
    $curlargs+= "`"Content-Type=plain/text`""

    $body = [System.Collections.Generic.Dictionary[string, string]]::new()
    foreach($item in ($storage.s3 | Get-Member | where {$_.MemberType -eq "NoteProperty"}))
    {
        $PropertyName = $item.Name
        $curlargs+= "-F"
        $curlargs+= "`"$PropertyName=$($storage.s3.$PropertyName)`""

    }

    $curlargs+= "-F"
    $curlargs+= "`"file=@$($filename)`""
    $curlargs+= $storage.upload_uri
    $curlargs+= "--ssl-no-revoke" 
    $curlargs+= "-x"
    $curlargs+= "http://10.98.246.68:8080"



    $ret = [xml](curl.exe $curlargs)
    log -Text $ret.OuterXml

    return $ret.PostResponse.Key

    #$ret = CreateRequestWithAttachment -filepath $ret.PostResponse.Key

}

function CreateCIS($name,$XictGarant,$XOwner,$XprocessOwner, $product_id)
{
    $Params = @{
        "URI"    = "$baseURL/cis"
        "Method" = 'Post'
        "Headers" = $authHeader
    }

   #$name =  $converter.EscapeIt($name)
   #$name = $name -replace  """", ""

  $app = GetCISByDisplayName -dn $name

    if($app -ne $null)
    {
        Log -text "Aplikace $name existuje"
        return $null
    }

   $BodyObj = @{
        product_id=543232
        status="in_production"
        name="$name"

    }
    #kod aplikace=cfs => product_id=543232
    #kod aplikace=S_EMO34 || Q_EMO34 => product_id=547031

    $body = "{""product_id"":$product_id,""status"":""in_production"",""name"":$($converter.EscapeIt($name))}"
    #$BodyObj =  $converter.EscapeIt($BodyObj)
    #$Body = $BodyObj | ConvertTo-Json

    #$Body = '{"subject":"ARSOZ_DOZ_Administr\u00e1tor","template_id":218724,"service_instance_id":940230,"team_id":38419,"category":"rfc"}'
    $custom_fields = $null
    if($XOwner -ne $null)
    {
        $custom_fields = "{""id"":""owner"",""value"":""$($XOwner.id)""}"
    }


    if($XprocessOwner -ne $null)
    {
        if($custom_fields -ne $null){$custom_fields+=","}
        $custom_fields += "{""id"":""process_garant"",""value"":""$($XprocessOwner.id)""}"
    }

    if($XictGarant -ne $null)
    {
        if($custom_fields -ne $null){$custom_fields+=","}
        $custom_fields += "{""id"":""process_garant"",""value"":""$($XictGarant.id)""}"
    }


    if($custom_fields -ne $null){$custom_fields="""custom_fields"":[$custom_fields],"}
    $body = $Body -replace "{", "{$custom_fields"


    #$Body = '{"product":534408,"status":"in_production","name":"FileNet"}'
    $a = 1

    try
    {
        $response = Invoke-RestMethod @Params -body $Body -Proxy "http://10.98.246.68:8080"  -UseBasicParsing
        return $response
    }
    catch [System.Net.WebException] {   
            if($_.Exception.Response -ne $null)
            {
                $respStream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($respStream)
                $respBody = $reader.ReadToEnd()
                Log -text $respBody
            }
            Log -text "$($error[0].Exception.Message)"
            Log -text $error[0].ErrorDetails.Message
        }
    catch
    {
        Log -Text "$($error[0].Exception.Message)"
        Log -text $error[0].ErrorDetails.Message
        Log -Text $Body
    }


}

function UpdateCIS($name,$XictGarant,$XOwner,$XprocessOwner, $product_id, $id)
{
    $Params = @{
        "URI"    = "$baseURL/cis/$id"
        "Method" = 'PATCH'
        "Headers" = $authHeader
    }


    $body = "{""product_id"":$product_id,""name"":$($converter.EscapeIt($name))}"


    $a = 1

    try
    {
        $response = Invoke-RestMethod @Params -body $Body -Proxy "http://10.98.246.68:8080" -UseBasicParsing
        return $response
    }
    catch [System.Net.WebException] {   
            if($_.Exception.Response -ne $null)
            {
                $respStream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($respStream)
                $respBody = $reader.ReadToEnd()
                Log -text $respBody
            }
            Log -text "$($error[0].Exception.Message)"
            Log -text $error[0].ErrorDetails.Message
        }
    catch
    {
        Log -Text "$($error[0].Exception.Message)"
        Log -text $error[0].ErrorDetails.Message
        Log -Text $Body
    }


}


function GetTeamID($roleID)
{
    Log -text "zjistovani team ID pro $roleID"
    $roleID=$roleID -replace "urn:uuid:",""

    $MIMRole = GetMIMObject -filter "/Group[ObjectID='$roleID']"
    if($MIMRole -ne $null)
    {
        $ret = $null
        if([string]::IsNullOrEmpty($MIMRole.seasTSServio))
        {
            log -text "Atribut ITSM resitelska skupina neni doplnen"
            return $null
        }
        else
        {
            if($MIMRole.seasTSServio.Trim() -match "^[\d\.]+$")
            {
                Log -text "Nalezena ITSM resitelska skupina: $($MIMRole.seasTSServio)"
                return $MIMRole.seasTSServio.Trim()
            }
            else
            {
                log -text "Atribut ITSM resitelska skupina ma spatny format: $($MIMRole.seasTSServio)"
                return $null
            }
        }
        
    }
    else
    {
        log -text "Role nenalezena"
        return $null
    }
}

Function CreateTicketWithAtt($requests, $note, $IDMRole)
{

    $externalRoleCode = $IDMRole.Attributes.externalRoleCode
    if($requests -eq $null -or [string]::IsNullOrEmpty($externalRoleCode))
    {
        Log -text "AR neni dolpnen atribut externalRoleCode"
        return
    }

    $teamID = $IDMRole.Attributes.itsmCode


    $body = ""
    #$title = "IDM – tiket na zmenu oprávnení"
    $title = $null
    $role =$IDMRole.Name
    $title = "Hromadná žiadosť o pridelenie / predĺženie / odobratie aplikačnej role '$role'"
    

    $description = "Žiadosť pre viacerých používateľov podľa priloženého súboru. Po spracovaní prístupu aplikačnej roly používateľovi doplňte do priloženého súboru výsledok Pridelené/Nepridelené/Odobraté. V prípade nepridelenia doplňte dôvod."

    $targetFileName = "$($role)_$((Get-Date).ToString("yyyyMMdd_HH_mm_ss")).xlsx"
    $targetName  = "C:\Batch\xurrent\Requests\$targetFileName"
     
    $xlSourceTemplate = "C:\Batch\xurrent\hromadny_tiket_sablona.xlsx"
    Copy-Item "$xlSourceTemplate" -Destination "$targetName"
            
    $excelServio = Open-ExcelPackage $targetName
    #$servioSheet = $excelServio.Workbook.Worksheets["List1"]
    $servioSheet = $excelServio.Workbook.Worksheets[1]
    $counter = 2
    foreach($request in $requests)
    {

        $user = $IDMUsers | where{$_.idmObjectID -eq $request.Attributes.requestedFor} 
        #$items = $($serviorecord.displayName) -split ("->")
        #$items = $($d) -split ("->")
        $displayname = "$($user.Name) ($($accountname))"
        $accountname =  $user.TechName

        $duvod = $request.Attributes.reason

        $counter++
        $ptype = "Pridelenie"
         if($request.Attributes.actionType -eq "3301")
        {
            $ptype = "Predľženie"
            $action = "ADD"
        }
        elseif($request.Attributes.actionType -eq "3302")
        {
            $ptype = "Odobratie"
            $action = "REMOVE"

        }
        $validFromDT = ConvertToDate -strDate $request.Attributes.requestedFrom
        $validToDT = ConvertToDate -strDate $request.Attributes.requestedTo
        Set-ExcelRange -Address $servioSheet.Cells[$counter,1] -Value $request.requestor
        Set-ExcelRange -Address $servioSheet.Cells[$counter,2] -Value $displayname
        Set-ExcelRange -Address $servioSheet.Cells[$counter,3] -Value $accountname
        if(![string]::IsNullOrEmpty($request.ValidTo))
        {
            Set-ExcelRange -Address $servioSheet.Cells[$counter,4] -Value $($validToDT.toString("dd.MM.yyyy"))
        }
        Set-ExcelRange -Address $servioSheet.Cells[$counter,5] -Value $duvod
        Set-ExcelRange -Address $servioSheet.Cells[$counter,6] -Value $ptype
    }


    Set-ExcelRange -Range $servioSheet.cells["G3:H$($requests.count + 2)"] -Locked:$false
    $servioSheet.Protection.SetPassword("741852")
    Set-ExcelRange -Range $servioSheet.cells["G3:H$($requests.count + 2)"] -AutoSize
    Close-ExcelPackage $excelServio

    $attPath = uploadfile -filename $targetName

    #$attPath = "attachments/52911/2025/04/02/4642222/1743610565.3908691-ece448fbe6186ec722e80b5226238cf8efbc6be4de4f81f9dc8daeb437ee38ab/arszemo_admin_20250402_18_05_57.xlsx"

    if($attPath -ne $null)
    {
        $Params = @{
        "URI"    = "$baseURL/requests"
        "Method" = 'Post'
        "Headers" = $authHeader
        }

        #$custom_fields = """custom_fields"":[{""id"":""ziadost_popis"",""value"":$($converter.EscapeIt($note))}]



        $Body = "{""custom_fields"":[{""id"":""ziadost_popis"",""value"":$($converter.EscapeIt($note))}],""subject"":$($converter.EscapeIt($title)),""template_id"":$template_id,""service_instance_id"":$service_instance_id,""team_id"":$teamID,""category"":""rfc"",""note"":$($converter.EscapeIt($description)),""note_attachments"":[{""key"":""XXXpathXXX""}]}"
        $Body= $Body -replace "XXXpathXXX", $attPath

    try
    {
        Log -text $Body
        $response = Invoke-RestMethod @Params -body $Body -Proxy "http://proxy.seas.sk:8080" -UseBasicParsing
        Log -text $response
        if($response.id -ne $null)
        {
            Log -Text "Uspesne vytvoreni $($action) tiketu $($response.id )"
            #SetRequestsStatus -requests $PSRequests -ticketNr $response.id  -errDescription $null -description "Vytvorenie $($action) tiketu: $($response.id)"
            SetTicketResult -requests $PSRequests -ticketNr $response.id -errDescription $null -description "Vytvorenie $($action) tiketu: $($response.id)"
        }
        else
        {
            Log -Text "Chyba vytvareni $($action) tiketu: $($ret.errCode): $($ret.errDescription)"
            SetTicketResult -requests $PSRequests -ticketNr $null -errDescription "$($ret.errCode): $($ret.errDescription)"
            #SetRequestsStatus -requests $PSRequests -ticketNr $null -errDescription "$($ret.errCode): $($ret.errDescription)"
        }
    }
    catch [System.Net.WebException] {   
            if($_.Exception.Response -ne $null)
            {
                $respStream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($respStream)
                $respBody = $reader.ReadToEnd()
                Log -text $respBody
            }
            Log -text $respBody
            Log -text "$($error[0].Exception.Message)"
            Log -text $error[0].ErrorDetails.Message
            SetTicketResult -requests $PSRequests -ticketNr $null -errDescription "$message"
        }
    catch
    {
        Log -Text "$($error[0].Exception.Message)"
        Log -text $error[0].ErrorDetails.Message
        Log -Text $Body
        SetTicketResult -requests $PSRequests -ticketNr $null -errDescription "$($action) tiketu: $($_.Exception.Message)"
    }
    }
   
}

function ConvertToDate($strDate)
{
    $strDate = $strDate -replace "Z", ""
    $strDate = $strDate.split(".")[0]

    try
    {
        return [datetime]::parseexact("$strDate", 'yyyy-MM-ddTHH:mm:ss', $null)  
    }
    catch
    {
        return $null
    }
}

Function CreateTicketWithoutAtt($requests, $note, $IDMRole)
{
    $externalRoleCode = $IDMRole.Attributes.externalRoleCode

    if($externalRoleCode -eq $null)
    {
        Log -text "AR neni dolpnen atribut externalRoleCode"
        return
    }

    $teamID = $IDMRole.Attributes.itsmCode

    if($teamID -eq $null)
    {
        Log -text "Nelze zjistit teamID - ITSM atribut neni doplnen nebo ma spatny format"
        return
    }

    $body = ""
    #$title = "IDM – tiket na zmenu oprávnení"
    $title = $null
    $role = $IDMRole.Attributes.externalRoleCode
    $title = "Žiadosť o pridelenie / predĺženie / odobratie aplikačnej role '$role'"

    
    #pre viacerých používateľov podľa priloženého súboru. 
    #Po spracovaní prístupu aplikačnej roly používateľovi doplňte do priloženého súboru výsledok Pridelené/Nepridelené/Odobraté. 
    #V prípade nepridelenia doplňte dôvod."

    #$Body = '{"custom_fields":[{"id":"meno_priezvisko_1","value":"Mih\u00e1l D\u00e1vid (u812816)"},{"id":"login_1","value":"u109246"},{"id":"date_1","value":"2026-01-24"},{"id": "operation_1", "value": "Pred\u013e\u017eenie"},{"id":"meno_priezvisko_2","value":"Nov\u00e1k Jan (u111111)"},{"id":"login_2","value":"u111111"},{"id":"date_2","value":"2025-03-04"},{"id": "operation_2", "value": "Odobratie"},{"id":"meno_priezvisko_3","value":"Petr Radek (u222222)"},{"id":"login_3","value":"u222222"},{"id":"date_3","value":"2026-01-01"},{"id": "operation_3", "value": "Pridelenie"},{"id":"meno_priezvisko_4","value":"Pavel Stejskal"},{"id":"login_4","value":"u333333"},{"id":"date_4","value":"2025-10-01"},{"id": "operation_4", "value": "Pridelenie"},{"id":"meno_priezvisko_5","value":"David Kocman"},{"id":"login_5","value":"u444444"},{"id":"date_5","value":"2026-01-01"},{"id": "operation_5", "value": "Pridelenie"}],"subject":"ARSOZ_DOZ_Administr\u00e1tor","template_id":218724,"service_instance_id":940230,"team_id":38419,"category":"rfc"}'
    $body = "{""subject"":$($converter.EscapeIt($title)),""template_id"":$template_id,""service_instance_id"":$service_instance_id,""team_id"":$teamID,""category"":""rfc""}"
    #

    $counter = 0
    $custom_fields = $null
    foreach($request in $requests)
    {
        $user = $IDMUsers | where{$_.idmObjectID -eq $request.Attributes.requestedFor} 
        $requestorUser = $IDMUsers | where{$_.idmObjectID -eq $request.Attributes.requestedBy} 
        $requestor =  "$($requestorUser.Name) ($($requestorUser.TechName))"

        #$items = $($serviorecord.displayName) -split ("->")
        #$items = $($d) -split ("->")
        $accountname = $user.TechName
        $displayname =  "$($user.Name) ($($accountname))"

        $request.requestor


        $duvod = $request.Attributes.reason

        $counter++
        $ptype = "Pridelenie"
        if($request.Attributes.actionType -eq "3301")
        {
            $ptype = "Predľženie"
            $action = "ADD"
        }
        elseif($request.Attributes.actionType -eq "3302")
        {
            $ptype = "Odobratie"
            $action = "REMOVE"
            $MIMRequest = $null
        }

        $validFromDT = ConvertToDate -strDate $request.Attributes.requestedFrom
        $validToDT = ConvertToDate -strDate $request.Attributes.requestedTo

        
        if($validFromDT -eq $null)
        {
            $validFrom = ""
        }
        else
        {
            $validFrom=$($validFromDT.AddHours(3).toString("dd.MM.yyyy"))
        }

        if($validToDT -eq $null)
        {
            $validTo = ""
        }
        else
        {
            $validTo=$($validToDT.AddHours(3).toString("dd.MM.yyyy"))
        }


        #$validity= "od: $validFrom do: $($validToDT.AddHours(3).toString("dd.MM.yyyy"))"
        $validity= "od: $validFrom do: $validTo)"
        #$custom_fields+= "{""id"":""requester_$($counter)"",""value"":$($converter.EscapeIt($($request.requestor)))},{""id"":""meno_priezvisko_$($counter)"",""value"":$($converter.EscapeIt($displayname))},{""id"":""login_$($counter)"",""value"":""$accountname""},"
        $custom_fields+= "{""id"":""ziadost_popis"",""value"":$($converter.EscapeIt($note))},{""id"":""requester_$($counter)"",""value"":$($converter.EscapeIt($($requestor)))},{""id"":""meno_priezvisko_$($counter)"",""value"":$($converter.EscapeIt($displayname))},"
        if(-not [string]::IsNullOrEmpty($ValidTo))
        {
            $custom_fields+= "{""id"":""date_$($counter)"",""value"":""$validity""}," 
        }                  
        $custom_fields+= "{""id"":""operation_$($counter)"", ""value"":$($converter.EscapeIt($ptype))},{""id"":""r_reason_$($counter)"", ""value"":$($converter.EscapeIt($duvod))},"
    }
    $custom_fields = $custom_fields.Substring(0,$custom_fields.Length -1)
    $custom_fields="""custom_fields"":[$custom_fields],"
    $body = $body -replace "{", "{$custom_fields"

    #,""note"":$($converter.EscapeIt($description))

   # $body += $body1
    Log -Text $Body
    #return $Body

    try
    {
        if($Body -eq $null)
        {
            return $null
        }

         $Params = @{
            "URI"    = "$baseURL/requests"
            "Method" = 'Post'
            "Headers" = $authHeader     
        }
        $response = Invoke-RestMethod @Params -body $Body -Proxy "http://proxy.seas.sk:8080" -UseBasicParsing


        #if($true)
        if($response.id -ne $null)
        {
            Log -Text "Uspesne vytvoreni $($action) tiketu $($response.id )"
            SetTicketResult -requests $requests -ticketNr $response.id -errDescription $null -description "Vytvorenie $($action) tiketu: $($response.id)"
        }
        else
        {
            Log -Text "Chyba vytvareni $($action) tiketu: $($ret.errCode): $($ret.errDescription)"
            #$ret =  ExportToPortal -objectType 32 -json $json            
            #SetRequestsStatus -requests $PSRequests -ticketNr $null -errDescription "$($ret.errCode): $($ret.errDescription)"
            SetTicketResult -requests $requests -ticketNr $null -errDescription "$($ret.errCode): $($ret.errDescription)"
        }
    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd()
            Log -text "Chyba request"
            $message = $null
            $message = $respBody
            if([string]::IsNullOrEmpty($message))
            {
                $message = $Error[0].Exception.Message
            }
            
            if($error[0].ErrorDetails.Message -ne $Null)
            {
                Log -text  "$($error[0].ErrorDetails.Message)"
            }

            Log -text $message
           # SetRequestsStatus -requests $requests -ticketNr $null -errDescription "$message"
            SetTicketResult -requests $requests -ticketNr $null -errDescription "$message"

        }
    catch
    {
        Log -Text "Chyba vytvareni $($action) tiketu: $($_.Exception.Message)"
        Log -Text $Body
        #SetRequestsStatus -requests $requests -ticketNr $null -errDescription "$($action) tiketu: $($_.Exception.Message)"
        SetTicketResult -requests $requests -ticketNr $null -errDescription "$($action) tiketu: $($_.Exception.Message)"
    }
}


Function CreateTicket($PSRequests, $IDMRole, $IDMApp)
{
    if($PSRequests -eq "-"){return}
    Log -text "-------------- Zadosti ke zpracovani ------------------"
    foreach($r in $PSRequests)
    {
        Log -Text $r.TechName
    }
    Log -text "-------------------------------------------------------"
    $params = $null
    if($PSRequests -eq $null){return $null}
    #$role = GetMIMObject -filter "/Group[ObjectID='$($PSRequests[0].RoleObjectID.Guid)']"
    $externalRoleCode = ""
    $kodAplikace = ""
    $kodModuluAplikace = ""
    
    $externalRoleCode = $IDMRole.Attributes.externalRoleCode
    $kodAplikace = $IDMApp.Attributes.applicationCodeAP
    $kodModuluAplikace = $IDMApp.Attributes.applicationModulCode

    $note="Kód aplikácie: **$($kodAplikace)**
Kód modulu aplikácie: **$($kodModuluAplikace)**
Externý názov role: **$($externalRoleCode)**"
    
    if($PSRequests.count -gt $maxRequestForNoAttachementRoles)
    {
        if($PSRequests.Count -gt $maxRequestForNoAttachementRoles -and  $NoAttachementRoles.Contains($kodAplikace))
        #if($PSRequests.Count -gt $maxRequestForNoAttachementRoles)
        {
            $pocet =  [math]::Ceiling($requestObjects.Count / $maxRequestForNoAttachementRoles)
            for($i=0; $i -lt $pocet; $i++)
            {
                $start=$i*$maxRequestForNoAttachementRoles
                $end = $start +$maxRequestForNoAttachementRoles - 1
                $requestObjects0 = $requestObjects[$start..$end]
                CreateTicketWithoutAtt -requests $requestObjects0 -note $note -IDMRole $IDMRole
            }
        
        }
        else
        {
            $ret = CreateTicketWithAtt -requests $PSRequests -note $note -IDMRole $IDMRole
        }
    }
    else
    {
        $ret = CreateTicketWithoutAtt -requests $PSRequests -note $note -IDMRole $IDMRole
    }
        

}


function UpdateKyBObjects($isDelta=$false)
{
    if($isDelta)
    {
        $lastDate = ""
        try
        {
            $dtLastDate = ExecuteReader -commandtext "select max(DateUpdatedInSQL) from [IDMSupport].[dbo].[X_Ciselnik_KyB]"
            $strLastDate = $dtLastDate[0].AddHours(-2).ToString("yyyy-MM-ddTHH:mm:ssZ")
            if(-not [string]::IsNullOrEmpty($strLastDate))
            {
                $strLastDate = "&updated_at=>$($strLastDate)"
                $strLastDate = "&updated_at=>2025-10-02T12:08:50Z"
            }
        }
        catch
        {

        }
    }

    $items = GetAllObjects -obj "custom_collection_elements?custom_collection=%c5%a1tandardy&fields=id,name,reference,description,created_at,updated_at,custom_fields"
    #$items_old = GetAllObjects -obj "custom_collection_elements?custom_collection=%c5%a1tandardy$($strLastDate)"
    #$items_old = GetAllObjects -obj "custom_collection_elements?custom_collection=%c5%a1tandardy$($strLastDate)"


    foreach($item in $items)
    {


        $dateUpdatetedstr = "2000-01-01"
        if($item.updated_at -ne $null)
        {
            $dateUpdatetedstr = $item.updated_at.Replace("T"," ")
            $dateUpdatetedstr = $dateUpdatetedstr.split("+")[0]
        }

        $ad_konto_standard = $null

        $ad_konto_standard = ($item.custom_fields | where {$_.id -eq "ad_konto_standard"}).value

            $sqlQuery = "IF EXISTS (SELECT [id] FROM [IDMSupport].[dbo].[X_Ciselnik_KyB] WHERE [id] = '$($item.id)')
        BEGIN
	        update [IDMSupport].[dbo].[X_Ciselnik_KyB] SET [reference] = '$($item.reference)', [name] = '$($item.name)',[description] = '$($item.description)',[DateUpdatedInXurrent] = '$dateUpdatetedstr', [ad_konto_standard] = '$ad_konto_standard' where  [id] = '$($item.id)'
        END
        ELSE
        BEGIN
	        insert into [IDMSupport].[dbo].[X_Ciselnik_KyB] (id,[reference],[name],[description],[DateUpdatedInXurrent],[ad_konto_standard]) Values('$($item.id)','$($item.reference)','$($item.name)','$($item.description)','$dateUpdatetedstr','$ad_konto_standard') 
        END"
        ExecuteNonQuery -query $sqlQuery

    }


    $itemsToUpdate= ExecuteReader -commandtext "select id,[name],[KybID] from [IDMSupport].[dbo].[X_Ciselnik_KyB] where ([DateUpdatedInSQL] < [DateUpdatedInXurrent]) or [DateUpdatedInSQL] is NULL"



    foreach($item in $itemsToUpdate)
    {
        $id = $item.id
        $detail = $null
        $detail = GetItems -obj "custom_collection_elements/$id"
        Start-Sleep -Milliseconds 500
        if($error.Count -gt 0)
        {
            $a =1
            $error.Clear()
        } 
        if($detail -ne $null -and $detail.custom_fields -ne $null )
        {
            $vplyv = $null
            $vplyv = $detail.custom_fields | where {$_.id -eq "vplyv_na_kyb"}
            if($vplyv -ne $null)
            {
                $oldvalue = $item.KybID
                if($oldvalue -ne $vplyv.value)
                {
                    ExecuteNonQuery -query "Update [IDMSupport].[dbo].[X_Ciselnik_KyB] SET KybID = '$($vplyv.value)', oldKybID='$oldvalue', DateUpdatedInSQL =  getdate() where id = '$id'"
                }
            }
        }
    }
}

function GetTicketDetail($IDMTicket)
{

}

Function  CreateWFTicket($TicketDetail)
{

# 1. Sestavení čistého PowerShell objektu
$KompletniObjekt = [PSCustomObject]@{
    subject             = $TicketDetail.title
    template_id         = $TicketDetail.template_id
    service_instance_id = $TicketDetail.service_instance_id
    category            = $TicketDetail.category
    custom_fields       = @(
        foreach ($Property in $TicketDetail.custom_fields.psobject.Properties) {
            [PSCustomObject]@{
                id    = $Property.Name
                value = $Property.Value
            }
        }
    )
}

# 2. Převod objektu do JSON řetězce (PS 5.1 zde vnitřně udělá unicode úniky \uXXXX)
$RawJson = ConvertTo-Json -InputObject $KompletniObjekt -Depth 10

# 3. Odstranění unicode úniků \uXXXX a převod na čitelný UTF-8 text string
$JsonString = [System.Text.Encoding]::UTF8.GetString([System.Text.Encoding]::Convert([System.Text.Encoding]::Unicode, [System.Text.Encoding]::UTF8, [System.Text.Encoding]::Unicode.GetBytes($RawJson)))

# 4. KLÍČOVÝ KROK PRO PS 5.1: Převod textového řetězce na pole UTF-8 bajtů
# Tímto krokem obejdeme chybu PS 5.1, která by string jinak přepsala do Latin-1
$BodyInBytes = [System.Text.Encoding]::UTF8.GetBytes($JsonString)
$Body = $JsonString
<#
    
    #pre viacerých používateľov podľa priloženého súboru. 
    #Po spracovaní prístupu aplikačnej roly používateľovi doplňte do priloženého súboru výsledok Pridelené/Nepridelené/Odobraté. 
    #V prípade nepridelenia doplňte dôvod."
    #,""team_id"":$($TicketDetail.title)
    #$Body = '{"custom_fields":[{"id":"meno_priezvisko_1","value":"Mih\u00e1l D\u00e1vid (u812816)"},{"id":"login_1","value":"u109246"},{"id":"date_1","value":"2026-01-24"},{"id": "operation_1", "value": "Pred\u013e\u017eenie"},{"id":"meno_priezvisko_2","value":"Nov\u00e1k Jan (u111111)"},{"id":"login_2","value":"u111111"},{"id":"date_2","value":"2025-03-04"},{"id": "operation_2", "value": "Odobratie"},{"id":"meno_priezvisko_3","value":"Petr Radek (u222222)"},{"id":"login_3","value":"u222222"},{"id":"date_3","value":"2026-01-01"},{"id": "operation_3", "value": "Pridelenie"},{"id":"meno_priezvisko_4","value":"Pavel Stejskal"},{"id":"login_4","value":"u333333"},{"id":"date_4","value":"2025-10-01"},{"id": "operation_4", "value": "Pridelenie"},{"id":"meno_priezvisko_5","value":"David Kocman"},{"id":"login_5","value":"u444444"},{"id":"date_5","value":"2026-01-01"},{"id": "operation_5", "value": "Pridelenie"}],"subject":"ARSOZ_DOZ_Administr\u00e1tor","template_id":218724,"service_instance_id":940230,"team_id":38419,"category":"rfc"}'
    

    $body = "{""subject"":$($converter.EscapeIt($($TicketDetail.title))),""template_id"":$($TicketDetail.template_id),""service_instance_id"":$($TicketDetail.service_instance_id),""category"":""$($TicketDetail.category)""}"
    #$body = "{""subject"":""$($TicketDetail.title)"",""template_id"":$($TicketDetail.template_id),""service_instance_id"":$($TicketDetail.service_instance_id),""category"":""$($TicketDetail.category)""}"

    $JsonObjekt = [PSCustomObject]@{
    custom_fields = foreach ($Property in $TicketDetail.custom_fields.psobject.Properties) {
            [PSCustomObject]@{
                id    = $Property.Name
                value = $Property.Value
            }
        }
    }

    $custom_fields = $JsonObjekt | ConvertTo-Json
    $custom_fields = $custom_fields.Trim("}")
    $custom_fields = $custom_fields + ","
   #$custom_fields = $converter.EscapeIt($custom_fields)

    #$custom_fields="""custom_fields"":$custom_fields,"


    $body = $body -replace "{", "$custom_fields"

#>

    try
    {
        if($Body -eq $null)
        {
            return $null
        }

         $Params = @{
            "URI"    = "$baseURL/requests"
            "Method" = 'Post'
            "Headers" = $authHeader     
        }


        # Konverze objektu do JSON (zde se znaky escapují)

# Trik pro vynucení správného UTF-8 kódování u textového řetězce
#$body = [System.Text.Encoding]::UTF8.GetString([System.Text.Encoding]::Convert([System.Text.Encoding]::Unicode, [System.Text.Encoding]::UTF8, [System.Text.Encoding]::Unicode.GetBytes($body)))


        #$Body = $Body | ConvertFrom-Json | ConvertTo-Json -Compress
        $response = Invoke-RestMethod @Params -body $BodyInBytes -Proxy "http://proxy.seas.sk:8080" -UseBasicParsing
        #$response = New-Object PSObject       
        #$response  | Add-Member NoteProperty "id" "13098019"

        #if($true)
        if($response.id -ne $null)
        {

            Write-Log  -Message "Uspesne vytvoreni tiketu $($TicketDetail.title): $($response.id )" -source "Xurrent" -detail "IDM TechName: $($TicketDetail.TicketTechName) --------------Tiket detail: $body"
            UpdateIDMTicket -IDMTicket $TicketDetail.IDMTicket -ticketNr $response.id -isSolved $true -solvedText "Vytvorenie tiketu: $($response.id) "
            #SetTicketResult -requests $requests -ticketNr $response.id -errDescription $null -description "Vytvorenie $($action) tiketu: $($response.id)"
        }
        else
        {
            Write-Log  -Message "Chyba vytvareni tiketu $($TicketDetail.title): $($ret.errCode): $($ret.errDescription))" -Level "Error" -source "Xurrent" -detail $body
            UpdateIDMTicket -IDMTicket $TicketDetail.IDMTicket -ticketNr $null -isSolved $false -solvedText  "$($ret.errCode): $($ret.errDescription)"
            #SetTicketResult -requests $requests -ticketNr $null -errDescription "$($ret.errCode): $($ret.errDescription)"
        }
    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd()
            $message = $null
            $message = $respBody
            if([string]::IsNullOrEmpty($message))
            {
                $message = $Error[0].Exception.Message
            }
            
            Write-Log  -Message "Chyba vytvareni tiketu $($TicketDetail.title): $message" -Level "Error" -source "Xurrent" -detail $body
           # SetTicketResult -requests $requests -ticketNr $null -errDescription "$message"
            UpdateIDMTicket -IDMTicket $TicketDetail.IDMTicket -ticketNr $null -isSolved $false -solvedText "$message"
        }
    catch
    {
        Write-Log  -Message "Chyba vytvareni tiketu $($TicketDetail.title): $message" -Level "Error" -source "Xurrent" -detail $body
        #SetTicketResult -requests $requests -ticketNr $null -errDescription "$($action) tiketu: $($_.Exception.Message)"
        UpdateIDMTicket -IDMTicket $TicketDetail.IDMTicket -ticketNr $null -isSolved $false -solvedText "$($_.Exception.Message)"
    }
}


#GetAllObjects -obj "notes"