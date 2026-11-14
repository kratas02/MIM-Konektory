$EIDMURL = "http://seweidmt1v"
add-type -assembly system.web.extensions
$ps_js=new-object system.web.script.serialization.javascriptSerializer



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

Function Get-AuthorizationHeader {
   
    $Uri = "$EIDMURL/connect/token"
    $Body = @{
        client_id = "EasyIDMPortalVNext"
        client_secret = "easyIDMClientSecret!123"
        grant_type="client_credentials"
        scope="easyidm_portal"
    }
    $authToken = Invoke-RestMethod -Method Post -Uri $Uri -Body $Body
    if($authToken.access_token -ne $null)
    {
        $authToken | Add-Member NoteProperty "expirationTime" ((Get-Date).AddSeconds($authToken.expires_in - 50))
    }
    return $authToken
}

function ExportToPortal($json, $objectType)
 {
   try{
        $request = [System.Net.WebRequest]::Create("$EIDMURL/api/mimsync/synchronize/$objectType")
        $request.Method = "POST"
        $request.ContentType = "application/json"
        $request.Headers["Authorization"] = "Bearer $token"


        $requestStream = [System.IO.StreamWriter]$request.GetRequestStream()
        $requestStream.Write($json)
        $requestStream.Flush() 
        $requestStream.Close()

        $response =[System.Net.HttpWebResponse]$request.GetResponse()
        $reader = [IO.StreamReader] $response.GetResponseStream()

        $responseJson = $reader.ReadToEnd()
        
        if([string]::IsNullOrEmpty($responseJson))
        {
           log -text "Vysledek aktualizace: $($response.StatusCode)"
           return $true
        }
        else
        {
            log -text $responseJson 
            $r = $ps_js.DeserializeObject($responseJson)
            $status = $r["Report"]["Entries"][0]["Code"]
            log -text $status
            return $true
        }
        


    }
    catch [System.Net.WebException] {
        Log -text $Error[0].Exception.Message
        $statusCode = $_.Exception.Response.StatusCode
        $respStream = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($respStream)
        $reader.BaseStream.Position = 0
        $errResponse= "$($reader.ReadToEnd())"
        Log -text "$($statusCode): $errResponse"
        return $false
    }
    catch
    {
        Log -text $Error[0].Exception.Message
        return $false
    }

 }


 function GetAllObjectsFromEIDM($url, $paging = $true)
{

    try{
        $allObject = @()
        $page = 1
    
        $canContinue = $true

        do
        {
            $request = [System.Net.WebRequest]::Create($url)
            $request.Method = "GET"
            $request.ContentType = "application/json"
            $request.Headers["Authorization"] = "Bearer $token"
            $request.Headers.Add("Pagination", "{ 'ItemsPerPage': 50,'CurrentPage': $page }")
                 

            $response = $null
            $reader = $null
            $responseJson = $null
            $response =[System.Net.HttpWebResponse]$request.GetResponse()
            $reader = [IO.StreamReader] $response.GetResponseStream()

            $responseJson = $reader.ReadToEnd()
            #$responseXML

            $jsonObjects = $responseJson | ConvertFrom-Json
            if($jsonObjects.Objects.count -gt 0)
            {
                $allObject += $jsonObjects
                $page++;
                if($jsonObjects.Objects.count -gt 1000)
                {
                    $canContinue = $false
                }
            }
            else
            {
                $canContinue = $false
            }

            if($paging -eq $false)
            {
                $canContinue = $false
            }

        } While ($canContinue)

        $isError = $false
        $status = "OK"



    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd() | ConvertFrom-Json
            Log -text "ChybaGetAllRequestsFromEIDM"
            Log -text $respBody.message
     }
    catch
    {
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $status = "Chyba: $($Error[0].Exception.Message)"
    }

$mytable=@{isError=$isError;status=$status;objects=$allObject.Objects}

Return $mytable
}

## ----------------------------------

function UpdateUser($json, $URL) 
{
    add-type -assembly system.web.extensions
    $ps_js=new-object system.web.script.serialization.javascriptSerializer

   try{
        $isError = $true


        $request = [System.Net.WebRequest]::Create($url)
        $request.Method = "POST"
        $request.ContentType = "application/json"

        $requestStream = [System.IO.StreamWriter]$request.GetRequestStream()
        $requestStream.Write($json)
        $requestStream.Flush() 
        $requestStream.Close()

        $response =[System.Net.HttpWebResponse]$request.GetResponse()
        $reader = [IO.StreamReader] $response.GetResponseStream()

        $responseJson = $reader.ReadToEnd()
        
        if([string]::IsNullOrEmpty($responseJson))
        {
           log -text "Vysledek aktualizace: $($response.StatusCode)"
        }
        else
        {
            log -text $responseJson 
            $r = $ps_js.DeserializeObject($responseJson)

            $mimstatusTXT = ""
            $status = $r["Report"]["Entries"][0]["Code"]
            return $true
        }
        


    }
    catch [System.Net.WebException] {
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $mimstatusTXT = "Chyba: $($Error[0].Exception.Message)"
        $statusCode = $_.Exception.Response.StatusCode
        $respStream = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($respStream)
        $reader.BaseStream.Position = 0
        $mimstatusTXT = "Chyba: $($reader.ReadToEnd())"
        Log -text $mimstatusTXT
        if(!$mimstatusTXT)
        {
            $mimstatusTXT = "Chyba: $($Error[0].Exception.Message)"
        }

    }
    catch
    {
       
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $mimstatusTXT = "Chyba: $($Error[0].Exception.Message)"



    }
}
function UpdateAPP($json, $URL) 
{
    add-type -assembly system.web.extensions
    $ps_js=new-object system.web.script.serialization.javascriptSerializer

   try{
        $isError = $true


        $request = [System.Net.WebRequest]::Create($url)
        $request.Method = "POST"
        $request.ContentType = "application/json"

        $requestStream = [System.IO.StreamWriter]$request.GetRequestStream()
        $requestStream.Write($json)
        $requestStream.Flush() 
        $requestStream.Close()

        $response =[System.Net.HttpWebResponse]$request.GetResponse()
        $reader = [IO.StreamReader] $response.GetResponseStream()

        $responseJson = $reader.ReadToEnd()
        
        if([string]::IsNullOrEmpty($responseJson))
        {
           log -text "Vysledek aktualizace: $($response.StatusCode)"
        }
        else
        {
            log -text $responseJson 
            $r = $ps_js.DeserializeObject($responseJson)

            $mimstatusTXT = ""
            $status = $r["Report"]["Entries"][0]["Code"]
            return $true
        }
        


    }
    catch [System.Net.WebException] {
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $mimstatusTXT = "Chyba: $($Error[0].Exception.Message)"
        $statusCode = $_.Exception.Response.StatusCode
        $respStream = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($respStream)
        $reader.BaseStream.Position = 0
        $mimstatusTXT = "Chyba: $($reader.ReadToEnd())"
        Log -text $mimstatusTXT
        if(!$mimstatusTXT)
        {
            $mimstatusTXT = "Chyba: $($Error[0].Exception.Message)"
        }

    }
    catch
    {
       
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $mimstatusTXT = "Chyba: $($Error[0].Exception.Message)"



    }
}



