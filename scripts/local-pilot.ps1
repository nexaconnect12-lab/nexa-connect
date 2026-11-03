#requires -Version 7.4
[CmdletBinding()]
param(
 [ValidateSet('Status','Start','Refresh','RefreshQr')][string]$Action='Status',
 [string]$RunId,
 [string]$InstallDirectory=(Join-Path $env:LOCALAPPDATA 'NexaConnect/DraftTerminal'),
 [string]$PilotId,[string]$CandidateDirectory,[switch]$ConfirmTestResources
)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Use the Windows account owning the normal pilot.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-release-helpers.ps1"
. "$PSScriptRoot/local-pilot-helpers.ps1"
$install=Assert-TerminalPath $InstallDirectory
Assert-TerminalInstallOwnership $install
$active=Get-Content "$install/active.json" -Raw|ConvertFrom-Json -AsHashtable
if(-not $RunId){$RunId=$active.runId}
Assert-DraftRunId $RunId
if($active.runId -cne $RunId -or $active.releaseId -cnotmatch '^[a-f0-9]{32}$'){throw 'Selected run differs from installed normal POS.'}
$package=Assert-TerminalPath "$install/releases/$($active.releaseId)"
$manifest=Read-TerminalPackage $package
if($manifest.runId -cne $RunId -or $manifest.releaseId -cne $active.releaseId){throw 'Installed manifest owner differs from selected pilot.'}
$profile=Get-Content "$package/appsettings.json" -Raw|ConvertFrom-Json -AsHashtable
$run=Assert-TerminalPath (Get-DraftRunDirectory $root $RunId)
$state=Get-Content "$run/state.json" -Raw|ConvertFrom-Json -AsHashtable
$reference=Get-Content "$run/reference.json" -Raw|ConvertFrom-Json -AsHashtable
if($PilotId){
 Assert-DraftRunId $PilotId
 $qr=Get-Content (Assert-TerminalPath "$root/.runstate/qr7/$PilotId/plan.json") -Raw|ConvertFrom-Json
 if($qr.stackRunId -cne $RunId){throw 'QR pilot belongs to a different stack.'}
}
Assert-LocalPilotProfile $state $profile $reference (Test-Path "$root/.runstate/pos-recovery-lab/$RunId/lab.json")
Assert-TerminalScopeEqual $manifest.scope (Get-TerminalScope $profile)
Assert-DraftUrls $state.urls
if($state.authority -cne "http://127.0.0.1:$($state.infrastructure.keycloak)/realms/nexa-review-it-$RunId"){throw 'Owned identity endpoint differs.'}
$work=Assert-TerminalPath "$root/.runstate/local-pilot/$RunId"
$hostPointer=Join-Path $work 'host-current.json'
if($Action -in @('Refresh','RefreshQr') -and (Test-Path $hostPointer)){throw 'Stop and remove the owned background host before refresh; its pinned publication must not race maintenance.'}
New-Item -ItemType Directory $work -Force|Out-Null;Protect-DraftDirectory $work
$lock=Open-DraftLifecycleLock $work
try{
function Child([string]$Name,[string]$Script,[string[]]$Arguments){
 $id=[guid]::NewGuid().ToString('N')
 $p=Start-Process (Get-Command pwsh).Source -ArgumentList (@('-NoProfile','-File',"`"$PSScriptRoot/$Script`"")+$Arguments) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$work/$Name-$id.private.log" -RedirectStandardError "$work/$Name-$id.error.private.log"
 [void]$p.Handle;$p.WaitForExit()
 if($p.ExitCode -ne 0){throw "Normal-pilot step failed: $Name. Protected diagnostics are in the run's local-pilot directory; no recovery was deleted."}
}
function Checks {
 $s=Get-Content "$run/state.json" -Raw|ConvertFrom-Json -AsHashtable
 $checks=@()
 foreach($name in $s.urls.Keys){
  $port=([uri]$s.urls[$name]).Port
  $listeners=@(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
  $record=if($s.processes.Contains($name)){$s.processes[$name]}else{$null}
  $p=if($record){Get-Process -Id $record.id -ErrorAction SilentlyContinue}else{$null}
  $owned=$record -and (Assert-DraftProcess $record $p)
  $status='stopped'
  if($listeners.Count -and (-not $owned -or @($listeners|Where-Object OwningProcess -ne $record.id).Count)){$status='changed-owner'}
  elseif($owned){
   $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.id)"
   $expected=Join-Path $run "apps/$name/$($record.assemblyName).dll"
   if($actual.ExecutablePath -cne (Get-Command dotnet).Source -or $actual.CommandLine -notlike "*`"$expected`"*"){$status='changed-owner'}
   elseif($listeners.Count){
    $status='unavailable'
    try{
     # Anonymous/header-only. 401/404 prove a response, never permission/business health.
     $client=[Net.Http.HttpClient]::new([Net.Http.HttpClientHandler]@{AllowAutoRedirect=$false;UseCookies=$false})
     $client.Timeout=[TimeSpan]::FromSeconds(4)
     try{
      $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,[uri]$s.urls[$name])
      $request.Headers.Add('X-Correlation-ID',[guid]::NewGuid().ToString('D'))
      try{$response=$client.SendAsync($request,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult();try{if([int]$response.StatusCode -in @(200,204,401,403,404)){$status='responding'}}finally{$response.Dispose()}}finally{$request.Dispose()}
     }finally{$client.Dispose()}
    }catch{}
   }
  }
  $checks+=@{service=$name;status=$status}
 }
 # Docker's exact loopback port binding identifies the owned Keycloak listener; an HTTP
 # response from a foreign listener cannot substitute for the generated realm.
 if($env:DOCKER_HOST){throw 'Remove DOCKER_HOST; normal-pilot checks require local Docker Desktop.'}
 $endpoint=& docker context inspect --format '{{.Endpoints.docker.Host}}' 2>$null
 if($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^npipe://'){throw 'A local Docker Desktop named-pipe context is required.'}
 $containers=@(& docker ps -q --filter "label=com.docker.compose.project=$($s.project)" --filter 'label=com.docker.compose.service=keycloak' 2>$null)
 if($LASTEXITCODE -ne 0){throw 'Unable to inspect owned identity container.'}
 $mappings=@()
 if($containers.Count -eq 1){
  if($containers[0] -cnotmatch '^[a-f0-9]{12,64}$'){throw 'Invalid container identity.'}
  $mappings=@(& docker port $containers[0] 8080/tcp 2>$null)
  if($LASTEXITCODE -ne 0){throw 'Unable to inspect owned identity port.'}
 }
 $identityPort=[int]$s.infrastructure.keycloak
 $binding=Get-LocalPilotIdentityBinding $mappings $containers.Count $identityPort ([bool]@(Get-NetTCPConnection -State Listen -LocalPort $identityPort -ErrorAction SilentlyContinue).Count)
 $identity=$binding
 if($binding -eq 'owned'){
  $identity='unavailable'
  $client=[Net.Http.HttpClient]::new([Net.Http.HttpClientHandler]@{AllowAutoRedirect=$false;UseCookies=$false});$client.Timeout=[TimeSpan]::FromSeconds(4)
  try{
   $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,($s.authority+'/.well-known/openid-configuration'))
   $request.Headers.Add('X-Correlation-ID',[guid]::NewGuid().ToString('D'))
   try{$response=$client.SendAsync($request,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult();try{if([int]$response.StatusCode -eq 200){$identity='responding'}}finally{$response.Dispose()}}finally{$request.Dispose()}
  }catch{}finally{$client.Dispose()}
 }
 $checks+=@{service='Identity';status=$identity}
 return $checks
}
function Published {
 $json=& node "$PSScriptRoot/release/source.mjs"
 if($LASTEXITCODE -ne 0){throw 'Source inventory failed.'}
 $s=Get-Content "$run/state.json" -Raw|ConvertFrom-Json -AsHashtable
 Assert-LocalPilotPublished $s ($json|ConvertFrom-Json -AsHashtable) (Get-DraftQrPilotArtifactHash $run)
}
$checks=@(Checks)
if($Action -eq 'Status'){
 $matching=$true;try{Published}catch{$matching=$false}
 [pscustomobject]@{target='normal-pilot';runId=$RunId;releaseId=$manifest.releaseId;publishedInputsMatch=$matching;checks=$checks;businessAccessVerified=$false;releaseReady=$false}|ConvertTo-Json -Depth 5
 return
}
if($Action -in @('Refresh','RefreshQr') -and -not $ConfirmTestResources){throw 'Refresh requires -ConfirmTestResources for this existing local test scope.'}
$running=@(Get-Process NexaConnect.POS -ErrorAction SilentlyContinue)
if($Action -in @('Refresh','RefreshQr') -and $running.Count){throw 'Finish the current action and close every POS window before refresh. No process was killed.'}
if($Action -eq 'Start' -and ($running.Count -gt 1 -or @($running|Where-Object {$_.Path -cne (Join-Path $package 'NexaConnect.POS.exe')}).Count)){throw 'A different POS profile is open. Close it first; normal-pilot startup does not adopt a lab or another installation.'}
if($Action -eq 'RefreshQr'){
 Published
 if(-not $PilotId -or -not $CandidateDirectory){throw 'Supply the previous PilotId and fresh QR6 CandidateDirectory.'}
 & "$PSScriptRoot/local-pilot-qr-refresh.ps1" -RunId $RunId -PreviousPilotId $PilotId -CandidateDirectory $CandidateDirectory -ConfirmTestResources
 if($LASTEXITCODE -ne 0){throw 'QR refresh failed; preserve diagnostics.'}
 return
}
$plan=Get-LocalPilotRecoveryPlan $checks ([bool]$running.Count)
if($Action -eq 'Refresh'){
 if($PilotId){Child 'edge-stop' 'qr-pilot.ps1' @('-Action','Stop','-PilotId',$PilotId)}
 Child 'stack-stop' 'first-draft-stack.ps1' @('-Action','Stop','-RunId',$RunId)
 Child 'stack-build' 'first-draft-stack.ps1' @('-Action','Start','-RunId',$RunId)
 $before=@(Get-ChildItem "$run/terminal-packages" -Directory|ForEach-Object Name)
 Child 'package' 'first-draft-terminal.ps1' @('-Action','Package','-RunId',$RunId)
 $new=@(Get-ChildItem "$run/terminal-packages" -Directory|Where-Object Name -NotIn $before)
 if($new.Count -ne 1){throw 'Expected one matching package.'}
 Child 'install' 'first-draft-terminal.ps1' @('-Action','Install','-PackageDirectory',"`"$($new[0].FullName)`"",'-InstallDirectory',"`"$install`"",'-RegisterProtocol')
 Write-Output 'Normal pilot refreshed and installed. Run Start to open POS. QR requires separate fresh candidate activation.'
 return
}
Published
# Verify the native package too; a current backend cannot make an older UI current.
Assert-TerminalCandidateUnchanged @{revision=$manifest.sourceRevision;dirty=$manifest.sourceDirty;inputHash=$manifest.sourceInputHash} (Get-DraftCandidate $root)
if(Test-Path $hostPointer){
 $hostOwner=Get-Content $hostPointer -Raw|ConvertFrom-Json
 if($hostOwner.runId -cne $RunId -or $hostOwner.hostId -cnotmatch '^[a-f0-9]{32}$'){throw 'Invalid background host owner.'}
 # Do not hold maintenance ownership while waiting for the scheduler worker to resume.
 $lock.Dispose();$lock=$null
 & "$PSScriptRoot/local-pilot-host.ps1" -Action Start -HostId $hostOwner.hostId
 $lock=Open-DraftLifecycleLock $work
 $checks=@(Checks)
 if(@($checks|Where-Object status -ne 'responding').Count){throw 'Background host is not fully reachable; POS was not opened.'}
 $plan='open'
}
if($plan -eq 'resume'){
 if(-not $ConfirmTestResources){throw 'Services are unavailable. Close POS and repeat Start -ConfirmTestResources to resume the same published local test stack.'}
 Child 'stack-stop' 'first-draft-stack.ps1' @('-Action','Stop','-RunId',$RunId)
 Child 'stack-resume' 'first-draft-stack.ps1' @('-Action','Start','-RunId',$RunId,'-NoBuild')
 $checks=@(Checks);[void](Get-LocalPilotRecoveryPlan $checks $false)
 if(@($checks|Where-Object status -ne 'responding').Count){throw 'Services remain unavailable; POS was not opened.'}
}
if($PilotId){
 Assert-DraftRunId $PilotId
 $qr=Get-Content (Assert-TerminalPath "$root/.runstate/qr7/$PilotId/plan.json") -Raw|ConvertFrom-Json
 if($qr.stackRunId -cne $RunId){throw 'QR pilot belongs to a different stack.'}
 $edgeDirectory=Assert-TerminalPath "$root/.runstate/qr7/$PilotId"
 $reportJson=& node "$PSScriptRoot/release/qr7.mjs" report $edgeDirectory
 if($LASTEXITCODE -ne 0 -or @((($reportJson|ConvertFrom-Json).prerequisites)).Count){throw 'QR binding is stale or prerequisites are missing. Use RefreshQr with a fresh candidate; POS was not opened.'}
 $activation=Get-Content "$edgeDirectory/activation.json" -Raw|ConvertFrom-Json
 if($activation.backendArtifacts -cne (Get-DraftQrPilotArtifactHash $run)){throw 'QR backend inventory differs.'}
 $edgeReady=$false
 if(Test-Path "$edgeDirectory/edge-process.json"){
  $record=Get-Content "$edgeDirectory/edge-process.json" -Raw|ConvertFrom-Json -AsHashtable
  if(Assert-DraftProcess $record (Get-Process -Id $record.id -ErrorAction SilentlyContinue)){
   $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.id)"
   if($actual.ExecutablePath -cne (Get-Command dotnet).Source -or $actual.CommandLine -notlike "*`"$edgeDirectory\edge\NexaConnect.QrPilotEdge.dll`"*" -or $actual.CommandLine -notlike "*`"$edgeDirectory\edge`"*"){throw 'QR process owner differs.'}
   $edgeReady=@(Get-NetTCPConnection -LocalAddress $qr.listenAddress -LocalPort ([uri]$qr.publicOrigin).Port -State Listen -ErrorAction SilentlyContinue|Where-Object OwningProcess -eq $record.id).Count -gt 0
   if($edgeReady){try{$edgeReady=(Invoke-WebRequest ($qr.publicOrigin+'/order') -TimeoutSec 5 -MaximumRedirection 0).StatusCode -eq 200}catch{$edgeReady=$false}}
  }
 }
 if(-not $edgeReady){Child 'edge-stop' 'qr-pilot.ps1' @('-Action','Stop','-PilotId',$PilotId);Child 'edge-start' 'qr-pilot.ps1' @('-Action','Start','-PilotId',$PilotId)}
}
if($running.Count){Write-Output 'Normal pilot services respond. POS is already open; retry the original action explicitly.';return}
& "$PSScriptRoot/first-draft-terminal.ps1" -Action Launch -InstallDirectory $install
Write-Output "Normal pilot opened: $RunId. Reachability is verified; cashier authorization and financial outcomes remain separate."
}finally{if($lock){$lock.Dispose()}}
