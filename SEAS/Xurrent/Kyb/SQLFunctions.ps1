[string]$dataSource = "mimsyncdb"
[string]$database = "IDMSupport"
$Error.Clear()
$connectionString = "Server=$dataSource;Database=$database;Integrated Security=True;"
$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString
$connection.Open()


function ExecuteNonQuery($query)
{
   Log -Text "$query"
  
   try
    {
        $command = $connection.CreateCommand()
        $command.CommandText = $query
        $rowsAffected = $command.ExecuteNonQuery()
        return $rowsAffected
    }
    catch
    {
        Log -Text "Chyba ExecuteReader: $($_.Exception.Message)"
        return 0
    }

}
function ExecuteReader($commandtext)
{
    try
    {
        $command = $connection.CreateCommand()
        $command.CommandText = $commandtext
        $command.CommandTimeout =0
        $result = $command.ExecuteReader()
        $table = new-object “System.Data.DataTable”
        $table.Load($result)
        Log -Text "$query"
        return $table
    }
    catch
    {
        Log -Text "Chyba ExecuteReader: $($_.Exception.Message)"
        Log -Text "$query"
        return $null
    }
}


Function CancelSERVIORequestsInSQL($id)
{
    $q = " update [MIMSupport].[dbo].[ServioRequests] set S_Status = 11, [S_DateUpdated] = GETDATE() where S_ID = $id"
    $r = ExecuteNonQuery -query $q
}

function getWaitingSERVIORequestsFromSQL()
{
    $cmd = "SELECT [S_ID]
          ,[S_DateCreated]
          ,[S_DateUpdated]
          ,[S_Status]
          ,[S_Action]
          ,[S_Info]
          ,[S_Err]
          ,[AR_DisplayName]
          ,[AR_ExternalRoleCode]
          ,[AR_ApplicationCode]
          ,[AR_AccountName]
          ,[AR_TSServio]
          ,[AR_ObjectID]
          ,[R_Type]
          ,[R_ValidFrom]
          ,[R_ValidTo]
          ,[R_Reason]
          ,[R_Requestor]
          ,[U_AccountName]
          ,[U_DisplayName]
          ,[U_ObjectID]
          ,[U_EmployeeID]
      ,[R_RequestorID]
      FROM [MIMSupport].[dbo].[v_ServioRequests] where S_Status = 1 or S_Status = 2 order by s_ID desc"

    return ExecuteReader -commandtext $cmd
    
}


function CancelNonUniqieSERVIORequestsFromSQL()
{
    $cmd = "select distinct [AR_ExternalRoleCode], [U_AccountName], COUNT(*) as pocet FROM [MIMSupport].[dbo].[v_ServioRequests] 
    where S_Status = 1 or S_Status = 2 group by  [AR_ExternalRoleCode], [U_AccountName] order by pocet desc"
   $nonUniqueRequests = ExecuteReader -commandtext $cmd

   foreach($r in $nonUniqueRequests)
   {
    if($r.pocet -gt 1)
    {
        $cmd = "update [MIMSupport].[dbo].[v_ServioRequests] set S_Status = 13 where  [AR_ExternalRoleCode] = '$($r.AR_ExternalRoleCode)' and [U_AccountName] = '$($r.U_AccountName)' and (S_Status = 1 or S_Status = 2) and S_ID not in 
	(select top 1 S_ID FROM  
		(select * FROM  [MIMSupport].[dbo].[v_ServioRequests] where  [AR_ExternalRoleCode] = '$($r.AR_ExternalRoleCode)' and [U_AccountName] = '$($r.U_AccountName)' and (S_Status = 1 or S_Status = 2) ) a
		order by S_ID desc)"
        $ret = ExecuteNonQuery -query $cmd
    }
   }
}

function getTicketsToCheck()
{
    $cmd =      "SELECT *
      FROM [MIMSupport].[dbo].[v_SERVIO_FIMService] where (seasSERVIOStatus = 3 or seasSERVIOStatus = 4 or seasSERVIOStatus = 5) and ticketNr is NOT NULL"


     return  ExecuteReader -commandtext $cmd
}

function getWaitingSERVIORequestsFromMIM()
{
    $cmd =      "SELECT [ObjectKey]
          ,[ObjectID]
          ,[seasSERVIOStatus]
          ,[seasreqStatus]
          ,[DisplayName]
          ,[seasreqReason]
          ,[MemberKey]
          ,[roleKey]
          ,[RoleDisplayName]
          ,[RoleAccountName]
          ,[UserDisplayName]
          ,[UserAccountName]
          ,[LastError]
          ,[ValidFrom]
          ,[ValidTo]
          ,[isValid]
          ,[RoleObjectID]
          ,[UserObjectID]
          ,[externalRoleCode]
          ,[ticketNr]
          ,[requestorDisplayName]
          ,[requestorAccountName]
      FROM [MIMSupport].[dbo].[v_SERVIO_FIMService] where (seasSERVIOStatus = 1 or seasSERVIOStatus = 2 or seasSERVIOStatus = 8)"


     return  ExecuteReader -commandtext $cmd
    
}