function GetAllRequestsFromEIDM
{

    try{
        $allObject = @()
        $page = 1
    
        $canContinue = $true

        do
        {
            $request = [System.Net.WebRequest]::Create("$($EIDMURL)/api/MimSync/request")
            $request.Method = "GET"
            $request.ContentType = "application/json"
            $request.Headers.Add("Pagination", "{ 'ItemsPerPage': 10,'CurrentPage': $page }")
            $response = $null
            $reader = $null
            $responseJson = $null
            $response =[System.Net.HttpWebResponse]$request.GetResponse()
            $reader = [IO.StreamReader] $response.GetResponseStream()

            $responseJson = $reader.ReadToEnd()
            #$responseXML

            $jsonObjects = $responseJson | ConvertFrom-Json
            if($jsonObjects.count -gt 0)
            {
                $allObject += $jsonObjects
                $page++;
            }
            else
            {
                $canContinue = $false
            }

        } While ($canContinue)

        $isError = $false
        $status = "OK"



    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd() | ConvertFrom-Json
            Log -text "ChybaGetAllRequestsFromEIDM"
            Log -text $respBody.message
     }
    catch
    {
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $status = "Chyba: $($Error[0].Exception.Message)"
    }

$mytable=@{isError=$isError;status=$status;requests=$allObject}

Return $mytable
}
function GetRequestFromEIDM ($externalRequestId)
{

    if([string]::IsNullOrEmpty($externalRequestId)){return $null}
    try{
        $allObject = $null
        $page = 1
    
        $canContinue = $true

        $request = [System.Net.WebRequest]::Create("$($EIDMURL)/api/MimSync/request/$externalRequestId")
        $request.Method = "GET"
        $request.ContentType = "application/json"
        $request.Headers.Add("Pagination", "{ 'ItemsPerPage': 10,'CurrentPage': $page }")
        $response = $null
        $reader = $null
        $responseJson = $null
        $response =[System.Net.HttpWebResponse]$request.GetResponse()
        $reader = [IO.StreamReader] $response.GetResponseStream()

        $responseJson = $reader.ReadToEnd()
        #$responseXML

        $allObject = $responseJson | ConvertFrom-Json
        $isError = $false
        $status = "OK"



    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd()
            $respBody = $respBody | ConvertFrom-Json

            Log -text "ChybaGetAllRequestsFromEIDM"
            Log -text $respBody.message
     }
    catch
    {
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $status = "Chyba: $($Error[0].Exception.Message)"
    }

$mytable=@{isError=$isError;status=$status;requests=$allObject}

Return $mytable
}

function GetRequestbyID($ID)
{
    $filter = "/RoleRequest[ObjectID='$id' or seasExtGUID ='$id']"
    $retObjects =  $null
    $retObjects =  GetMIMObject -filter $filter
    foreach($retObject in $retObjects)
    {
        if($retObject.PSobject.Properties.name -notcontains "seasWorkflows")
        {
            $retObject | Add-Member NoteProperty "seasWorkflows" $null
        }
        $retObject | Add-Member NoteProperty "seasWorkflowObjects" @()
        foreach($wf in $retObject.seasWorkflows)
        {
            $WFObject =  GetMIMObject -filter "/RoleWorkflow[ObjectID='$($wf.substring(9))']"
            $retObject.seasWorkflowObjects += $WFObject
        }

    }
    return $retObjects
}

function SetRequestStatus($MIMSQLrequest, $seasReqStatus, $message, $extendedDate)
{
 

    $MIMRequest = GetRequestbyID -ID $MIMSQLrequest.ObjectID
    $obj = New-Object Microsoft.ResourceManagement.Automation.ObjectModel.ImportObject
    $obj.ObjectType = $MIMRequest.MIMObject.ResourceManagementObject.ObjectType
    $obj.SourceObjectIdentifier = $MIMRequest.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.TargetObjectIdentifier = $MIMRequest.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.State = 1

    #SetSingleValue $obj "seasExtGUID" $null

    if($seasReqStatus -eq 9)
    {
        if($MIMRequest.seasIsValid -ne $true)
        {
            SetSingleValue $obj "seasReqStatus" $seasReqStatus
        }  
        #SetSingleValue $obj "seasRenewValidity" $null
    }   

    if($seasReqStatus -eq 6)
    {
        SetSingleValue $obj "seasIsValid" $true
        SetSingleValue $obj "seasUserIsValid" $true
        SetSingleValue $obj "seasReqStatus" $seasReqStatus
    }    


    if($seasReqStatus -eq 8)
    {

        if($IDMRequest.externalRequestId -eq $MIMRequest.seasExtGUID)
        {
            $message = "Predľženie žiadosti bolo zamietnuté z dôvodu: $message"
            if($MIMRequest.seasIsValid -and $MIMRequest.seasUserIsValid)
            {
                SetSingleValue $obj "seasReqStatus" $seasReqStatus
            } 
            else
            {
                SetSingleValue $obj "seasReqStatus" $seasReqStatus
            }
        }
        else
        {
            SetSingleValue $obj "seasReqStatus" $seasReqStatus
            SetSingleValue $obj "seasIsValid" $false
        
        }
    } 
    
    if(![string]::IsNullOrEmpty($message))
    {
        SetSingleValue $obj "seasReqApprovalInfo" $message
    }


    $error.Clear()
    try
    {
        $obj | Import-FIMConfig -Uri $DefaultUri
        if($error.Count -gt 0)
        {
            Log -text "Chyba pri aktualizaci zadosti $($MIMRequest.DisplayName)"
            Log -text "$($error[0].exception.message)"
        }
        else
        {
            Log -text "Uspesne nastaveni zadosti $($MIMRequest.DisplayName) na stav: $seasReqStatus"
        }
    }
    catch
    {
        Log -text "Chyba pri aktualizaci zadosti $($MIMRequest.DisplayName)"
        Log -text "$($error[0].exception.message)"
    } 
}

