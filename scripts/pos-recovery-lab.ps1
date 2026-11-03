#requires -Version 7.4
[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidateSet('Prepare','Status','Launch','RestoreCallback','Stop','Resume','Reset','ExpireCashierSession','CutOrder','RestoreOrder')][string]$Action,
 [string]$LabId,[switch]$ConfirmTestResources
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
if(-not $IsWindows){throw 'Recovery lab requires the current Windows test account.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-release-helpers.ps1"
. "$PSScriptRoot/first-draft-uat-helpers.ps1"
. "$PSScriptRoot/pos-recovery-lab-helpers.ps1"
if($Action -ceq 'Prepare'){
 if($LabId -or -not $ConfirmTestResources){throw 'Prepare requires test-resource confirmation and always generates a new lab; existing runs cannot be adopted.'}
 $LabId=[guid]::NewGuid().ToString('N')
 $directory=Assert-TerminalPath "$root/.runstate/pos-recovery-lab/$LabId"
 New-Item -ItemType Directory -Path $directory|Out-Null;Protect-DraftDirectory $directory
 $lab=@{version=1;kind='nexaconnect-pos-recovery-lab';id=$LabId;runId=$LabId;phase='Preparing'}
 $lab|ConvertTo-Json|Set-Content -LiteralPath "$directory/lab.json"
}else{
 $lab=Read-PosRecoveryLab $root $LabId
 $directory=Assert-TerminalPath "$root/.runstate/pos-recovery-lab/$LabId"
}
$operation=Open-DraftLifecycleLock $directory
$run=Assert-TerminalPath (Get-DraftRunDirectory $root $LabId)
function Save-Lab {$script:lab|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$directory/lab.json"}
function Invoke-LabChild([string]$Name,[string]$Script,[string[]]$Arguments){
 # Native pipeline EOF can be inherited by long-lived service grandchildren. Direct file
 # redirection and WaitForExit wait only for the launcher, not its background service tree.
 $process=Start-Process (Get-Command pwsh).Source -ArgumentList (@('-NoProfile','-File',('"'+"$PSScriptRoot/$Script"+'"'))+$Arguments) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$directory/$Name.private.log" -RedirectStandardError "$directory/$Name.error.private.log"
 [void]$process.Handle;$process.WaitForExit()
 if($process.ExitCode -ne 0){throw "Recovery lab operation failed: $Name. Preserve its protected diagnostics; no retained pilot was selected."}
}
function Read-State {ConvertFrom-UatJson (Get-Content -LiteralPath "$run/state.json" -Raw)}
function Assert-LabSource {
 $target=ConvertFrom-UatJson (Get-Content -LiteralPath "$directory/target.json" -Raw)
 Assert-TerminalCandidateUnchanged $target.source (Get-DraftCandidate $root)
}
function Lab-Admin($State){
 $secrets=Read-DraftSecrets "$run/secrets.clixml"
 $admin=Invoke-RestMethod "http://127.0.0.1:$($State.infrastructure.keycloak)/realms/master/protocol/openid-connect/token" -Method Post -Body @{grant_type='password';client_id='admin-cli';username='acceptance-admin';password=$secrets.KEYCLOAK_ADMIN_PASSWORD}
 return @{Authorization='Bearer '+$admin.access_token}
}
function Get-LabExecutable {
 $target=ConvertFrom-UatJson (Get-Content -LiteralPath "$directory/target.json" -Raw);Assert-UatId $target.releaseId
 $package=Assert-TerminalPath "$run/terminal-packages/$($target.releaseId)"
 $manifest=Read-TerminalPackage $package
 if($manifest.runId -cne $LabId){throw 'Lab package belongs to another run.'}
 return Join-Path $package 'NexaConnect.POS.exe'
}
function Assert-LabClosed {
 foreach($process in @(Get-Process -Name NexaConnect.POS -ErrorAction SilentlyContinue)){
  if($process.Path -ceq (Get-LabExecutable)){throw 'Finish the lab POS action and close its window first. No process was killed.'}
 }
}
function Read-Callback {
 $values=@{}
 foreach($entry in @(@('root','Software\Classes\nexaconnect-pos',''),@('url','Software\Classes\nexaconnect-pos','URL Protocol'),@('command','Software\Classes\nexaconnect-pos\shell\open\command',''))){
  $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($entry[1])
  try{
   $exists=$null -ne $key -and $entry[2] -in $key.GetValueNames()
   if($exists -and $key.GetValueKind($entry[2]) -ne [Microsoft.Win32.RegistryValueKind]::String){throw 'Unexpected callback value type; preserve it for review.'}
   $values[$entry[0]]=@{exists=$exists;value=if($exists){[string]$key.GetValue($entry[2])}else{''}}
  }finally{if($key){$key.Dispose()}}
 }
 return $values
}
function Restore-Callback {
 $file=Assert-TerminalPath "$directory/callback.json"
 if(-not(Test-Path -LiteralPath $file)){return}
 Assert-LabClosed
 $saved=ConvertFrom-UatJson (Get-Content -LiteralPath $file -Raw)
 $current=Read-Callback
 $unchanged=$true;foreach($name in @('root','url','command')){if($current[$name].exists -cne $saved.values[$name].exists -or $current[$name].value -cne $saved.values[$name].value){$unchanged=$false}}
 if($unchanged){[IO.File]::Move($file,(Join-Path $directory ('callback-restored-'+[guid]::NewGuid().ToString('N')+'.json')));return}
 $values=Get-PosRecoveryProtocolRestore $saved.values $current.command.value ('"'+(Get-LabExecutable)+'" "%1"')
 foreach($entry in @(@('root','Software\Classes\nexaconnect-pos',''),@('url','Software\Classes\nexaconnect-pos','URL Protocol'),@('command','Software\Classes\nexaconnect-pos\shell\open\command',''))){
  $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($entry[1])
  try{if($values[$entry[0]].exists){$key.SetValue($entry[2],$values[$entry[0]].value,[Microsoft.Win32.RegistryValueKind]::String)}else{$key.DeleteValue($entry[2],$false)}}finally{$key.Dispose()}
 }
 $restored=Read-Callback
 foreach($name in @('root','url','command')){if($restored[$name].exists -cne $saved.values[$name].exists -or $restored[$name].value -cne $saved.values[$name].value){throw 'Callback changed during restoration; preserve the backup for review.'}}
 [IO.File]::Move($file,(Join-Path $directory ('callback-restored-'+[guid]::NewGuid().ToString('N')+'.json')))
}
try{
 if($Action -ceq 'Prepare'){
  if(Test-Path -LiteralPath "$run/state.json"){throw 'New lab must not adopt an existing draft.'}
  $source=Get-DraftCandidate $root
  Invoke-LabChild stack first-draft-stack.ps1 @('-Action','Start','-RunId',$LabId,'-ConfirmTestResources','-EnableDrawerHistory')
  Invoke-LabChild provision provision-first-draft.ps1 @('-RunId',$LabId,'-ConfirmTestResources')
  $state=Read-State
  $secrets=Read-DraftSecrets "$run/secrets.clixml"
  $headers=Lab-Admin $state
  $base="http://127.0.0.1:$($state.infrastructure.keycloak)/admin/realms/$($state.realm)"
  $settings=@{}
  try{
   foreach($name in @('lab-cashier-other','lab-wrong-branch')){
    $secretKey=$name.ToUpperInvariant().Replace('-','_')+'_PASSWORD'
    $secrets[$secretKey]='Aa1!'+[guid]::NewGuid().ToString('N')+[guid]::NewGuid().ToString('N')
    Save-DraftSecrets "$run/secrets.clixml" $secrets
    $user=@{username=$name;enabled=$true;emailVerified=$true;firstName='Lab';lastName='Tester';email="$name@nexa.invalid";credentials=@(@{type='password';value=$secrets[$secretKey];temporary=$false})}
    Invoke-RestMethod "$base/users" -Method Post -Headers $headers -ContentType 'application/json' -Body ($user|ConvertTo-Json -Depth 6)|Out-Null
    $users=@(Invoke-RestMethod "$base/users?username=$name&exact=true" -Headers $headers|ForEach-Object{$_})
    if($users.Count -ne 1){throw 'Generated lab user mismatch.'}
    $role=Invoke-RestMethod "$base/roles/customer-user" -Headers $headers
    Invoke-RestMethod "$base/users/$($users[0].id)/role-mappings/realm" -Method Post -Headers $headers -ContentType 'application/json' -Body (ConvertTo-Json -InputObject @($role))|Out-Null
    $settings['NEXACONNECT_DRAFT_SUBJECT_'+$name]=$users[0].id
   }
   foreach($suffix in @('platform','authorization')){
    $builder=[System.Data.Common.DbConnectionStringBuilder]::new();$builder['Host']='127.0.0.1';$builder['Port']=$state.infrastructure.postgres
    $builder['Database']="nexa_review_it_${LabId}_$suffix";$builder['Username']=if($suffix -ceq 'platform'){'platform_directory_app'}else{'nexaconnect_authorization_app'};$builder['Password']=$secrets.RUNTIME_PASSWORD
    $settings['NEXACONNECT_DRAFT_DB_'+$suffix]=$builder.ConnectionString
   }
   $settings.DOTNET_ENVIRONMENT='Development';$settings.NEXACONNECT_DRAFT_RUN=$LabId;$settings.NEXACONNECT_DRAFT_REFERENCE="$run/reference.json";$settings.NEXACONNECT_DRAFT_RECOVERY_LAB=$LabId
   $previous=@{}
   foreach($entry in $settings.GetEnumerator()){$previous[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key);[Environment]::SetEnvironmentVariable($entry.Key,[string]$entry.Value)}
   try{& dotnet "$run/provisioner/NexaConnect.FirstDraftProvisioning.dll" --recovery-lab-access *> "$directory/access.private.log";if($LASTEXITCODE){throw 'Service-owned lab role provisioning failed.'}}finally{Restore-DraftEnvironment $previous}
  }finally{$settings.Clear()}
  # Only this generated realm receives short test access tokens. Retained realms are never selected.
  $realm=Invoke-RestMethod $base -Headers $headers
  $realm.accessTokenLifespan=30;$realm.ssoSessionIdleTimeout=300;$realm.ssoSessionMaxLifespan=900
  Invoke-RestMethod $base -Method Put -Headers $headers -ContentType 'application/json' -Body ($realm|ConvertTo-Json -Depth 40)|Out-Null
  $profile=ConvertFrom-UatJson (Get-Content -LiteralPath "$run/pos-configuration.json" -Raw)
  $profile.Pos.UseIsolatedLocalPilotStorage=$true;$profile.Session.TokenRefreshLeadSeconds=15
  $reference=ConvertFrom-UatJson (Get-Content -LiteralPath "$run/reference.json" -Raw)
  Assert-PosRecoveryLabProfile $profile $state $reference
  foreach($file in @('pos-configuration.json','pos/appsettings.json')){$profile|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$run/$file"}
  Invoke-LabChild package first-draft-terminal.ps1 @('-Action','Package','-RunId',$LabId)
  $packages=@(Get-ChildItem -LiteralPath "$run/terminal-packages" -Directory)
  if($packages.Count -ne 1){throw 'Expected one fresh lab package.'}
  $package=Read-TerminalPackage $packages[0].FullName
  Assert-TerminalCandidateUnchanged $source (Get-DraftCandidate $root)
  $target=@{version=1;runId=$LabId;releaseId=$package.releaseId;source=$source;
   configurationHash=(Get-FileHash "$($packages[0].FullName)/appsettings.json").Hash.ToLowerInvariant();
   referenceHash=(Get-FileHash "$run/reference.json").Hash.ToLowerInvariant();backendArtifacts=Get-DraftQrPilotArtifactHash $run}
  $target|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$directory/target.json"
  $lab.phase='Ready';Save-Lab
  [void](Get-PosRecoveryLabBinding $root $LabId)
  Write-Output "Recovery lab prepared: $LabId. No POS was launched or callback changed."
  Write-Output 'Prepare separate lab observations using pos-acceptance.ps1 -Action Prepare -RecoveryLabId <lab-id>. Retrieve test passwords locally only.'
  return
 }
 if($Action -ceq 'Status'){
  $valid=$false;try{[void](Get-PosRecoveryLabBinding $root $LabId);$valid=$true}catch{}
  @{labId=$LabId;phase=$lab.phase;bindingValid=$valid;operatorAcceptance=$false;releaseReady=$false}|ConvertTo-Json;return
 }
 if($Action -ceq 'RestoreCallback'){Restore-Callback;Write-Output 'Original callback restored or already unchanged.';return}
 if($Action -ceq 'Launch'){
  [void](Get-PosRecoveryLabBinding $root $LabId)
  if(@(Get-Process -Name NexaConnect.POS -ErrorAction SilentlyContinue).Count){throw 'Finish the normal POS action and close its window first. Only one POS per Windows account may run.'}
  Restore-Callback
  @{values=(Read-Callback)}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$directory/callback.json"
  $exe=Get-LabExecutable
  Write-Output 'Launching the isolated cash-only LAB POS. Use synthetic orders and lab accounts. This command waits until the window closes, then restores the callback.'
  try{
   $process=Start-Process -FilePath $exe -PassThru;[void]$process.Handle
   # Fault controls need the lab lifecycle lock while the interactive POS is running.
   $operation.Dispose();$operation=$null
   $process.WaitForExit();if($process.ExitCode -ne 0){throw 'Lab POS exited abnormally; preserve recovery.'}
  }finally{Restore-Callback}
  return
 }
 if(-not $ConfirmTestResources){throw 'Explicit test-resource confirmation is required for lab lifecycle and faults.'}
 if($Action -in @('Stop','Reset')){
  if(Test-Path -LiteralPath "$directory/target.json"){Assert-LabClosed;Restore-Callback}
  Invoke-LabChild $Action first-draft-stack.ps1 @('-Action',$Action,'-RunId',$LabId,'-ConfirmTestResources')
  $lab.phase=if($Action -ceq 'Reset'){'Reset'}else{'Stopped'};Save-Lab;return
 }
 if($Action -ceq 'Resume'){
  if($lab.phase -cne 'Stopped'){throw 'Resume only a stopped lab.'}
  Assert-LabSource
  Invoke-LabChild resume first-draft-stack.ps1 @('-Action','Start','-RunId',$LabId,'-NoBuild','-ConfirmTestResources')
  $lab.phase='Ready';Save-Lab;[void](Get-PosRecoveryLabBinding $root $LabId);return
 }
 if($lab.phase -cne 'Ready'){throw 'Fault actions require the prepared lab.'}
 Assert-LabSource
 if($Action -ceq 'RestoreOrder'){
  Invoke-LabChild restore-order first-draft-stack.ps1 @('-Action','Restart','-RunId',$LabId,'-Service','Order')
  [void](Get-PosRecoveryLabBinding $root $LabId);return
 }
 [void](Get-PosRecoveryLabBinding $root $LabId)
 if($Action -ceq 'CutOrder'){
  Invoke-LabChild cut-order first-draft-stack.ps1 @('-Action','StopService','-RunId',$LabId,'-Service','Order');return
 }
 if($Action -ceq 'ExpireCashierSession'){
  $state=Read-State;$headers=Lab-Admin $state
  $reference=ConvertFrom-UatJson (Get-Content -LiteralPath "$run/reference.json" -Raw)
  $subject=$reference.subjects.cashier;$parsedSubject=[guid]::Empty
  if(-not [guid]::TryParse([string]$subject,[ref]$parsedSubject)){throw 'Invalid original lab cashier.'}
  Invoke-RestMethod "http://127.0.0.1:$($state.infrastructure.keycloak)/admin/realms/$($state.realm)/users/$subject/logout" -Method Post -Headers $headers|Out-Null
  Write-Output 'Original lab cashier sessions ended at the real identity provider. Wait for refresh/expiry while active; preserve original custody/recovery.'
 }
}catch{
 if($Action -ceq 'Prepare'){$lab.phase='Failed';Save-Lab}
 throw
}finally{if($operation){$operation.Dispose()}}
