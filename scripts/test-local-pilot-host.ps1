#requires -Version 7.4
param([switch]$ConfirmTestResources)
$ErrorActionPreference='Stop'
if(-not $ConfirmTestResources){throw 'Confirm disposable Windows host, Task Scheduler and isolated Docker fixtures.'}
if(Get-Process NexaConnect.POS -ErrorAction SilentlyContinue){throw 'Finish work and close POS before the isolated installer/recovery fixture.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/local-pilot-host-helpers.ps1"
$id=[guid]::NewGuid().ToString('N');$run=Get-DraftRunDirectory $root $id
$work=Assert-TerminalPath "$root/.runstate/local-pilot-host-tests/$id"
New-Item -ItemType Directory $work|Out-Null;Protect-DraftDirectory $work
$installedHost='';$passed=$false;$cleanup=$false
$normal=Join-Path $env:LOCALAPPDATA 'NexaConnect/DraftTerminal/active.json'
$normalHash=if(Test-Path $normal){(Get-FileHash $normal).Hash}else{''}
function Child([string]$Name,[string]$Script,[string[]]$Arguments){
 $p=Start-Process (Get-Command pwsh).Source -ArgumentList (@('-NoProfile','-File',"`"$PSScriptRoot/$Script`"")+$Arguments) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$work/$Name.private.log" -RedirectStandardError "$work/$Name.error.private.log"
 [void]$p.Handle;$p.WaitForExit();if($p.ExitCode -ne 0){throw "Disposable host fixture step failed: $Name"}
}
function Wait-Running {
 $deadline=[DateTimeOffset]::UtcNow.AddMinutes(6)
 do{
  if(Test-Path "$root/.runstate/local-pilot-host/$installedHost/status.json"){
   $s=Get-Content "$root/.runstate/local-pilot-host/$installedHost/status.json" -Raw|ConvertFrom-Json
   if($s.phase -eq 'running' -and ([DateTimeOffset]::UtcNow-[DateTimeOffset]$s.observedAtUtc).TotalSeconds -lt 15){return}
   if($s.phase -in @('blocked-owner','blocked-budget','degraded-live-process','recovery-blocked')){throw 'Disposable host recovery blocked.'}
  }
  Start-Sleep -Seconds 2
 }while([DateTimeOffset]::UtcNow -lt $deadline)
 throw 'Disposable host did not reach running state.'
}
try{
 # The generated QR profile selects isolated native storage, but no LAN edge or guest QR is issued.
 Child 'stack' 'first-draft-stack.ps1' @('-Action','Start','-RunId',$id,'-QrPilotOrigin','https://192.168.250.250:8443','-ConfirmTestResources')
 Child 'provision' 'provision-first-draft.ps1' @('-RunId',$id,'-ConfirmTestResources')
 Child 'package' 'first-draft-terminal.ps1' @('-Action','Package','-RunId',$id)
 $package=@(Get-ChildItem "$run/terminal-packages" -Directory)
 if($package.Count -ne 1){throw 'Expected a single disposable package.'}
 $install=Assert-TerminalPath "$run/host-fixture-terminal"
 Child 'install-terminal' 'first-draft-terminal.ps1' @('-Action','Install','-PackageDirectory',"`"$($package[0].FullName)`"",'-InstallDirectory',"`"$install`"")
 Child 'install-host' 'local-pilot-host.ps1' @('-Action','Install','-InstallDirectory',"`"$install`"",'-ConfirmTestResources')
 $installedHost=(Get-Content "$root/.runstate/local-pilot/$id/host-current.json" -Raw|ConvertFrom-Json).hostId
 Child 'stop-before-start' 'local-pilot-host.ps1' @('-Action','Stop','-HostId',$installedHost)
 Child 'start-host' 'local-pilot-host.ps1' @('-Action','Start','-HostId',$installedHost)
 Wait-Running
 $before=Get-Content "$run/state.json" -Raw|ConvertFrom-Json -AsHashtable
 $oldHost=$env:NEXACONNECT_LOCAL_PILOT_HOST_ID
 try{$env:NEXACONNECT_LOCAL_PILOT_HOST_ID=$installedHost;Child 'catalog-exit' 'first-draft-stack.ps1' @('-Action','StopService','-RunId',$id,'-Service','Catalog')}finally{$env:NEXACONNECT_LOCAL_PILOT_HOST_ID=$oldHost}
 $deadline=[DateTimeOffset]::UtcNow.AddMinutes(6)
 do{
  Start-Sleep -Seconds 2
  $after=Get-Content "$run/state.json" -Raw|ConvertFrom-Json -AsHashtable
  $recovered=$after.processes.Contains('Catalog') -and $after.processes.Catalog.id -ne $before.processes.Catalog.id
  if($recovered){try{$recovered=(Invoke-WebRequest ($after.urls.Catalog+'openapi/v1.json') -SkipHttpErrorCheck -TimeoutSec 2).StatusCode -eq 401}catch{$recovered=$false}}
 }while(-not $recovered -and [DateTimeOffset]::UtcNow -lt $deadline)
 if(-not $recovered){throw 'Exited disposable Catalog was not recovered.'}
 Wait-Running
 $after=Get-Content "$run/state.json" -Raw|ConvertFrom-Json -AsHashtable
 foreach($name in $before.processes.Keys){if($name -ne 'Catalog' -and $after.processes[$name].startTicks -ne $before.processes[$name].startTicks){throw 'An existing healthy dependency was restarted.'}}
 Child 'stop-host' 'local-pilot-host.ps1' @('-Action','Stop','-HostId',$installedHost)
 Child 'restart-host' 'local-pilot-host.ps1' @('-Action','Start','-HostId',$installedHost)
 Wait-Running
 if($normalHash -and (Get-FileHash $normal).Hash -cne $normalHash){throw 'Normal installation changed.'}
 $passed=$true
}finally{
 try{
  if($installedHost){
   try{Child 'cleanup-stop' 'local-pilot-host.ps1' @('-Action','Stop','-HostId',$installedHost)}catch{
    # Fixture-only task termination: no retained run, actual cashier or physical device is selected.
    $config=Read-LocalPilotHost $root $installedHost
    Assert-LocalPilotTask (Get-ScheduledTask -TaskName $config.taskName) $config $root
    Stop-ScheduledTask -TaskName $config.taskName
    Child 'cleanup-stop-after-task' 'local-pilot-host.ps1' @('-Action','Stop','-HostId',$installedHost)
   }
   Child 'cleanup-remove' 'local-pilot-host.ps1' @('-Action','Remove','-HostId',$installedHost)
  }
  if(Test-Path "$run/state.json"){Child 'cleanup-reset' 'first-draft-stack.ps1' @('-Action','Reset','-RunId',$id,'-ConfirmTestResources')}
  $cleanup=(-not(Test-Path "$root/.runstate/local-pilot/$id/host-current.json")) -and ($null -eq (Get-ScheduledTask -TaskName "NexaConnect-LocalPilot-$installedHost" -ErrorAction SilentlyContinue))
 }catch{$cleanup=$false}
 @{version=1;runId=$id;hostId=$installedHost;kind='disposable-real-local-pilot-host';passed=($passed -and $cleanup);cleanupVerified=$cleanup;financialActions=0;normalInstallationMutated=$false;nativePosLaunched=$false;rebootVerified=$false;releaseReady=$false}|ConvertTo-Json|Set-Content "$work/verification.json"
}
if(-not $passed -or -not $cleanup){throw 'Disposable host verification or cleanup failed; preserve its diagnostics.'}
Write-Output "PASS: real scheduled host start/exit recovery/healthy-PID preservation/restart/cleanup: $work/verification.json"
