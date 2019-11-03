#!/usr/bin/env pwsh

function New-LocalUser {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$Name,
        [bool] $NoLoginShell = $false
    )
    $options = @()
    $options += , "--comment"
    $options += , "$Name"

    If ($NoLoginShell) {
        $options += , "-s"
        $options += , "/usr/sbin/nologin"
    }
    useradd $options $Name

    if ($LASTEXITCODE -ne 0) {
        Write-Error -Message "Error creating $Name"
        Exit
    }
}

function Remove-LocalUser {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$Name
    )
    
    userdel $Name

    if ($LASTEXITCODE -ne 0) {
        Write-Error -Message "Error removing $Name"
        Exit
    }
}

function Get-LocalUserExists {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$Name
    )
    $userEntry = $(getent passwd $Name)
    return ![string]::IsNullOrEmpty($userEntry)
}

function Get-LocalUserId {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$Name
    )
    return $(id -u "$Name")
}

function Set-LocalUserId {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$Name,
        [ValidateNotNullOrEmpty()] 
        [string]$UserId,
        [bool] $NonUnique = $false
    )

    If ($UserId -eq $(Get-LocalUserId -Name $Name)) { return; }

    $options = @()
    If ($NonUnique) { $options += , "--non-unique" }
    
    pkill -U "$(Get-LocalUserId -Name $Name)"
    usermod $options --uid $UserId $Name
    if (-Not ($?)) {
        Write-Error -Message "Error while changing user ID for $Name" 
        exit $LastExitCode
    }
}

function Set-LocalUserPassword {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [securestring]$Password,
        [bool]$PasswordEncrypted = $false
    )

    #If the password is null or emtpy set it to disabled password
    If (([string]::IsNullOrEmpty($Password))) {
        usermod -p "*" "$Name"
        return;
    }
    
    $options = @()
    If ($PasswordEncrypted) { $options += , "-e" }

    $UnsecurePassword = (New-Object PSCredential "user", $Password).GetNetworkCredential().Password
    Write-Output "$($Name):$($UnsecurePassword)" | chpasswd $options
    if (-Not ($?)) {
        Write-Error -Message "Error changing password for $Name" 
        exit $LastExitCode
    }
}



function New-LocalGroup {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$Name,
        [ValidateNotNullOrEmpty()] 
        [string] $GroupId,
        [bool]$NonUniqueId = $true,
        [bool]$Force
    )
    
    $options = @()
    If ($Force) { $options += , "-f" }
    If ($GroupId) { 
        $options += , "-g $GroupId"
        If ($NonUniqueId) { $options += , "-o" }
    }
    
    groupadd $options $Name
    if (-Not ($?)) {
        Write-Error -Message "Error creating group $Name" 
        exit $LastExitCode
    }
}

function Get-LocalGroupExists {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$NameOrId
    )
    $entry = $(getent group $NameOrId)
    return ![string]::IsNullOrEmpty($entry)
}



function Get-LocalGroupUsers {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$Group
    )
    $groupEntries = $(members $Group)
    if ($([string]::IsNullOrEmpty($groupEntries))) {
        $groupEntries = ""
    }
    return $groupEntries.split(' ')
}
function Add-LocalGroupUser {
    param
    (
        [ValidateNotNullOrEmpty()] 
        [string]$Group,
        [ValidateNotNullOrEmpty()] 
        [string]$Name
    )
    usermod -a -G $Group $Name
    if (-Not ($?)) {
        Write-Error -Message "Error while adding $Name to $Group" 
        exit $LastExitCode
    }
}