function UpdateRequestStatus($MIMRequest,$level)
{
    $level = [int]$level
    $obj = New-Object Microsoft.ResourceManagement.Automation.ObjectModel.ImportObject
    $obj.ObjectType = $MIMRequest.MIMObject.ResourceManagementObject.ObjectType
    $obj.SourceObjectIdentifier = $MIMRequest.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.TargetObjectIdentifier = $MIMRequest.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.State = 1

    SetSingleValue $obj "seasReqStatus" (20 + $level)
    
    $error.Clear()
    try
    {
        $obj | Import-FIMConfig -Uri $DefaultUri
        if($error.Count -gt 0)
        {
            Log -text "Chyba pri aktualizaci stavu UpdateRequestStatus $($MIMRequest.DisplayName)"
            Log -text "$($error[0].exception.message)"
        }
        else
        {
            Log -text "Uspesna aktualizace stavu zadosti $($MIMRequest.DisplayName) na stav: $(20 + $level)"
        }
    }
    catch
    {
        Log -text "Chyba pri aktualizaci stavu UpdateRequestStatus $($MIMRequest.DisplayName)"
        Log -text "$($error[0].exception.message)"
    }

}
function AddRequestWF($MIMRequest, $WFID, $level)
{
    $level = [int]$level
    $obj = New-Object Microsoft.ResourceManagement.Automation.ObjectModel.ImportObject
    $obj.ObjectType = $MIMRequest.MIMObject.ResourceManagementObject.ObjectType
    $obj.SourceObjectIdentifier = $MIMRequest.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.TargetObjectIdentifier = $MIMRequest.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.State = 1

    AddMultiValue $obj "seasWorkflows" $WFID.Substring(9)
    SetSingleValue $obj "seasReqStatus" (20 + $level)
    
    $error.Clear()
    try
    {
        $obj | Import-FIMConfig -Uri $DefaultUri
        if($error.Count -gt 0)
        {
            Log -text "Chyba pri vytvareni WF zadosti $($MIMRequest.DisplayName)"
            Log -text "$($error[0].exception.message)"
        }
        else
        {
            Log -text "Uspesne vytvoreni WF $($MIMRequest.DisplayName) na stav: $seasReqStatus"
        }
    }
    catch
    {
        Log -text "Chyba pri vytvareni WF zadosti $($MIMRequest.DisplayName)"
        Log -text "$($error[0].exception.message)"
    }

}
function GetApprovers($IDMRequest)
{
    $ret = @()
    foreach($waiting in $IDMRequest.waitingFor)
    {
            
        $MIMObj = GetMIMObject -filter "/Person[AccountName='$($waiting.techName)']"
        if($MIMObj -ne $null){
            $ret+= $MIMObj.ObjectID.substring(9)}


        foreach($deputie in $waiting.deputies)
        {
            
            $MIMObj = GetMIMObject -filter "/Person[AccountName='$($deputie.techName)']"
            if($MIMObj -ne $null){
                $ret+= $MIMObj.ObjectID.substring(9)}
        }
    }




    return $ret
}
function getLevel($status)
{
    $levels = ($status -split "-")
    $level = $levels[$levels.Length-1]
    if($level -eq "expired")
    {
        $level = 9
    }        
    $level = "0$level"
    

    return $level
}


function CreateRoleWorkflow($MIMSQLrequest,$IDMRequest)
{

    $MIMRequest = GetRequestbyID -ID $MIMSQLrequest.ObjectID
    $culture = (Get-Culture).DateTimeFormat
    $level = getLevel -status $IDMRequest.approvalStatus 
    $approle =  GetMIMObject -filter "/*[ObjectID='$($MIMRequest.seasReqRole.SUbstring(9))']"
    $member = GetMIMObject -filter "/*[ObjectID='$($MIMRequest.seasReqMembers.Substring(9))']"

    $displayName = "$level Žiadosť o roli $($approle.DisplayName) -> $($member.DisplayName)"
    $rolewf = $null
    $rolewf = $MIMrequest.seasWorkflowObjects | where {$_.DisplayName -eq $displayName -and $_.seasReqStatus -eq 21}
    if($rolewf -eq $null)
    {

        $NewWF = CreateImportObject -ObjectType "RoleWorkflow"
    
        SetSingleValue $NewWF "seasValidFrom" (Get-Date).ToString("$($culture.ShortDatePattern) $($culture.LongTimePattern)")
        SetSingleValue $NewWF "DisplayName" $displayName
        SetSingleValue $NewWF "seasValidTo" (Get-Date).AddDays(30).ToString("$($culture.ShortDatePattern) $($culture.LongTimePattern)")
        SetSingleValue $NewWF "seasReqMembers" $MIMRequest.seasReqMembers.Substring(9)
        SetSingleValue $NewWF "seasReqReason" $MIMRequest.seasReqReason
        SetSingleValue $NewWF "seasReqStatus" 21
        SetSingleValue $NewWF "seasIsValid" $true
        SetSingleValue $NewWF "seasReqRole" $approle.ObjectID.Substring(9)
        
        if($MIMRequest.seasReqRequestor -ne $null)
        {
            SetSingleValue $NewWF "seasRoleRequestor" $MIMRequest.seasReqRequestor.Substring(9)
        }
        else
        {
            SetSingleValue $NewWF "seasRoleRequestor" $MIMRequest.Creator.Substring(9)
        }


        foreach($approver in GetApprovers -IDMRequest $IDMRequest)
        {
            AddMultiValue $NewWF "seasApprovers" $approver

        }
    
        SetSingleValue $NewWF "seasParent" $MIMRequest.ObjectID.Substring(9)
        SetSingleValue $NewWF "EmployeeStartDate" ([datetime]$IDMRequest.from).ToString("$($culture.ShortDatePattern) $($culture.LongTimePattern)")
        SetSingleValue $NewWF "EmployeeEndDate" ([datetime]$IDMRequest.to).ToString("$($culture.ShortDatePattern) $($culture.LongTimePattern)")
        SetSingleValue $NewWF "seasValidStatus" 21
        #SetSingleValue $NewWF "seasRenewValidity"

        try
        {
            $error.Clear()

            $NewWF | Import-FIMConfig -Uri $DefaultUri
            if($error.Count -gt 0)
            {
                Log -text "Chyba pri vytvareni WF $($MIMRequest.DisplayName)"

                Log -text "$($error[0].exception.message)"
            }
            else
            {
                Log -text "Uspesne vytvoreni WF  $($MIMRequest.DisplayName)"

                AddRequestWF -MIMRequest $MIMRequest -WFID $NewWF.TargetObjectIdentifier -level $level
            }
        }
        catch
        {
                Log -text "Chyba pri vytvareni WF $($MIMRequest.DisplayName)"
                Log -text "$($error[0].exception.message)"
        }
    }
    else
    {
        UpdateRequestStatus -MIMRequest $MIMRequest -level $level
    }


}

