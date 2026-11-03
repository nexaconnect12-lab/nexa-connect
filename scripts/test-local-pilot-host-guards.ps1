#requires -Version 7.4
$ErrorActionPreference='Stop'
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/local-pilot-host-helpers.ps1"
$count=0
function Require([bool]$Value){if(-not $Value){throw 'Host guard failed.'};$script:count++}
function Reject([scriptblock]$Work){try{& $Work|Out-Null}catch{$script:count++;return};throw 'Unsafe host input accepted.'}
Require ((Get-LocalPilotHostDecision @(@{status='running'}) $false 0) -ceq 'running')
Require ((Get-LocalPilotHostDecision @(@{status='stopped'}) $false 0) -ceq 'resume')
Require ((Get-LocalPilotHostDecision @(@{status='stopped'}) $false 3) -ceq 'blocked-budget')
Require ((Get-LocalPilotHostDecision @(@{status='changed-owner'}) $false 0) -ceq 'blocked-owner')
Require ((Get-LocalPilotHostDecision @(@{status='unresponsive'}) $false 0) -ceq 'degraded-live-process')
Require ((Get-LocalPilotHostDecision @(@{status='stopped'}) $true 3) -ceq 'stop')
Reject {Get-LocalPilotHostDecision @() $false -1}
Reject {Get-LocalPilotHostDecision @() $false 4}
Require (-not (Test-LocalPilotHostReady @{phase='running';qr='recovering'}))
Require (Test-LocalPilotHostReady @{phase='running';qr='not-configured'})
Require (Test-LocalPilotHostReady @{phase='running';qr='running'})
Require (-not (Test-LocalPilotHostReady @{phase='recovering';qr='running'}))
$id='a'*32;$root='D:\fixture';$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$config=@{id=$id;taskName="NexaConnect-LocalPilot-$id";userSid=$sid;pwsh='C:\fixture\pwsh.exe';enableLogonStart=$false}
$task=@{TaskName=$config.taskName;TaskPath='\';Principal=@{UserId=$sid;RunLevel='Limited';LogonType='Interactive'};Actions=@(@{Execute=$config.pwsh;Arguments=(Get-LocalPilotHostArguments $root $id);WorkingDirectory=$root});Settings=@{ExecutionTimeLimit='PT0S';MultipleInstances='IgnoreNew';RestartCount=3;RestartInterval='PT1M';DisallowStartIfOnBatteries=$false;StopIfGoingOnBatteries=$false};Triggers=@()}
Assert-LocalPilotTask $task $config $root;$count++
$task.Settings.ExecutionTimeLimit='PT1M';Reject {Assert-LocalPilotTask $task $config $root};$task.Settings.ExecutionTimeLimit='PT0S'
$task.Actions[0].Arguments+=' -Command unapproved';Reject {Assert-LocalPilotTask $task $config $root};$task.Actions[0].Arguments=Get-LocalPilotHostArguments $root $id
$task.Principal.RunLevel='Highest';Reject {Assert-LocalPilotTask $task $config $root};$task.Principal.RunLevel='Limited'
$task.Principal.LogonType='Password';Reject {Assert-LocalPilotTask $task $config $root};$task.Principal.LogonType='Interactive'
$task.TaskPath='\foreign\';Reject {Assert-LocalPilotTask $task $config $root};$task.TaskPath='\'
$task.Actions+=@(@{Execute=$config.pwsh;Arguments='extra'});Reject {Assert-LocalPilotTask $task $config $root}
Reject {Get-LocalPilotHostArguments $root '../outside'}
$status=[pscustomobject]@{version=1;hostId=$id;phase='running';qr='not-configured';attempts=0;qrAttempts=0;processId=123;startTicks=123;observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');secret='must-not-export'}
Require (-not ((Get-LocalPilotHostPublicStatus $status $id|ConvertTo-Json).Contains('must-not-export')))
$status.phase='private-secret';Reject {Get-LocalPilotHostPublicStatus $status $id}
Write-Output "PASS: $count host budget/task/principal/maintenance guards. No tasks or runtime state changed."
