$EIDMURL = "http://seweidmt1v"
$eidmTokenSkewSeconds = 50

# Ignorovat chyby SSL certifikatu – ICertificatePolicy neni dostupne v .NET Core (.NET 5+),
# ServerCertificateValidationCallback funguje na .NET Framework i .NET Core.
try
{
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
}
catch { }

function Get-AuthorizationHeader {
    $Uri = "$EIDMURL/connect/token"
    $Body = @{
        client_id     = "EasyIDMPortalVNext"
        client_secret = "easyIDMClientSecret!123"
        grant_type    = "client_credentials"
        scope         = "easyidm_portal"
    }
    $authToken = Invoke-RestMethod -Method Post -Uri $Uri -Body $Body
    if ($null -ne $authToken.access_token)
    {
        $lifetime = 0
        if ($null -ne $authToken.expires_in) { $lifetime = [int]$authToken.expires_in }
        $skew = $eidmTokenSkewSeconds
        if ($lifetime -le $skew) { $skew = 0 }
        $authToken | Add-Member NoteProperty "expirationTime" ((Get-Date).AddSeconds($lifetime - $skew))
    }
    return $authToken
}

function Update-EidmToken {
    $stillValid = $null -ne $script:eidmAuth -and
        -not [string]::IsNullOrEmpty($script:eidmAuth.access_token) -and
        $null -ne $script:eidmAuth.expirationTime -and
        $script:eidmAuth.expirationTime -gt (Get-Date)

    if ($stillValid)
    {
        $script:token = $script:eidmAuth.access_token
        return $true
    }

    $hadToken = $null -ne $script:eidmAuth
    try
    {
        $script:eidmAuth = Get-AuthorizationHeader
    }
    catch
    {
        $script:eidmAuth = $null
        return $false
    }

    if ($null -eq $script:eidmAuth -or [string]::IsNullOrEmpty($script:eidmAuth.access_token))
    {
        return $false
    }

    $script:token = $script:eidmAuth.access_token
    if ($hadToken)
    {
        Write-Log -Message "Obnoveni tokenu EIDM, platnost $($script:eidmAuth.expires_in) s" -source "EIDM" -logFile $logFile
    }
    return $true
}

function addAttribute($IDMObject, $attributeName)
{
    if ($IDMObject.PSObject.Properties.Name -notcontains $attributeName)
    {
        $IDMObject | Add-Member NoteProperty $attributeName $null
    }
}

function ExportToPortal($json, $objectType)
{
    if (-not (Update-EidmToken))
    {
        return @{ isError = $true; status = $null; objects = $null; exception = "Chyba prihlaseni k EIDM" }
    }

    try
    {
        $request = [System.Net.WebRequest]::Create("$EIDMURL/api/mimsync/synchronize/$objectType")
        $request.Method = "POST"
        $request.ContentType = "application/json"
        $request.Headers["Authorization"] = "Bearer $script:token"

        $requestStream = New-Object System.IO.StreamWriter($request.GetRequestStream(), (New-Object System.Text.UTF8Encoding $false))
        $requestStream.Write($json)
        $requestStream.Flush()
        $requestStream.Close()

        $response = [System.Net.HttpWebResponse]$request.GetResponse()
        $reader = [System.IO.StreamReader]$response.GetResponseStream()
        $responseJson = $reader.ReadToEnd()
        $reader.Close()

        return @{ isError = $false; status = $response.StatusCode; objects = $null; responseJson = $responseJson }
    }
    catch [System.Net.WebException]
    {
        $errResponse = $null
        if ($null -ne $_.Exception.Response)
        {
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $errResponse = $reader.ReadToEnd()
            $reader.Close()
        }
        return @{ isError = $true; status = $errResponse; objects = $null; exception = $_.Exception.Message }
    }
    catch
    {
        return @{ isError = $true; status = $null; objects = $null; exception = $_.Exception.Message }
    }
}

function GetAllObjectsFromEIDM($url, $paging = $true)
{
    $entities = @()
    $page = 1
    $isError = $false
    $status = "OK"
    $ex = $null
    $itemsPerPage = 50

    try
    {
        do
        {
            if (-not (Update-EidmToken))
            {
                $isError = $true
                $ex = "Chyba prihlaseni k EIDM"
                $status = $ex
                break
            }

            $request = [System.Net.WebRequest]::Create($url)
            $request.Method = "GET"
            $request.ContentType = "application/json"
            $request.Headers["Authorization"] = "Bearer $script:token"
            $request.Headers.Add("Pagination", "{ 'ItemsPerPage': $itemsPerPage,'CurrentPage': $page }")

            $response = [System.Net.HttpWebResponse]$request.GetResponse()
            $reader = [System.IO.StreamReader]$response.GetResponseStream()
            $responseJson = $reader.ReadToEnd()
            $reader.Close()

            $jsonObjects = $responseJson | ConvertFrom-Json
            if ($null -eq $jsonObjects.Objects)
            {
                break
            }

            $pageItems = @($jsonObjects.Objects)
            if ($pageItems.Count -eq 0)
            {
                break
            }

            $entities += $pageItems
            $page++

            if ($pageItems.Count -gt 1000 -or $paging -eq $false)
            {
                break
            }
        } while ($true)
    }
    catch [System.Net.WebException]
    {
        $respBody = $null
        if ($null -ne $_.Exception.Response)
        {
            $respStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($respStream)
            $rawBody = $reader.ReadToEnd()
            $reader.Close()
            try { $respBody = $rawBody | ConvertFrom-Json } catch { $respBody = $rawBody }
        }
        $ex = "$($respBody.message)"
        if ([string]::IsNullOrEmpty($ex)) { $ex = $_.Exception.Message }
        $isError = $true
        $status = $ex
        Write-Log -Message "Chyba GetAllObjectsFromEIDM" -Level "Error" -source "EIDM" -logFile $logFile -ex $ex -detail $url
    }
    catch
    {
        $ex = $_.Exception.Message
        $isError = $true
        $status = "Chyba: $ex"
        Write-Log -Message "Chyba GetAllObjectsFromEIDM" -Level "Error" -source "EIDM" -logFile $logFile -ex $ex -detail $url
    }

    return @{ isError = $isError; status = $status; objects = $entities; exception = $ex }
}