function getAllWaitingRequests()
{
    $requests = @()


    $SERVIORequestsFromMIM = getWaitingSERVIORequestsFromMIM
    #$SERVIORequestsFromSQL = getWaitingSERVIORequestsFromSQL

    foreach($request in $SERVIORequestsFromMIM)
    {
        $PSObject = New-Object PSObject
        $PSObject | Add-Member NoteProperty "S_ID" $null
        $PSObject | Add-Member NoteProperty "ObjectID" $request.ObjectID
        $PSObject | Add-Member NoteProperty "seasSERVIOStatus" $request.seasSERVIOStatus
        $PSObject | Add-Member NoteProperty "seasreqStatus" $request.seasreqStatus
        $PSObject | Add-Member NoteProperty "DisplayName" $request.DisplayName
        $PSObject | Add-Member NoteProperty "seasreqReason" $request.seasreqReason
        $PSObject | Add-Member NoteProperty "RoleDisplayName" $request.RoleDisplayName
        $PSObject | Add-Member NoteProperty "RoleAccountName" $request.RoleAccountName
        
        if([string]::IsNullOrEmpty($request.UserAccountName) -or [string]::IsNullOrEmpty($request.UserDisplayName))
        {
            $UserDisplayName = $request.UserDisplayName
            $userAccountName = $request.UserAccountName
            
            $DisplayName = $request.DisplayName
            $rUser = $DisplayName.Split("->")[0].Trim()
            $items = $rUser.Split("(")
            $items = $items[0..($items.Length - 2)]
            $UserDisplayName = $items -join "("

            if($rUser.Contains("("))
            {
                $userAccountName = $rUser.Split("(")[-1].Trim()
                $userAccountName = $userAccountName.Replace(")","").Trim()
            }
            $PSObject | Add-Member NoteProperty "UserDisplayName" $UserDisplayName
            $PSObject | Add-Member NoteProperty "UserAccountName" $UserAccountName
        }
        else
        {
            $PSObject | Add-Member NoteProperty "UserDisplayName" $request.UserDisplayName
            $PSObject | Add-Member NoteProperty "UserAccountName" $request.UserAccountName
        
        }

        $PSObject | Add-Member NoteProperty "ticketNr" $request.ticketNr
        $PSObject | Add-Member NoteProperty "LastError" $request.LastError
        $PSObject | Add-Member NoteProperty "ValidFrom" $request.ValidFrom
        $PSObject | Add-Member NoteProperty "ValidTo" $request.ValidTo.AddHours(2)
        $PSObject | Add-Member NoteProperty "isValid" $request.isValid
        $PSObject | Add-Member NoteProperty "RoleObjectID" $request.RoleObjectID
        $PSObject | Add-Member NoteProperty "UserObjectID" $request.UserObjectID
        $PSObject | Add-Member NoteProperty "RoleExternalRoleCode" $request.externalRoleCode
        $PSObject | Add-Member NoteProperty "ApplicationCode" $null
        $PSObject | Add-Member NoteProperty "Source" "MIM"
        $PSObject | Add-Member NoteProperty "Requestor" $null
        $PSObject | Add-Member NoteProperty "U_EmployeeID" $null
        $PSObject | Add-Member NoteProperty "R_RequestorID" $null
        $PSObject | Add-Member NoteProperty "R_Type" "AR"

        if([string]::IsNullOrEmpty($request.externalRoleCode))
        {
            $PSObject.R_Type = "Computer"
        }

        if(![string]::IsNullOrEmpty($request.requestorDisplayName))
        {
            $PSObject.Requestor = "$($request.requestorDisplayName) ($($request.requestorAccountName))"
        }
        $action = $null
        if($request.seasSERVIOStatus -eq 1)
        {
            if($request.isValid)
            {
                $action = "ADD"
            }
            else
            {
                $action = "REMOVE"
            }
        }
        elseif($request.seasSERVIOStatus -eq 8)
        {
            $action = "ADD"
        }
        elseif($request.seasSERVIOStatus -eq 2)
        {
            $action = "PROLONG"
        }
        elseif($request.seasSERVIOStatus -eq 3)
        {
            $action = "CHECKADD"
        }
        elseif($request.seasSERVIOStatus -eq 4)
        {
            $action = "CHECKPROLONG"
        }
        elseif($request.seasSERVIOStatus -eq 5)
        {
            $action = "CHECKREMOVE"
        }
        $PSObject | Add-Member NoteProperty "Action" $action

        $requests+= $PSObject
    }

    foreach($request in $SERVIORequestsFromSQL)
    {
        $existing = $null
        $existing = $requests | where {$_.RoleObjectID -eq $request.AR_ObjectID  -and $_.UserObjectID -eq $request.U_ObjectID}

        if($existing -eq $null)
        {
            $PSObject = New-Object PSObject
            $PSObject | Add-Member NoteProperty "S_ID" $request.S_ID
            $PSObject | Add-Member NoteProperty "ObjectID" $request.ObjectID
            $PSObject | Add-Member NoteProperty "seasSERVIOStatus" $request.S_Status
            $PSObject | Add-Member NoteProperty "seasreqStatus" $null
            $PSObject | Add-Member NoteProperty "DisplayName" "$($request.U_DisplayName) ($($request.U_AccountName)) -> $($request.AR_DisplayName)"
            $PSObject | Add-Member NoteProperty "seasreqReason" $null
            $PSObject | Add-Member NoteProperty "RoleDisplayName" $request.AR_DisplayName 
            $PSObject | Add-Member NoteProperty "RoleAccountName" $request.AR_AccountName
            $PSObject | Add-Member NoteProperty "RoleExternalRoleCode" $request.AR_ExternalRoleCode
            $PSObject | Add-Member NoteProperty "UserDisplayName" $request.U_DisplayName
            $PSObject | Add-Member NoteProperty "UserAccountName" $request.U_AccountName
            $PSObject | Add-Member NoteProperty "ticketNr" $request.S_ticketNr
            $PSObject | Add-Member NoteProperty "LastError" $request.S_Err
            $PSObject | Add-Member NoteProperty "ValidFrom" $null
            $PSObject | Add-Member NoteProperty "ValidTo" $null
            $PSObject | Add-Member NoteProperty "isValid" $null
            $PSObject | Add-Member NoteProperty "RoleObjectID" $request.AR_ObjectID
            $PSObject | Add-Member NoteProperty "UserObjectID" $request.U_ObjectID
            $PSObject | Add-Member NoteProperty "ApplicationCode" $request.AR_ApplicationCode
            $PSObject | Add-Member NoteProperty "Action" $request.S_Action
            $PSObject | Add-Member NoteProperty "Source" "SQL"
            $PSObject | Add-Member NoteProperty "Requestor" $null
            $PSObject | Add-Member NoteProperty "U_EmployeeID" $request.U_EmployeeID
            $PSObject | Add-Member NoteProperty "R_RequestorID" $request.R_RequestorID


            $requests+= $PSObject
        }
        else
        {
            CancelSERVIORequestsInSQL -id $request.S_ID
            #zrušení duplicitního záznamu
        }
    }
    return $requests
}


