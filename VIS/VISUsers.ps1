$user = 'idm'
$pass = 'pass'


$uri = 'http://crow.test.local:5003/api/v1/Custom/Idm/Users'
$secpasswd = ConvertTo-SecureString $pass -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential($user, $secpasswd)

$result = Invoke-RestMethod $uri -Credential $credential -Method get 

# Source - https://stackoverflow.com/a
# Posted by Karol Berezicki
# Retrieved 2025-12-05, License - CC BY-SA 4.0

$uri = 'http://crow.asol.local:5003/api/v1/Custom/Idm/Users'
$users = iwr -Method Get -Uri $uri -Headers @{ Authorization = "Basic "+ [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes("$($user):$($pass)")) }

 