function ConvertToDateTime($dt)
{
    try{
        return [datetime]$dt
    }

    catch
    {
        $ret = $null
        if($dt -like "*.*")
        {
            $ret = [datetime]::parseexact($dt, 'd.MM.yyyy H:mm:ss', $null)
        }
        return $ret
    }

}

function GetSODConflict ($MIMRequest,$MIMRole,$MIMUser)
{
    $retConflicts = [System.Collections.Generic.List[Object]]::new()

    $MIMRoleObjectID=$MIMRole.ObjectID

    $conflicts =@()

    $MIMconflicts = GetMIMObject -filter "/RoleConflicting[seasConflict1='$($MIMRoleObjectID.SUbstring(9))']"

    foreach($MIMconflict in $MIMconflicts)
    {
        $conflicts+=$MIMconflict.seasConflict2
    }    

    $MIMconflicts = GetMIMObject -filter "/RoleConflicting[seasConflict2='$($MIMRoleObjectID.SUbstring(9))']"

    foreach($MIMconflict in $MIMconflicts)
    {
        $conflicts+=$MIMconflict.seasConflict1
    }    

    $conflicts = $conflicts | Sort-Object | Get-Unique -AsString

    foreach($conflict in $conflicts) 
    {
        $MIMSODRole = $null
        $MIMSODRole = GetMIMObject -filter "/Group[ObjectID='$($conflict.Substring(9))' and ExplicitMember='$($MIMUser.ObjectID.Substring(9))']"
        if($MIMSODRole -ne $null -and $MIMSODRole.ObjectID -ne $MIMRole.ObjectID)
        {
            $retConflicts.Add($MIMSODRole)
        }

    }

    foreach($conflict in $conflicts)
    {
        $MIMSODRequest = $null
        $MIMSODRequest = GetMIMObject -filter "/RoleRequest[seasReqRole='$($conflict.Substring(9))' and seasReqMembers='$($MIMUser.ObjectID.Substring(9))']"
        if($MIMSODRequest -ne $null -and $MIMSODRequest.ObjectID -ne $MIMRequest.ObjectID)
        {
            $MIMSODRole = $null
            $MIMSODRole = GetMIMObject -filter "/Group[ObjectID='$($conflict.Substring(9))']"
            
            $existing = $null
            $existing = $retConflicts | where {$_.ObjectID -eq $MIMSODRole.ObjectID}
            if($existing -eq $null)
            {
                $retConflicts.Add($MIMSODRole)
            }
        }

    }

    return $retConflicts
}