function SetSQLRequestStatus($SQLRequest, $errDescription, $status, $description, $ticketNr, $isError, $reason)
{
    $replacestring = "xxxxx"
    $cmd = "update [MIMSupport].[dbo].[ServioRequests] set S_DateUPdated = GetDate() $replacestring where S_ID = $($SQLRequest.S_ID)"


    if(![string]::IsNullOrEmpty($status))
    {
        $cmd = $cmd -replace "$replacestring", ",S_Status = $status $replacestring"
    }

    if(![string]::IsNullOrEmpty($description))
    {
        $cmd = $cmd -replace "$replacestring", ",[S_Info] = CONCAT([S_Info],CHAR(13),CHAR(10) ,'$description') $replacestring"
    }

    if(![string]::IsNullOrEmpty($errDescription))
    {
        $cmd = $cmd -replace "$replacestring", ",S_Err = $errDescription $replacestring"
    }
    else
    {
        $cmd = $cmd -replace "$replacestring", ",S_Err = NULL $replacestring"
    }

    if(![string]::IsNullOrEmpty($ticketNr))
    {
        $cmd = $cmd -replace "$replacestring", ",S_ticketNr = '$ticketNr' $replacestring"
    }

    if(![string]::IsNullOrEmpty($reason))
    {
        $cmd = $cmd -replace "$replacestring", ",S_ApprovalInfo = $reason $replacestring"
    }


        $cmd = $cmd -replace "$replacestring", ""
    
   

    $error.Clear()
    try
    {
        $ret =  ExecuteNonQuery -query $cmd
        if($error.Count -gt 0)
        {
            Log -text "Chyba pri aktualizaci zadosti $($SetSQLRequestStatus.DisplayName)"
            Log -text "$($error[0].exception.message)"
        }
        else
        {
            Log -text "Uspesne nastaveni zadosti $($SetSQLRequestStatus.DisplayName) na stav: $status"
        }
    }
    catch
    {
        Log -text "Chyba pri aktualizaci zadosti $($SetSQLRequestStatus.DisplayName)"
        Log -text "$($error[0].exception.message)"
    } 
}

