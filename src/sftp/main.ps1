#!/usr/bin/env pwsh
param(
    [string]$EntryAction
)

#######################################################################
# Import modules

Import-module .\scripts\logging.ps1 -Force
Import-module .\scripts\security.ps1 -Force


#######################################################################
# Constants, used by all functions.

$ConfigFilePath = "./config/sftp.json"
$SftpGroup = "sftp"
$HomeBasePath = "/home"


#######################################################################
# Main entry point

Write-Host "Entry Action: $EntryAction"
switch ($EntryAction) {
    "Start" {
        Write-Log -Info 'Starting'

        # ---- Check host keys
        Write-Log -Info 'Verifying host keys'
        If (-Not $(Test-Path "/etc/ssh/keys/ssh_host_ed25519_key")) {
            Write-Log -Info "Creating '/etc/ssh/keys/ssh_host_ed25519_key'"
            [void](ssh-keygen -t ed25519 -f /etc/ssh/keys/ssh_host_ed25519_key -N '""')
        }
        If (-Not $(Test-Path "/etc/ssh/keys/ssh_host_rsa_key")) {
            Write-Log -Info "Creating '/etc/ssh/keys/ssh_host_rsa_key'"
            [void](ssh-keygen -t rsa -b 4096 -f /etc/ssh/keys/ssh_host_rsa_key -N '""')
        }

        # ---- Create home base path if it doesn't exist
        If (!(Test-Path -Path $HomeBasePath)) {
            Write-Log -Info "Creating home base '$HomeBasePath'"
            [void](New-Item -Path $HomeBasePath -ItemType Directory -Force)
        }

        # ---- Set root to own home base path
        Write-Log -Info "Setting 'root' as owner for '$HomeBasePath'"
        chown root:root $HomeBasePath
        
        # ---- Load the JSON configuration file
        Write-Log -Info "Loading configuration."
        If (!$(Test-Path -Path $ConfigFilePath)) {
            Write-Log -Error "Could not find configuration file at '$ConfigFilePath'."
        }
        $Config = Get-Content -Raw -Path $ConfigFilePath | ConvertFrom-Json

        
        # ---- Basic configuration validation
        IF (!$Config.global) {
            Write-Log -Error "'global' section missing from configuration."
            Exit 1
        }
        IF (!$Config.users) {
            Write-Log -Error "'users' section missing from configuration."
            Exit 1
        }
        else {
            If ($Config.users.Length -eq 0) {
                Write-Log -Error "'users' section does not contain any user definitions."
                Exit 1
            }
            else {
                $configUsers = @()
                For ($i = 0; $i -lt $Config.users.Length; $i++) {
                    $configUser = $Config.users[$i]
                    If (@([string]::IsNullOrWhiteSpace($configUser.username))) {
                        Write-Log -Error "'users[$i].username' property is missing, null or empty."
                        Exit 1
                    }
                    $configUsers += , $($configUser.username.Trim())
                }
                $distinctUsers = $configUsers | Select-Object -Unique
                If (Compare-Object -ReferenceObject $distinctUsers -DifferenceObject $configUsers) {
                    Write-Log -Error "'users' section contains multiple users with the same username. Usernames need to be unique."
                    Exit 1
                }
                Write-Log -Info "Configuration contains '$($Config.users.Length)' user(s)."
            }
        }

        # ---- Build sshd configuration
        $sshdConfigBuilder = [System.Text.StringBuilder]::new()
        # SSH Protocol
        [void]$sshdConfigBuilder.AppendLine("");
        [void]$sshdConfigBuilder.AppendLine("# SSH Protocol");
        [void]$sshdConfigBuilder.AppendLine("Protocol 2");
        [void]$sshdConfigBuilder.AppendLine("");
        # Host Keys
        [void]$sshdConfigBuilder.AppendLine("# Host Keys");
        [void]$sshdConfigBuilder.AppendLine("HostKey /etc/ssh/keys/ssh_host_ed25519_key");
        [void]$sshdConfigBuilder.AppendLine("HostKey /etc/ssh/keys/ssh_host_rsa_key");
        [void]$sshdConfigBuilder.AppendLine("");
        # Use DNS
        [void]$sshdConfigBuilder.AppendLine("UseDNS $($(If($Config.global.useDNS){ 'yes' } else { 'no' }))");
        [void]$sshdConfigBuilder.AppendLine("");
        # Logging
        If ($Config.sshd.logging.enabled) {
            [void]$sshdConfigBuilder.AppendLine("# Logging");
            [void]$sshdConfigBuilder.AppendLine(
                "LogLevel $($(If($Config.global.logging.level){ $Config.global.logging.level } else { 'INFO' }))");
            [void]$sshdConfigBuilder.AppendLine("");
        }
        # Configure file transfer daemon
        [void]$sshdConfigBuilder.AppendLine("# Subsystem");
        [void]$sshdConfigBuilder.AppendLine("Subsystem sftp internal-sftp");
        [void]$sshdConfigBuilder.AppendLine("");
        # Chroot configuration
        $Chroot = [PSCustomObject]@{
            directory = "%h"
            startPath = $null
        }
        If ($Config.global.chroot) {
            $configChroot = $($Config.global.chroot) 
            If (-Not @([string]::IsNullOrWhiteSpace($configChroot.directory))) {
                $Chroot.directory = $configChroot.directory.Trim()
            }
            If (-Not @([string]::IsNullOrWhiteSpace($configChroot.startPath))) {
                $Chroot.startPath = $configChroot.startPath.Trim()
            }
        }
        # ---- Load default directories
        $DefaultDirectories = @()
        If ($Config.global.directories -and ($Config.global.directories.Length -gt 0)) {
            $DefaultDirectories = $Config.global.directories | ForEach-Object -Process { $_.Trim() }
        }
        # ---- Load user configuration
        $Users = @()
        $usersWithCustomDefaultDir = @()
        ForEach ($configUser in $Config.users) {
            $user = @{
                username    = $configUser.username.Trim()
                password    = $(If ($configUser.password) { $configUser.password | ConvertTo-SecureString -AsPlainText -Force } else { $null })
                chroot      = @{
                    override  = $False
                    directory = $null
                    startPath = $null
                }
                directories = $(If ($configUser.directories) { $($configUser.directories | Select-Object -Unique) } else { @() })
                uid         = "$($confiUser.uid)".ToString().Trim()
                gid         = "$($confiUser.gid)".ToString().Trim()
            }
            If ($configUser.chroot) {
                $user.chroot.override = $True
                If (! @([string]::IsNullOrWhiteSpace($configUser.chroot.directory))) { 
                    $user.chroot.directory = $configUser.chroot.directory.Trim()
                }
                If (! @([string]::IsNullOrWhiteSpace($configUser.chroot.startPath))) {
                    $user.chroot.startPath = $configUser.chroot.startPath.Trim()
                }
                $usersWithCustomDefaultDir += [PSCustomObject] $user
            }
            else {
                $user.chroot.directory = $Chroot.directory
                $user.chroot.startPath = $Chroot.startPath
            }
            
            $user.directories = $user.directories + $DefaultDirectories | Select-Object -Unique
            $Users += [PSCustomObject] $user
        }
        # ---- Build Match block for SFTP group
        [void]$sshdConfigBuilder.AppendLine("# Match SFTP group");
        [void]$sshdConfigBuilder.Append("Match Group $SftpGroup");
        If ($usersWithCustomDefaultDir.Length -gt 0) {
            $userMatchExclude = $usersWithCustomDefaultDir | Select-Object -ExpandProperty username | ForEach-Object -Process { "!$_" } | Join-String -Separator ","
            [void]$sshdConfigBuilder.Append(' User "*,');
            [void]$sshdConfigBuilder.Append($userMatchExclude);
            [void]$sshdConfigBuilder.Append('"');
        }
        [void]$sshdConfigBuilder.AppendLine();
        [void]$sshdConfigBuilder.AppendLine("    ChrootDirectory $($Chroot.directory)");
        [void]$sshdConfigBuilder.AppendLine("    X11Forwarding no");
        [void]$sshdConfigBuilder.AppendLine("    AllowTcpForwarding no");
        If (!([string]::IsNullOrWhiteSpace($Chroot.startPath))) {
            [void]$sshdConfigBuilder.AppendLine("    ForceCommand internal-sftp -d $($Chroot.startPath)");
        }
        else {
            [void]$sshdConfigBuilder.AppendLine("    ForceCommand internal-sftp");
        }
        [void]$sshdConfigBuilder.AppendLine();
        # ---- Build Match block for users with default directory override
        ForEach ($user in $usersWithCustomDefaultDir) {
            [void]$sshdConfigBuilder.AppendLine("# Match User $($user.username)");
            [void]$sshdConfigBuilder.AppendLine("Match User $($user.username)");
            [void]$sshdConfigBuilder.AppendLine("    ChrootDirectory $($user.chroot.directory)");
            [void]$sshdConfigBuilder.AppendLine("    X11Forwarding no");
            [void]$sshdConfigBuilder.AppendLine("    AllowTcpForwarding no");
            If (!([string]::IsNullOrWhiteSpace($user.chroot.startPath))) {
                [void]$sshdConfigBuilder.AppendLine("    ForceCommand internal-sftp -d $($user.chroot.startPath)");
            }
            else {
                [void]$sshdConfigBuilder.AppendLine("    ForceCommand internal-sftp");
            }
            [void]$sshdConfigBuilder.AppendLine();
        }
        # --- Set configuration
        $sshConfigContent = $sshdConfigBuilder.ToString();
        Write-Host "-------------------------"
        Write-Host "sshd configuration:"
        Write-Host "-------------------------"
        Write-Host $sshConfigContent
        Write-Host "-------------------------"
        Write-Log -Info "Saving sshd configuration"
        Set-Content -Path "/etc/ssh/sshd_config" $sshConfigContent
        # ---- Ensure the sftp group
        If (!$(Get-LocalGroupExists -Name $SftpGroup)) {
            Write-Log -Info "Creating '$SftpGroup' group."
            New-LocalGroup -Name $SftpGroup -Force $True
        }

        $existingSftpUsers = Get-LocalGroupUsers -Group $SftpGroup | Select-Object -Unique
        $Usernames = ($Users | Select-Object -ExpandProperty username)
        If($existingSftpUsers.Length -gt 0){
            ForEach($user in $existingSftpUsers){
                If(-Not ($Usernames -contains $user)){
                    Write-Host "Removing $user"
                    Remove-LocalUser -Name $user
                }
            }
        }
       

        ForEach ($user in $Users) {
            # Create the user if it does not exist
            If (-Not $(Get-LocalUserExists -Name $user.username)) {
                Write-Log -Info "Creating user '$($user.username)'"
                New-LocalUser -Name $user.username -NoLoginShell $True
            }
            # Add the user to the sftp group
            Write-Log -Info "Adding user '$($user.username)' to the '$SftpGroup' group"
            Add-LocalGroupUser -Group $SftpGroup -Name $user.username
            # Update the password
            Write-Log -Info "Updating password for user '$($user.username)'"
            Set-LocalUserPassword -Name $user.username -Password $user.password
            # Update the user UID (if set)
            If (-Not @([string]::IsNullOrWhiteSpace($user.uid))) {
                If ($user.uid -ne $(Get-LocalUserId -Name $user.username)) {
                    Write-Log -Info "Setting UID for user '$($user.username)' to '$($user.uid)'"
                    Set-LocalUserId -Name $user.username -UserId $user.uid -NonUnique $True
                }
            }
            # Update the user GID (if set)
            If (-Not @([string]::IsNullOrWhiteSpace($user.gid))) {
                $newGroup = "sftp_virtual_gid_$($user.gid)"
                Write-Log -Info "Creating group '$newGroup' with gid '$($user.gid)'"
                New-LocalGroup -Name $newGroup -GroupId $user.gid -Force $True
                Add-LocalGroupUser -Group $newGroup -Name $user.username
            }

            #Create user home if it doesn't exist
            $homeDirPath = Join-Path -Path $HomeBasePath -ChildPath $user.username
            If (!(Test-Path -Path $homeDirPath)) {
                Write-Log -Info "Creating home directory for user '$($user.username)' at '$homeDirPath'"
                [void](New-Item -Path $homeDirPath -ItemType Directory -Force)
            }
            chown "$($user.username):$($SftpGroup)" "$homeDirPath"
            chmod 700 "$homeDirPath"

            # -- Compute the chroot directory (escape %%h and %%u) and set permissions
            $pathSplit = $user.chroot.directory.Split("%%h", [System.StringSplitOptions]::None)
            $pathParts = @()
            ForEach ($part in $pathSplit) {
                $pathParts += , $part.replace("%h", $homeDirPath)
            }
            $chrootPath = $pathParts | Join-String -Separator "%%h"
            $pathSplit = $chrootPath.Split("%%u", [System.StringSplitOptions]::None)
            $pathParts = @()
            ForEach ($part in $pathSplit) {
                $pathParts += , $part.replace("%u", $user.username)
            }
            $chrootPath = $pathParts | Join-String -Separator "%%u"
            Write-Log -Info "Setting chroot permissions on '$($chrootPath)'"
            chown root:root "$($chrootPath)"
            chmod 755 "$($chrootPath)"


            #Create directories
            foreach ($directory in $user.directories) {
                $userId = (Get-LocalUserId -Name $user.username);
                $dirPath = Join-Path -Path $homeDirPath -ChildPath $directory
                If (!(Test-Path -Path $dirPath)) {
                    Write-Log -Info "Creating directory '$dirPath' for user '$($user.username)'"
                    [void](New-Item -Path $dirPath -ItemType Directory -Force)
                }
                chown -R "$($userId):users" "$dirPath"
            }

            Write-Log -Info "Refreshing ssh keys for user '$($user.username)'"
            $sshDir = Join-Path -Path $homeDirPath -ChildPath ".ssh"
            $sshKeysDir = Join-Path -Path $sshDir -ChildPath "keys"
            $sshAuthKeys = Join-Path -Path $sshDir -ChildPath "authorized_keys"
            [void](Remove-Item -Path $sshAuthKeys -Force -ErrorAction SilentlyContinue)
            [void](New-Item -Path $sshDir -ItemType Directory -Force)
            [void](New-Item -Path $sshKeysDir -ItemType Directory -Force)
            Get-ChildItem -Path $sshKeysDir | ForEach-Object { Get-Content $_ } | Select-Object -Unique | Join-String -Separator "$($([Environment]::NewLine))" | Set-Content $sshAuthKeys -Force
            chown "$($user.username)" $sshAuthKeys
            chmod 600 $sshAuthKeys
        }


        # --- Start sshd
        Write-Log -Info "Starting sshd and streaming output"
        Start-Process -NoNewWindow sshd "-D -e" -Wait

        break;
    }
    default {
        Write-Log -Info "Invalid or missing entry action"
        break
    }
}