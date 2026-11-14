[string]$dataSource = "."
[string]$database = "MIMSupport"
$Error.Clear()

$connectionString = "Data Source=DBEIDMT.seas.sk,1436;Database=EasyIDM;User Id=sys_IDM.Appool;Password=qLOt4})Eb!063y;MultipleActiveResultSets=true;TrustServerCertificate=True;Persist Security Info=True"
$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString
$connection.Open()

function ExecuteNonQuery($query)
{
    Write-Log -Message $query -source "SQL" -logFile $logFile
    try
    {
        $command = $connection.CreateCommand()
        $command.CommandText = $query
        return $command.ExecuteNonQuery()
    }
    catch
    {
        Write-Log -Message "Chyba ExecuteNonQuery: $($_.Exception.Message)" -Level "Error" -source "SQL" -logFile $logFile -detail $query
        return 0
    }
}

function ExecuteReader($commandtext)
{
    try
    {
        $command = $connection.CreateCommand()
        $command.CommandText = $commandtext
        $command.CommandTimeout = 0
        $result = $command.ExecuteReader()
        $table = New-Object System.Data.DataTable
        $table.Load($result)
        return $table
    }
    catch
    {
        Write-Log -Message "Chyba ExecuteReader: $($_.Exception.Message)" -Level "Error" -source "SQL" -logFile $logFile -detail $commandtext
        return $null
    }
}

function getIDMUser($IdmObjectId)
{
    $ret = ExecuteReader -commandtext "SELECT [JsonData] FROM [EasyIDM].[dbo].[IDMObjectReadStore] where IdmObjectId = '$IdmObjectId'"
    if ($null -ne $ret -and $null -ne $ret.JsonData)
    {
        return $ret.JsonData | ConvertFrom-Json
    }
    return $null
}
