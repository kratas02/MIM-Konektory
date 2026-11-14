. C:\Batch\FIMAutomation\FIMPowerShell.ps1

$EIDMURL = "http://vmsewmimt01v"


function Remove-StringDiacritic
{
<#
    .SYNOPSIS
        This function will remove the diacritics (accents) characters from a string.
        
    .DESCRIPTION
        This function will remove the diacritics (accents) characters from a string.
    
    .PARAMETER String
        Specifies the String on which the diacritics need to be removed
    
    .PARAMETER NormalizationForm
        Specifies the normalization form to use
        https://msdn.microsoft.com/en-us/library/system.text.normalizationform(v=vs.110).aspx
    
    .EXAMPLE
        PS C:\> Remove-StringDiacritic "L'été de Raphaël"
        
        L'ete de Raphael
    
    .NOTES
        Francois-Xavier Cat
        @lazywinadmin
        www.lazywinadmin.com
#>
    
    param
    (
        [ValidateNotNullOrEmpty()]
        [Alias('Text')]
        [System.String]$String,
        [System.Text.NormalizationForm]$NormalizationForm = "FormD"
    )
    
    BEGIN
    {
        $Normalized = $String.Normalize($NormalizationForm)
        $NewString = New-Object -TypeName System.Text.StringBuilder
        
    }
    PROCESS
    {
        $normalized.ToCharArray() | ForEach-Object -Process {
            if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($psitem) -ne [Globalization.UnicodeCategory]::NonSpacingMark)
            {
                [void]$NewString.Append($psitem)
            }
        }
    }
    END
    {
        Write-Output $($NewString -as [string])
    }
}

function ConvertToDateTime($dt)
{
    try{
        return [datetime]$dt
    }

    catch
    {
        return $null
    }

}

function GetOwnerMail($owners)
{
    $ret = $null
    foreach($ownerID in $owners)
    {
        $MIMuser = GetMIMObject -filter "/Person[ObjectID='$($ownerID.Substring(9))']"
        if($MIMuser -ne $null -and $MIMuser.Email -ne $null)
        {
            return $MIMuser.Email
        }
        elseif($MIMuser -ne $null)
        {
            $mail = "$($MIMuser.FirstName).$($MIMuser.LastName)"
            $mail = Remove-StringDiacritic -String $mail
            return "$($mail)@seas.sk"
        }
    }


}


function UpdateAPP($MIMApp, $XResponse)
{
    $obj = New-Object Microsoft.ResourceManagement.Automation.ObjectModel.ImportObject
    $obj.ObjectType = $MIMApp.MIMObject.ResourceManagementObject.ObjectType
    $obj.SourceObjectIdentifier = $MIMApp.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.TargetObjectIdentifier = $MIMApp.MIMObject.ResourceManagementObject.ObjectIdentifier
    $obj.State = 1


        SetSingleValue $obj "seasexternalID" $XResponse.id

    $error.Clear()
    try
    {
        $obj | Import-FIMConfig -Uri $DefaultUri
        if($error.Count -gt 0)
        {
            Log -text "Chyba pri aktualizaci aplikace $($MIMApp.DisplayName)"
            Log -text "$($error[0].exception.message)"
        }
        else
        {
            Log -text "Uspesna aktualizace aplikace $($MIMApp.DisplayName)"
        }
    }
    catch
    {
        Log -text "Chyba pri aktualizaci aplikace $($MIMApp.DisplayName)"
        Log -text "$($error[0].exception.message)"
    } 
}


function getApps ($appCodes)
{
    $retObjects=@()
    foreach($appCode in $appCodes)
    {
        $retObjects += GetMIMObject -filter "/RoleApplication[seasapplicationCode='$appCode']"
    }
    return $retObjects
}

function GetMIMObject($filter)
{
    $retObjects=@()
    
    $PSObject = New-Object PSObject

    $MIMObjects = $null
    $MIMObjects = Export-FIMConfig –onlyBaseResources -customconfig "$filter" -Uri $DefaultUri
    if($MIMObjects -ne $null)
    {
        foreach($MIMObj in $MIMObjects)
        {
            $PSObject = New-Object PSObject
            $PSObject | Add-Member NoteProperty "MIMObject" $MIMObj
            foreach($attr in $MIMObj.ResourceManagementObject.ResourceManagementAttributes)
            {
                if($attr.IsMultiValue)
                {
                    $PSObject | Add-Member NoteProperty $attr.AttributeName $attr.Values 
                }
                else
                {
                    $PSObject | Add-Member NoteProperty $attr.AttributeName $attr.Value
                }
            }
            $retObjects+=$PSObject
        }
    }

    return $retObjects
}

function SetMIMREquestStatus($MIMRequest, $errDescription, $status, $description, $ticketNr, $isError, $reason)
{
    $foundRequest = Export-FIMConfig –onlyBaseResources -customconfig "/RoleRequest[ObjectID='$($MIMRequest.ObjectID.Guid)']" -Uri $DefaultUri

    $obj = New-Object Microsoft.ResourceManagement.Automation.ObjectModel.ImportObject
    $obj.ObjectType = $foundRequest.ResourceManagementObject.ObjectType
    $obj.SourceObjectIdentifier = $foundRequest.ResourceManagementObject.ObjectIdentifier
    $obj.TargetObjectIdentifier = $foundRequest.ResourceManagementObject.ObjectIdentifier
    $obj.State = 1

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
            Log -text "Uspesne nastaveni zadosti $($MIMRequest.DisplayName) na stav: $status"
        }
    }
    catch
    {
        Log -text "Chyba pri aktualizaci zadosti $($MIMRequest.DisplayName)"
        Log -text "$($error[0].exception.message)"
    } 
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

function SetRequestsStatus($requests, $ticketNr, $errDescription, $reason)
{

    foreach($r in $requests)
    {
        $status, $description = getRequestStatus -errDescription $errDescription -action $r.Action -ticketNr $ticketNr
        if($r.Source -eq "MIM")
        {
            SetMIMREquestStatus -MIMRequest $r -errDescription $errDescription -status $status -description $description -ticketNr $ticketNr -reason $reason
        }
        elseif($r.Source -eq "SQL")
        {
            SetSQLREquestStatus -SQLRequest $r -errDescription $errDescription -status $status -description $description -ticketNr $ticketNr -reason $reason
        }
    
    
    } 
}