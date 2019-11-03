#!/usr/bin/env pwsh

#######################################################################
# Constants, used by all functions.
$logTimeFormat="yyyy-MM-dd hh:mm:ss"

#######################################################################
function Write-Log
{
    Param
    (
        # Information type of log entry
        [Parameter(Mandatory=$true, 
                   ValueFromPipelineByPropertyName=$true,
                   Position=0,
                   ParameterSetName = 'Info')]
        [ValidateNotNull()]
        [ValidateNotNullOrEmpty()]
        [Alias("information")]
        [System.String]$Info,
 
        # Debug type of log entry
        [Parameter(Mandatory=$true, 
                   ValueFromPipelineByPropertyName=$true, 
                   Position=0,
                   ParameterSetName = 'Debug')]
        [ValidateNotNull()]
        [ValidateNotNullOrEmpty()]
        [System.String]$Debugging,
 
        # Error type of log entry
        [Parameter(Mandatory=$true, 
                   ValueFromPipeline=$true,
                   Position=0,
                   ParameterSetName = 'Error')]
        [ValidateNotNull()]
        [ValidateNotNullOrEmpty()]
        [System.String]$Error,
 
 
        # The error record containing an exception to log
        [Parameter(Mandatory=$false, 
                   ValueFromPipeline=$true,
                   ValueFromPipelineByPropertyName=$true, 
                   ValueFromRemainingArguments=$false, 
                   Position=1,
                   ParameterSetName = 'Error')]
        [ValidateNotNull()]
        [ValidateNotNullOrEmpty()]
        [Alias("record")]
        [System.Management.Automation.ErrorRecord]$ErrorRecord
    )
 
    $logPrefix = $(Get-Date).ToString($logTimeFormat)
    switch ($PSBoundParameters.Keys)
    {
         'Error' 
         {
            Write-Host "$($logPrefix) [ERROR] $Error"
 
            if ($PSBoundParameters.ContainsKey('ErrorRecord'))
            {
                $Message = '{0} ({1}: {2}:{3} char:{4})' -f $ErrorRecord.Exception.Message,
                                                            $ErrorRecord.FullyQualifiedErrorId,
                                                            $ErrorRecord.InvocationInfo.ScriptName,
                                                            $ErrorRecord.InvocationInfo.ScriptLineNumber,
                                                            $ErrorRecord.InvocationInfo.OffsetInLine
 
                Write-Host "$($logPrefix) [ERROR] $Message"
            }
         }
         'Info' 
         {
            Write-Host  "$($logPrefix) [INFO] $Info"
         }
         'Debugging' 
         {
            Write-Host "$($logPrefix) [DEBUG] $Debugging"
         }
    }
}