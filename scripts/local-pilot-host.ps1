#requires -Version 7.4
[CmdletBinding()]
param([ValidateSet('Install','Start','Retry','Status','Stop','Remove')][string]$Action='Status',[string]$HostId,
 [string]$InstallDirectory=(Join-Path $env:LOCALAPPDATA 'NexaConnect/DraftTerminal'),[string]$PilotId,
 [switch]$EnableLogonStart,[string]$OtlpEndpoint,[switch]$ConfirmTestResources)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Local pilot hosting requires this Windows account.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-release-helpers.ps1"
. "$PSScriptRoot/local-pilot-helpers.ps1"
. "$PSScriptRoot/local-pilot-host-helpers.ps1"
if($Action -eq 'Install'){
 if($HostId -or -not $ConfirmTestResources){throw 'Install generates a fresh host ID and requires -ConfirmTestResources.'}
 if(Get-Process NexaConnect.POS -ErrorAction SilentlyContinue){throw 'Finish work and close POS before installing supervision.'}
 $install=Assert-TerminalPath $InstallDirectory;Assert-TerminalInstallOwnership $install
 $active=Get-Content "$install/active.json" -Raw|ConvertFrom-Json -AsHashtable;Assert-DraftRunId $active.runId;Assert-DraftRunId $active.releaseId
 $run=Assert-TerminalPath (Get-DraftRunDirectory $root $active.runId)
 $pointer=Assert-TerminalPath "$root/.runstate/local-pilot/$($active.runId)/host-current.json"
 if(Test-Path $pointer){throw 'Stop and remove the existing owned host before a new installation.'}
 $package=Assert-TerminalPath "$install/releases/$($active.releaseId)";$manifest=Read-TerminalPackage $package
 if($manifest.runId -cne $active.runId){throw 'Installed run differs.'}
 $hostId=[guid]::NewGuid().ToString('N');$directory=Assert-TerminalPath "$root/.runstate/local-pilot-host/$HostId"
 New-Item -ItemType Directory $directory|Out-Null;Protect-DraftDirectory $directory
 $pwsh=if(Test-Path 'C:/Program Files/PowerShell/7/pwsh.exe'){'C:\Program Files\PowerShell\7\pwsh.exe'}else{(Get-Command pwsh).Source}
 if($OtlpEndpoint){$uri=[uri]$OtlpEndpoint;if(-not $uri.IsLoopback -or $uri.Scheme -notin @('http','https') -or $uri.UserInfo -or $uri.Query -or $uri.Fragment){throw 'OTLP must use a local HTTP(S) collector.'}}
 $config=@{version=1;kind='normal-local-pilot-host';id=$HostId;runId=$active.runId;releaseId=$active.releaseId;installDirectory=$install;userSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;taskName="NexaConnect-LocalPilot-$HostId";pwsh=$pwsh;enableLogonStart=[bool]$EnableLogonStart;otlpEndpoint=$OtlpEndpoint;qrPilotId='';qrActivationSha256='';workerSha256=(Get-FileHash "$PSScriptRoot/local-pilot-host-worker.ps1").Hash.ToLowerInvariant();packageSha256=(Get-FileHash "$package/terminal-package.json").Hash.ToLowerInvariant();referenceSha256=(Get-FileHash "$run/reference.json").Hash.ToLowerInvariant();backendArtifacts=Get-DraftQrPilotArtifactHash $run}
 Assert-LocalPilotHostBinding $root $config
 if($PilotId){
  Assert-DraftRunId $PilotId;$qr=Assert-TerminalPath "$root/.runstate/qr7/$PilotId"
  $plan=Get-Content "$qr/plan.json" -Raw|ConvertFrom-Json
  if($plan.stackRunId -cne $active.runId){throw 'QR pilot belongs to another run.'}
  $report=& node "$PSScriptRoot/release/qr7.mjs" report $qr
  if($LASTEXITCODE -ne 0 -or @((($report|ConvertFrom-Json).prerequisites)).Count){throw 'QR activation needs fresh verification.'}
  $config.qrPilotId=$PilotId;$config.qrActivationSha256=(Get-FileHash "$qr/activation.json").Hash.ToLowerInvariant()
 }
 & dotnet publish "$root/src/Tools/NexaConnect.LocalPilotTelemetry" -c Release --output "$directory/telemetry" --verbosity quiet -m:1 *> "$directory/telemetry-build.private.log"
 if($LASTEXITCODE -ne 0){throw 'Host telemetry publication failed.'}
 $config.telemetryFiles=Get-TerminalFileHashes "$directory/telemetry"
 $config|ConvertTo-Json -Depth 6|Set-Content "$directory/host.json"
 $principal=New-ScheduledTaskPrincipal -UserId $config.userSid -LogonType Interactive -RunLevel Limited
 $taskAction=New-ScheduledTaskAction -Execute $pwsh -Argument (Get-LocalPilotHostArguments $root $HostId) -WorkingDirectory $root
 $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval ([TimeSpan]::FromMinutes(1)) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
 $arguments=@{TaskName=$config.taskName;Action=$taskAction;Principal=$principal;Settings=$settings;Description="NexaConnect owned normal pilot $HostId"}
 if($EnableLogonStart){$arguments.Trigger=New-ScheduledTaskTrigger -AtLogOn -User $config.userSid}
 $registered=$false
 try{
  if(Get-ScheduledTask -TaskName $config.taskName -ErrorAction SilentlyContinue){throw 'Generated task name already exists.'}
  Register-ScheduledTask @arguments|Out-Null;$registered=$true
  Assert-LocalPilotTask (Get-ScheduledTask -TaskName $config.taskName) $config $root
  New-Item -ItemType Directory (Split-Path $pointer) -Force|Out-Null
  @{version=1;hostId=$HostId;runId=$config.runId}|ConvertTo-Json|Set-Content "$pointer.pending"
  [IO.File]::Move("$pointer.pending",$pointer,$false)
 }catch{
  if($registered){try{Assert-LocalPilotTask (Get-ScheduledTask -TaskName $config.taskName) $config $root;Unregister-ScheduledTask -TaskName $config.taskName -Confirm:$false}catch{Write-Warning 'Registered task ownership needs review; no changed owner was removed.'}}
  throw
 }
 Write-Output "Normal pilot host installed, not started: $HostId. At-logon startup: $($config.enableLogonStart)."
 return
}
$config=Read-LocalPilotHost $root $HostId
$directory=Assert-TerminalPath "$root/.runstate/local-pilot-host/$HostId"
$task=Get-ScheduledTask -TaskName $config.taskName -ErrorAction Stop
Assert-LocalPilotTask $task $config $root
if($Action -eq 'Status'){
 $status=if(Test-Path "$directory/status.json"){Get-LocalPilotHostPublicStatus (Get-Content "$directory/status.json" -Raw|ConvertFrom-Json) $HostId}else{$null}
 [pscustomobject]@{hostId=$HostId;taskState=[string]$task.State;enableLogonStart=$config.enableLogonStart;status=$status;releaseReady=$false}|ConvertTo-Json -Depth 5;return
}
$control=Open-DraftLifecycleLock $directory
try{
 if($Action -in @('Start','Retry')){
  Assert-LocalPilotHostBinding $root $config
  if($Action -eq 'Retry'){
   if(-not $ConfirmTestResources){throw 'Explicit Retry requires -ConfirmTestResources; automatic recovery budgets are not silently reset.'}
   Set-Content "$directory/retry.request" 'operator-retry'
  }
  if(Test-Path "$directory/stop.request"){Remove-Item -LiteralPath "$directory/stop.request"}
  Start-ScheduledTask -TaskName $config.taskName
  $deadline=[DateTimeOffset]::UtcNow.AddMinutes(5)
  do{
   Start-Sleep -Seconds 2
   if(Test-Path "$directory/status.json"){
    $status=Get-Content "$directory/status.json" -Raw|ConvertFrom-Json
    $p=Get-Process -Id $status.processId -ErrorAction SilentlyContinue
    if($p -and $p.StartTime.ToUniversalTime().Ticks -eq $status.startTicks -and ([DateTimeOffset]::UtcNow-[DateTimeOffset]$status.observedAtUtc).TotalSeconds -lt 20){
     if(Test-LocalPilotHostReady $status){Write-Output 'Owned background host running. Business access remains unverified.';return}
     if($status.phase -in @('blocked-owner','blocked-budget','degraded-live-process','recovery-blocked')){throw 'Background recovery is blocked; inspect owned host status and private diagnostics.'}
    }
   }
  }while([DateTimeOffset]::UtcNow -lt $deadline)
  throw 'Background startup is still pending; inspect Status. No POS was opened.'
 }
 if(Get-Process NexaConnect.POS -ErrorAction SilentlyContinue){throw 'Finish work and close every POS window before host shutdown or removal.'}
 if($Action -eq 'Stop'){
  Set-Content "$directory/stop.request" 'operator-stop'
  if($task.State -ne 'Running'){
   $oldHostFlag=$env:NEXACONNECT_LOCAL_PILOT_HOST_ID
   try{
    $env:NEXACONNECT_LOCAL_PILOT_HOST_ID=$HostId
    if($config.qrPilotId){& "$PSScriptRoot/qr-pilot.ps1" -Action Stop -PilotId $config.qrPilotId}
    & "$PSScriptRoot/first-draft-stack.ps1" -Action Stop -RunId $config.runId
   }finally{$env:NEXACONNECT_LOCAL_PILOT_HOST_ID=$oldHostFlag}
  }
  else{
   $deadline=[DateTimeOffset]::UtcNow.AddMinutes(2)
   do{Start-Sleep -Seconds 2;$task=Get-ScheduledTask -TaskName $config.taskName}while($task.State -eq 'Running' -and [DateTimeOffset]::UtcNow -lt $deadline)
   if($task.State -eq 'Running'){throw 'Graceful stop is pending; no task or POS was force-killed.'}
  }
  Write-Output 'Owned background host stopped; retained volumes and recovery preserved.';return
 }
 if($task.State -eq 'Running'){throw 'Stop the host before removal.'}
 $run=Get-DraftRunDirectory $root $config.runId;$state=Get-Content "$run/state.json" -Raw|ConvertFrom-Json -AsHashtable
 if($state.status -cne 'Stopped' -or $state.processes.Count){throw 'Stop the owned services before removing supervision.'}
 $pointer=Assert-TerminalPath "$root/.runstate/local-pilot/$($config.runId)/host-current.json"
 $value=Get-Content $pointer -Raw|ConvertFrom-Json
 if($value.hostId -cne $HostId){throw 'Current host pointer changed; it was preserved.'}
 Unregister-ScheduledTask -TaskName $config.taskName -Confirm:$false
 Remove-Item -LiteralPath $pointer
 Write-Output 'Owned task removed. Host diagnostics, databases and POS recovery retained.'
}finally{$control.Dispose()}