function GetSODConfilct_From_Request ($MIMRequest,$MIMRole,$MIMUser)
{
    $retConflicts = [System.Collections.Generic.List[Object]]::new()

    $MIMRoleObjectID=$MIMRole.ObjectID

    $conflicts =@()

    $MIMconflicts = GetMIMObject -filter "/RoleConflicting[seasConflict1='$($MIMRoleObjectID.SUbstring(9))']"
    foreach($MIMconflict in $MIMconflicts)
    {
        $retConflicts+=$MIMconflict.seasConflict2
    }    


    $MIMconflicts += GetMIMObject -filter "/RoleConflicting[seasConflict2='$($MIMRoleObjectID.SUbstring(9))']"
    foreach($MIMconflict in $MIMconflicts)
    {
        $conflicts+=$MIMconflict.seasConflict1
    }    

    $conflicts = $conflicts | Sort-Object | Get-Unique -AsString

    foreach($conflict in $conflicts)
    {
        $MIMSODRequest = $null
        $MIMSODRequest = GetMIMObject -filter "/RoleRequest[seasReqRole='$($conflict.Substring(9))' and seasReqMembers='$($MIMUser.ObjectID.Substring(9))']"
        if($MIMSODRequest -ne $null -and $MIMSODRequest.ObjectID -ne $MIMRequest.ObjectID)
        {
            $retConflicts.Add($MIMSODRequest)
        }

    }

    return $retConflicts
}
function CreateRequestonIDM ($MIMSQLrequest)
{

    <#
    param
    (
        $seasRenewValidityDt,
        $prolongguid,
        $guid,
        $displayName,
        $fromDt,
        $toDt,
        $roleID,
        $userID,
        $requestReason,
        $requestedBy
    )
    #>

    $MIMRequest = GetRequestbyID -ID $MIMSQLrequest.ObjectID

    $items = $MIMRequest.seasReqCreator.split(";")
    $requestedBy = $null
    $requestor = $null
    
    try
    {
        if($MIMRequest.seasReqRequestor -ne $null)
        {
            $requestor = GetMIMObject -filter "/Person[ObjectID = '$($MIMRequest.seasReqRequestor.Substring(9))']"
            $requestedBy = $requestor.seasExtGUID
        }
    }
    catch
    {}

    if($requestedBy -eq $null)
    {  
        $items = $MIMRequest.seasReqCreator.split(";") 
        if($items.Count -eq 4)
        {
            $requestor = GetMIMObject -filter "/Person[AccountName = '$($items[1])']"
            $requestedBy = $requestor.seasExtGUID
            if($requestedBy -eq $null)
            {
                $requestor = GetMIMObject -filter "/Person[AccountName = '$($items[3])']"
                $requestedBy = $requestor.seasExtGUID
            }
        }
    }

    if($requestedBy -ne $null)
    {
        $requestedBy = $requestor.seasExtGUID
    }


    $MIMRole = $null
    $MIMUser = $null

    $MIMRole =  GetMIMObject -filter "/*[ObjectID = '$($MIMRequest.seasReqRole.Substring(9))']"
    $MIMUser =  GetMIMObject -filter "/*[ObjectID = '$($MIMRequest.seasReqMembers.Substring(9))']"






    #$requestedBy = $requestor.seasExtGUID
    $roleID = $MIMRole.seasExtGUID
    $seasRenewValidity = $MIMRequest.seasRenewValidity
    $guid = $MIMRequest.ObjectID
    $displayName = $MIMRequest.DisplayName
    $from = $MIMRequest.seasValidFrom
    $to = $MIMRequest.seasValidTo
    $userID = $MIMUser.seasExtGUID
    $requestReason = $MIMRequest.seasReqReason
    $prolongguid = $MIMRequest.seasExtGUID




    if($roleID -eq $null -or $userID -eq $null)
    {
        if($roleID -eq $null){$reason = "Rola nie je vytvorená na externom portáli"}
        if($userID -eq $null){$reason = "Používateľ nie je vytvorený na externom portáli"}

        Log -text ($reason)
        $mytable=@{isError=$true;status="$reason";mimstatus=20}

        Return $mytable
    }

    add-type -assembly system.web.extensions
    $ps_js=new-object system.web.script.serialization.javascriptSerializer
    $mimstatus = 20

    
    if($requestedBy -eq $null)
    {
        $requestedBy = $userID
    }

    <#
    $guid=([GUID]::NewGuid()).Guid
    $roleID = "0C23A209-2FDA-4B92-ABE7-6EE53839D4C5"
    #$roleID = "0C23A209-2FDA-4B92-ABE7-6EE53839D4C5"
    $userID = "2D21EAB1-4DA5-4787-B378-5C2AED165465"
    #$userID = "969FB1A7-B13F-4BF1-B6DB-97C1A6D00C8A"
    $from = "2023-8-1" 
    $to = "2024-12-31"
    $requestReason = "pokus"
    $displayName
    #>

    $from = $null
    $to = $null

    
    $fromDt = ConvertToDateTime -dt $MIMRequest.seasValidFrom
    $toDt = ConvertToDateTime -dt $MIMRequest.seasValidTo
    if($fromDt -ne $null){$from =$fromDt.AddHours(2).toString("yyyy-M-d")}
    if($toDt -ne $null){$to =$toDt.AddHours(2).toString("yyyy-M-d")}
    
    
    if($seasRenewValidity)
    {
        $seasRenewValidity = ConvertToDateTime -dt $MIMRequest.seasRenewValidity
        $seasRenewValidity=$seasRenewValidity.AddHours(2).toString("yyyy-M-d")
        Log -text "renew: $seasRenewValidity"
    }
    if($from -eq $null -or $from -eq "" ){$from = (Get-Date).toString("yyyy-M-d"); $fromDt = (Get-Date)}

    if($to -eq $null -or $to -eq "" ){$to = ( (Get-Date).AddYears(1)).toString("yyyy-M-d")}


    $userID = $userID.Replace("urn:uuid:","")
    $roleID = $roleID.Replace("urn:uuid:","")
    $requestedBy = $requestedBy.Replace("urn:uuid:","")
    $userID = $userID.Replace("urn:uuid:","")
    if($prolongguid -like "urn:uuid:*")
    {
        $prolongguid = $prolongguid.Replace("urn:uuid:","")
    }
    $guid = $guid.Replace("urn:uuid:","")

    $SODConflictRoles = [System.Collections.Generic.List[Object]](GetSODConflict -MIMRequest $MIMRequest -MIMRole $MIMRole -MIMUser $MIMUser)
 

    $PSObject = New-Object PSObject
    $PSObject | Add-Member NoteProperty "identityId" $userID
    $PSObject | Add-Member NoteProperty "requestedObjectId" $roleID
    $PSObject | Add-Member NoteProperty "requestReason" $requestReason
    $PSObject | Add-Member NoteProperty "requestedBy" $requestedBy
    $PSObject | Add-Member NoteProperty "from" $from
    $PSObject | Add-Member NoteProperty "to" $to
    if($prolongguid -like "*-*")
    {
        $PSObject | Add-Member NoteProperty "ExtendTo" $seasRenewValidity
        $PSObject | Add-Member NoteProperty "externalRequestId" $prolongguid
    }
    else
    {
        $PSObject | Add-Member NoteProperty "externalRequestId" $guid
    }

    if($SODConflictRoles.count -gt 0)
    {
        $SODObject = New-Object PSObject
        $SODObject | Add-Member NoteProperty "description" "Požadovaná rola je v konflikte s rolou $($SODConflictRoles[0].DisplayName)"
        #$SODObject | Add-Member NoteProperty "conflictingRoleId" "73313af0-db39-4d59-988b-d2ec96e2f10a"
        if($SODConflictRoles.count -gt 1)
        {
            $SODObject.description = "Požadovaná rola je v konflikte s nasledujúcimi rolami: $($SODConflictRoles.DisplayName -join ';')"
        }
        $PSObject | Add-Member NoteProperty "sodViolation" $SODObject
    }

    $json = $PSObject | ConvertTo-Json -Compress

    <#
    if($prolongguid -like "*-*")
    {
        $json = 
        "{
            ""identityId"": ""$userID"",
            ""externalRequestId"": ""$prolongguid"",
            ""requestedObjectId"": ""$roleID"",
            ""requestReason"": ""$requestReason"",
            ""requestedBy"": ""$requestedBy"",
            ""from"": ""$from"",
            ""to"": ""$to"",
            ""ExtendTo"": ""$seasRenewValidity""
        }"

    }
    else
    {
        $json = 
        "{
            ""identityId"": ""$userID"",
            ""externalRequestId"": ""$guid"",
            ""requestedObjectId"": ""$roleID"",
            ""requestReason"": ""$requestReason"",
            ""requestedBy"": ""$requestedBy"",
            ""from"": ""$from"",
            ""to"": ""$to""
        }"

    }
    #>
    <#
        $json = 
        "{
            ""identityId"": ""781da473-067e-4d7f-9e1b-659f673d4e72"",
            ""externalRequestId"": ""8b796304-74f7-446f-ba31-fa380cc68a17"",
            ""requestedObjectId"": ""6E0C5BDA-F747-40C7-9164-08C2E9CACCEE"",
            ""requestReason"": ""test 958 (Hromadná žiadosť)"",
            ""requestedBy"": ""71246747-A32A-4396-855C-45FF5B5BA3FD"",
            ""from"": ""2023-11-28"",
            ""to"": ""2023-12-31""
        }"

    #>

    Log -text $json

    $Error.Clear()
    try{
        $isError = $true


        $request = [System.Net.WebRequest]::Create("$($EIDMURL)/api/MimSync/request")
        $request.Method = "POST"
        $request.ContentType = "application/json"


        #$jObject = [Newtonsoft.Json.Linq.JObject]::Parse($json)
        #$objects =  $jObject["Objects"];

        $requestStream = [System.IO.StreamWriter]$request.GetRequestStream()
        $requestStream.Write($json)
        $requestStream.Flush() 
        $requestStream.Close()

            $response =[System.Net.HttpWebResponse]$request.GetResponse()
        $reader = [IO.StreamReader] $response.GetResponseStream()

        #Our response will always be xml except in 404/401 case so cast as such
        $responseJson = $reader.ReadToEnd()
        #$responseXML
        log -text $responseJson 

        $r = $ps_js.DeserializeObject($responseJson)

        $mimstatusTXT = ""
        $status = $r["Report"]["Entries"][0]["Code"]

        $guid = $guid.Replace("urn:uuid:","")
        $r | Export-Clixml "$($logfolder)\XML\$($guid)-$($status).xml"
        if($status -eq "casting-process-initalized" -or $status -eq "4")
        {
            $mimstatusTXT = "Vytvorený schvaľovací proces"
            $mimstatus=21
        }
        else
        {
            $mimstatusTXT = "Chyba v procese: $status"
        }

        #$ret | Export-Clixml "C:\Temp\$($guid)-$($ret.Report.Entries[0].Code).xml"
        #$status = $ret.Report.Entries[0].Code
        if($ret.ExternalRequestId -eq $guid)
        {
            $isError = $false
        }

    }
    catch [System.Net.WebException] {
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $mimstatusTXT = "Chyba: $($Error[0].Exception.Message)"
        $statusCode = $_.Exception.Response.StatusCode
        $respStream = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($respStream)
        $reader.BaseStream.Position = 0
        $mimstatusTXT = "Chyba: $($reader.ReadToEnd())"
        Log -text $mimstatusTXT
        if(!$mimstatusTXT)
        {
            $mimstatusTXT = "Chyba: $($Error[0].Exception.Message)"
        }

    }
    catch
    {
       
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $mimstatusTXT = "Chyba: $($Error[0].Exception.Message)"



    }

    $mytable=@{isError=$isError;status=$mimstatusTXT;mimstatus=$mimstatus}

    Return $mytable
}


function CreateRequestonIDMOLD ($MIMRequest)
{
    $approle = $null
    $member = $null
    $approle =  GetMIMObject -filter "/Group[ObjectID='$($MIMRequest.seasReqRole.SUbstring(9))']"
    $member = GetMIMObject -filter "/Person[ObjectID='$($MIMRequest.seasReqMembers.Substring(9))']"
    $culture = (Get-Culture).DateTimeFormat

    if($approle -eq $null -or $member -eq $null)
    {
        Log -text "$($MIMRequest.DisplayName) - nenalezena role nebo uzivatele"
        return
    }

    $from = $null
    $to = $null

    if($from -eq $null) 
        {$from = (Get-Date).toString("yyyy-M-d")}
    else
        {$from = ([datetime]::parseexact($MIMRequest.seasValidFrom, "$($culture.ShortDatePattern) $($culture.LongTimePattern)", $null)).toString("yyyy-M-d")}
    
    $to = ([datetime]::parseexact($MIMRequest.seasValidTo, "$($culture.ShortDatePattern) $($culture.LongTimePattern)", $null)).toString("yyyy-M-d")



    $json = 
    "{
        ""identityId"": ""$($member.seasExtGUID)"",
        ""externalRequestId"": ""$($MIMRequest.ObjectID.Substring(9))"",
        ""requestedObjectId"": ""$($approle.seasExtGUID)"",
        ""requestReason"": ""$($MIMRequest.seasReqReason)"",
        ""from"": ""$from"",
        ""to"": ""$to""
    }"

    Log -text $json

    $Error.Clear()
    try{
        $isError = $true


        $request = [System.Net.WebRequest]::Create("$($EIDMURL)/api/MimSync/request")
        $request.Method = "POST"
        $request.ContentType = "application/json"


        #$jObject = [Newtonsoft.Json.Linq.JObject]::Parse($json)
        #$objects =  $jObject["Objects"];

        $requestStream = [System.IO.StreamWriter]$request.GetRequestStream()
        $requestStream.Write($json)
        $requestStream.Flush() 
        $requestStream.Close()

            $response =[System.Net.HttpWebResponse]$request.GetResponse()
        $reader = [IO.StreamReader] $response.GetResponseStream()

        #Our response will always be xml except in 404/401 case so cast as such
        $responseJson = $reader.ReadToEnd()
        #$responseXML
        log -text $responseJson 
    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd() | ConvertFrom-Json
            Log -text "Chyba vytvareni zadosti $($MIMRequest.DisplayName)"
            Log -text $respBody.message
     }
    catch
    {
        Log -text "Chyba vytvareni zadosti $($MIMRequest.DisplayName)"
        Log -text $Error[0].Exception.Message
    }

}


function SetWFSTatus($rolewf, $IDMRequest, $wflevel)
{

    $approver = $null
    $message = $null
    if($IDMRequest.approvalStatus -eq "denied")
    {
        $IDMaprovedby =  $IDMRequest.deniedBy[$IDMRequest.deniedBy.count - 1]
        $acc = $IDMaprovedby.TechName
        if($acc -eq "eidma"){$acc = "SYS_MIM.Install"}
        $approver = GetMIMObject -filter "/Person[AccountName='$($acc)']"          
        $message = $IDMRequest.deniedBy[$IDMRequest.deniedBy.count - 1].message   
    }
    elseif($IDMRequest.approvedBy.Count -ge $wflevel)
    {
            $IDMaprovedby =  $IDMRequest.approvedBy[$wflevel - 1]
            $acc = $IDMaprovedby.TechName
            if($acc -eq "eidma"){$acc = "SYS_MIM.Install"}
            $approver = GetMIMObject -filter "/Person[AccountName='$($acc)']"          
            $message = $IDMRequest.approvedBy[$wflevel - 1].message
    }

    #$approle =  GetMIMObject -filter "/Group[ObjectID='$($MIMRequest.seasReqRole.SUbstring(9))']"
    #$member = GetMIMObject -filter "/Person[ObjectID='$($MIMRequest.seasReqMembers.Substring(9))']"

 
    $obj = New-Object Microsoft.ResourceManagement.Automation.ObjectModel.ImportObject
    $obj.ObjectType = $rolewf.MIMObject.ResourceManagementObject.ObjectType
    $obj.SourceObjectIdentifier = $rolewf.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.TargetObjectIdentifier = $rolewf.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.State = 1

    if($IDMRequest.approvalStatus -eq "denied")
    {
        SetSingleValue $obj "seasReqStatus" 23
        SetSingleValue $obj "DisplayName" $rolewf.DisplayName.Replace("Žiadosť o roli","Zamietnuté")
    }
    elseif($IDMRequest.approvalStatus -eq "expired")
    {
        SetSingleValue $obj "seasReqStatus" 24
        SetSingleValue $obj "DisplayName" $rolewf.DisplayName.Replace("Žiadosť o roli","Bez reakcie")
    }
    else
    {
        SetSingleValue $obj "seasReqStatus" 22
        SetSingleValue $obj "DisplayName" $rolewf.DisplayName.Replace("Žiadosť o roli","Schválené")
    }

    if($approver -ne $null)
    {
        SetSingleValue $obj "seasApprovedBy" $approver.ObjectID.Substring(9)
    }

    if(![string]::IsNullOrEmpty($message))
    {
        SetSingleValue $obj "seasReqApprovalInfo" $message
    }
    
    $error.Clear()
    try
    {
        $obj | Import-FIMConfig -Uri $DefaultUri
        if($error.Count -gt 0)
        {
            Log -text "Chyba SetWFSTatus $($MIMRequest.DisplayName)"
            Log -text "$($error[0].exception.message)"
        }
        else
        {
            Log -text "Uspesne SetWFSTatus $($MIMRequest.DisplayName) na stav: $seasReqStatus"
        }
    }
    catch
    {
        Log -text "Chyba pri SetWFSTatus $($MIMRequest.DisplayName)"
        Log -text "$($error[0].exception.message)"
    }

}

 function updateApprovedWF($MIMRequest,$IDMRequest)
 {
    if($IDMRequest.approvalStatus -eq "valid" -or $IDMRequest.approvalStatus -eq "denied")
    {
        $roleWFs =  $MIMrequest.seasWorkflowObjects | where {$_.seasReqStatus -eq 21}
        foreach($rolewf in $roleWFs)
        {
            $wflevel = [int]$rolewf.DisplayName.Substring(0,2)
            SetWFSTatus -rolewf $rolewf -IDMRequest $IDMRequest -wflevel $wflevel
        }
    }
    else
    {
        $level = getLevel -status $IDMRequest.approvalStatus
        for($wflevel = 1; $wflevel -lt ([int]$level); $wflevel++)
        {
            $rolewf = $null
            $rolewf = $MIMrequest.seasWorkflowObjects | where {$_.DisplayName -like "0$($wflevel)*" -and $_.seasReqStatus -eq 21}
            if($rolewf -ne $null)
            {
                SetWFSTatus -rolewf $rolewf -IDMRequest $IDMRequest -wflevel $wflevel
            }
        }
    }


 }

 function GetDate($strval)
{
    $ret = $null
    if($null -ne $strval)
    {
        $ret = ([datetime]$strval).Tostring("yyyy-MM-ddTHH:mm:ss")
    }
    return $ret
}

function GetReference($MIMID)
{
    if($MIMID -eq $null)
    {
        return $null
    }
    if($MIMID -like "urn:uuid:*")
    {
        $MIMID = $MIMID.SUbstring(9)
    }

    $MIMAccountName = $MIMIDs | where {$_.csObjectID -eq $MIMID }
    $IDMID = $IDMIDs | where {$_.Techname -eq $MIMAccountName.AccountName }

    if($IDMID -ne $null)
    {
        return $IDMID.IdmObjectID
    }
    return $null
}

function MapReferenciesFromFile($refArr)
{
    $ret = @()

    foreach($MIMID in $refArr)
    {
        if($MIMID -like "urn:uuid:*")
        {
            $MIMID = $MIMID.SUbstring(9)
        }
        
        $IDMVal = $mapping["$MIMID"]
        if($null -ne $IDMVal)
        {

            $ret += $IDMVal
        }

    }
    return $ret
}

function MapReferencies($refArr)
{
if($refArr -like "*;*")
{
    $refArr = $refArr -split ";"

}

    $ret = @()

    foreach($MIMID in $refArr)
    {
        if($MIMID -like "urn:uuid:*")
        {
            $MIMID = $MIMID.SUbstring(9)
        }
        
<#
  SELECT AccountName FROM [dbo].[MIGMIMIDs] where ObjectID = '232EC128-A224-4BCD-B4CB-E7AA2434DAE5'
    SELECT idmoBJECTid FROM [dbo].[v_MIGEASYIDMObjects] where Techname = '4500354224 - VUJE, a.s.' or name = '4500354224 - VUJE, a.s.'
SELECT Value
  FROM [EasyIDM].[dbo].[AttributeValueAssignments]
  where IDMObjectId = '7CB42A4D-4C1D-443F-8444-89243178B5E6' and AttributeId = 'E76EBAC8-B243-42B8-993C-08DDB86E64BD'
#>

        $MIMAccount = ExecuteReader -commandtext "SELECT AccountName FROM [dbo].[MIGMIMIDs] where ObjectID = '$MIMID'"
        if($null -eq $MIMAccount.AccountName){
           continue
        }

        $MIMAccountName = $MIMAccount.AccountName
        
        $IDMGuid = ExecuteReader -commandtext "SELECT IDMObjectId FROM [EasyIDM].[dbo].[IDMObjects] where [TechName] = '$($MIMAccountName)' or [Name] = '$($MIMAccountName)'"
        if($null -ne $IDMGuid.IDMObjectId.Guid){
            $ret += $IDMGuid.IDMObjectId.Guid
        }

    }
    return $ret
}

function GetOrganization($ID)
{
    if($null -eq $id)
    {
        return $null
    }
    $org = ExecuteReader -commandtext "SELECT Value FROM [EasyIDM].[dbo].[AttributeValueAssignments] where IDMObjectId = '$ID' and AttributeId = 'E76EBAC8-B243-42B8-993C-08DDB86E64BD'"

    return $org.Value

}

function GetContractFromEIDM ($contractId)
{

    if([string]::IsNullOrEmpty($contractId)){return $null}
    try{
        $allObject = $null
        $page = 1
    
        $canContinue = $true

        $request = [System.Net.WebRequest]::Create("$($EIDMURL)/api/MimSync/contract/$contractId")
        $request.Method = "GET"
        $request.ContentType = "application/json"
        $request.Headers.Add("Pagination", "{ 'ItemsPerPage': 10,'CurrentPage': $page }")
        $response = $null
        $reader = $null
        $responseJson = $null
        $response =[System.Net.HttpWebResponse]$request.GetResponse()
        $reader = [IO.StreamReader] $response.GetResponseStream()

        $responseJson = $reader.ReadToEnd()
        #$responseXML

        $allObject = $responseJson | ConvertFrom-Json
        $isError = $false
        $status = "OK"



    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd()
            $respBody = $respBody | ConvertFrom-Json

            Log -text "ChybaGetAllRequestsFromEIDM"
            Log -text $respBody.message
     }
    catch
    {
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $status = "Chyba: $($Error[0].Exception.Message)"
    }

$mytable=@{isError=$isError;status=$status;requests=$allObject}

Return $mytable
}


function GetRoleFromEIDM ($id)
{

    if([string]::IsNullOrEmpty($id)){return $null}
    try{
        $allObject = $null
        $page = 1
    
        $canContinue = $true

        $request = [System.Net.WebRequest]::Create("$($EIDMURL)/api/MimSync/Identity/$id")
        $request.Method = "GET"
        $request.ContentType = "application/json"
        $request.Headers.Add("Pagination", "{ 'ItemsPerPage': 10,'CurrentPage': $page }")
        $response = $null
        $reader = $null
        $responseJson = $null
        $response =[System.Net.HttpWebResponse]$request.GetResponse()
        $reader = [IO.StreamReader] $response.GetResponseStream()

        $responseJson = $reader.ReadToEnd()
        #$responseXML

        $allObject = $responseJson | ConvertFrom-Json
        $isError = $false
        $status = "OK"



    }
    catch [System.Net.WebException] {   
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $respBody = $reader.ReadToEnd()
            $respBody = $respBody | ConvertFrom-Json

            Log -text "ChybaGetAllRequestsFromEIDM"
            Log -text $respBody.message
     }
    catch
    {
        Log -text "Chyba $displayName "
        Log -text $Error[0].Exception.Message
        $isError = $true
        $status = "Chyba: $($Error[0].Exception.Message)"
    }

$mytable=@{isError=$isError;status=$status;requests=$allObject}

Return $mytable
}

function getRequestStatus($errDescription, $action, $ticketNr)
{
    $status = $null
     
    if($errDescription -ne $null)
    {
        $description = $null
        $status = $null
    }
    else
    {
        if($action -eq "ADD")
        {
            $description = "Úspešné vytvorenie tiketu $ticketNr"
            $status = "3"
        }
        elseif($action -eq "PROLONG")
        {
            $description = "Úspešné vytvorenie tiketu $ticketNr"
            $status = "4"
        }
        elseif($action -eq "REMOVE")
        {
            $description = "Úspešné vytvorenie tiketu $ticketNr"
            $status = "5"
        }

    }

    

    return $status, $description
}
function SetTicketResult($requests, $ticketNr, $errDescription, $reason, $description)
{
#solvedText,error,ticketNumber
    foreach($r in $requests)
    {
        $status, $description = getRequestStatus -errDescription $errDescription -action $r.Action -ticketNr $ticketNr

        if($r.Attributes.PSobject.Properties.name -notcontains "solvedText")
        {
            $r.Attributes | Add-Member NoteProperty "solvedText" $null
        }
        if($r.Attributes.PSobject.Properties.name -notcontains "isSolved")
        {
            $r.Attributes | Add-Member NoteProperty "isSolved" $false
        }

        if($r.Attributes.PSobject.Properties.name -notcontains "error")
        {
            $r.Attributes | Add-Member NoteProperty "error" $null
        }

        if($ticketNr -eq $null)
        {
            $r.Attributes.error = "$((Get-Date).ToString('dd.MM.yyyy HH:mm:ss')) $errDescription"
            $r.Attributes.isSolved=$false
        }
        else
        {
            $r.Attributes.ticketNumber = $ticketNr
            $r.Attributes.solvedText = $description    
            $r.Attributes.isSolved=$true
            $r.Attributes.error = $null
        }

        $json = $r | ConvertTo-Json -Compress
        $json = "[$json]"
        $ret =  ExportToPortal -objectType 33 -json $json

        #SetMIMREquestStatus -MIMRequest $r -errDescription $errDescription -status $status -description $description -ticketNr $ticketNr -reason $reason
        

    } 
}

<#

 if(![string]::IsNullOrEmpty($status))
    {
        SetSingleValue $obj "seasServioStatus" $status
    }

    if(![string]::IsNullOrEmpty($description))
    {
        AddMultiValue $obj "seasServioInfo"  "[$((Get-Date).Tostring('dd.MM.yyyy HH:mm:ss'))] $description"
    }
    if(![string]::IsNullOrEmpty($errDescription))
    {
        SetSingleValue $obj "seasSERVIOLastResult" "[$((Get-Date).Tostring('dd.MM.yyyy HH:mm:ss'))] $errDescription"
    }
    else
    {
        SetSingleValue $obj "seasSERVIOLastResult" " "
    }

    if(![string]::IsNullOrEmpty($ticketNr))
    {
        SetSingleValue $obj "seasServioTicketNr" $ticketNr
    }

    if(![string]::IsNullOrEmpty($reason))
    {
        SetSingleValue $obj "seasReqApprovalInfo" $reason
    }

#>