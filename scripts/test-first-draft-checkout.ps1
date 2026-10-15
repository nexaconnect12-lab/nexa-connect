#requires -Version 7.4
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure,[switch]$VerifyTerminalPackage)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'payment-review-joined-helpers.ps1')
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm generated local draft startup, financial test commands and exact cleanup.'}
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$RunId=[Guid]::NewGuid().ToString('N');$run=Get-DraftRunDirectory $root $RunId
$old=@{};$passed=$false;$cleanup=$false;$checks=[ordered]@{}
function Phase([string]$Mode){
 [Environment]::SetEnvironmentVariable('NEXACONNECT_DRAFT_CHECKOUT_MODE',$Mode,'Process')
 Push-Location (Join-Path $root 'src/Frontend')
 try{& node e2e/first-draft/checkout.mjs;if($LASTEXITCODE -ne 0){throw "Joined draft failed: $Mode"}}finally{Pop-Location}
 $script:checks[$Mode]=$true
}
function Owned-Broker([string]$Action){
 $localEndpoint=& docker context inspect --format '{{.Endpoints.docker.Host}}'
 if($env:DOCKER_HOST -or $localEndpoint -notmatch '^npipe://'){throw 'Local Docker context required.'}
 $previous=Set-DraftComposeEnvironment $script:secrets $RunId ([uri]$script:state.urls.Bff).Port $script:state.infrastructure
 try{
  $compose=Get-DraftComposeArguments $root $RunId
  $container=& docker @compose ps -aq rabbitmq
  if($LASTEXITCODE -ne 0 -or $container -notmatch '^[a-f0-9]{12,64}$' -or (& docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' $container) -ne "nexa-draft-$RunId"){throw 'Broker ownership mismatch.'}
  & docker @compose $Action rabbitmq
  if($LASTEXITCODE -ne 0){throw 'Owned broker operation failed.'}
  if($Action -eq 'start'){
   $until=[DateTimeOffset]::UtcNow.AddSeconds(60)
   do{$health=& docker inspect --format '{{.State.Health.Status}}' $container;if($health -eq 'healthy'){
       $published=& docker @compose port rabbitmq 5672
       if((ConvertFrom-ReviewJoinedPort $published) -ne $script:state.infrastructure.rabbitmq){throw 'Broker restart changed its retained port.'}
       return
      };Start-Sleep -Milliseconds 500}while([DateTimeOffset]::UtcNow -lt $until)
   throw 'Owned broker recovery readiness failed.'
  }
 }finally{Restore-DraftEnvironment $previous}
}
function Owner-Sql([string]$Suffix,[string]$Sql,[hashtable]$Variables=@{}){
 $previous=Set-DraftComposeEnvironment $script:secrets $RunId ([uri]$script:state.urls.Bff).Port $script:state.infrastructure
 try{
  $compose=Get-DraftComposeArguments $root $RunId
  $arguments=@('exec','-T','-e','PGPASSWORD','postgres','psql','-h','127.0.0.1','-U',"nexaconnect_${Suffix}_app",'-d',"nexa_review_it_${RunId}_$Suffix",'-t','-A','--set=ON_ERROR_STOP=1')
  foreach($entry in $Variables.GetEnumerator()){$arguments+="--set=$($entry.Key)=$($entry.Value)"}
  $value=($Sql | & docker @compose @arguments) -join ''
  if($LASTEXITCODE -ne 0){throw "Owning $Suffix verification query failed."}
  return $value.Trim()
 }finally{Restore-DraftEnvironment $previous}
}
try{
 & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Start -RunId $RunId -ConfirmTestResources
 & (Join-Path $PSScriptRoot 'provision-first-draft.ps1') -RunId $RunId -ConfirmTestResources
 if($VerifyTerminalPackage){
  & (Join-Path $PSScriptRoot 'first-draft-terminal.ps1') -Action Package -RunId $RunId
  $packages=@(Get-ChildItem (Join-Path $run 'terminal-packages') -Directory)
  if($packages.Count -ne 1){throw 'Expected one fresh terminal package.'}
  $installation=Join-Path $run 'terminal-install'
  & (Join-Path $PSScriptRoot 'first-draft-terminal.ps1') -Action Install -PackageDirectory $packages[0].FullName -InstallDirectory $installation
  & (Join-Path $PSScriptRoot 'first-draft-terminal.ps1') -Action Install -PackageDirectory $packages[0].FullName -InstallDirectory $installation
  $status=& (Join-Path $PSScriptRoot 'first-draft-terminal.ps1') -Action Status -InstallDirectory $installation
  if($status.runId -cne $RunId){throw 'Installed terminal identity mismatch.'}
  $checks.terminalPackage=$true
 }
 $state=Get-Content (Join-Path $run 'state.json') -Raw|ConvertFrom-Json -AsHashtable
 $secrets=Read-DraftSecrets (Join-Path $run 'secrets.clixml')
 $settings=@{NEXACONNECT_DRAFT_RUN=$RunId;NEXACONNECT_DRAFT_STATE=(Join-Path $run 'state.json');NEXACONNECT_DRAFT_REFERENCE=(Join-Path $run 'reference.json');NEXACONNECT_DRAFT_CHECKOUT_MODE='baseline';NEXACONNECT_DRAFT_CASHIER_PASSWORD=$secrets.READER_PASSWORD;NEXACONNECT_DRAFT_KITCHEN_PASSWORD=$secrets.SECONDMANAGER_PASSWORD;NEXACONNECT_DRAFT_MANAGER_PASSWORD=$secrets.RESOLVER_PASSWORD;NEXACONNECT_DRAFT_ACCOUNTANT_PASSWORD=$secrets.ACCOUNTANT_PASSWORD;PGPASSWORD=$secrets.RUNTIME_PASSWORD}
 foreach($entry in $settings.GetEnumerator()){$old[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key,'Process');[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
 $reference=Get-Content (Join-Path $run 'reference.json') -Raw|ConvertFrom-Json
 # Test-only explicit observer override, in owning Authorization persistence. No transition grant.
 $observerSql=@'
INSERT INTO authorization_user_permission_overrides(id,subject_id,scope_id,permission_code,effect,status)
SELECT gen_random_uuid(), :'subject', id, 'kitchen.ticket.read','allow','active'
FROM authorization_resource_scopes WHERE organization_id=:'organization'::uuid AND restaurant_id=:'restaurant'::uuid AND branch_id=:'branch'::uuid;
'@
 Owner-Sql 'authorization' $observerSql @{subject=$reference.subjects.accountant;organization=$reference.organizationId;restaurant=$reference.restaurantId;branch=$reference.branchId}|Out-Null
 Phase 'baseline'
 & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action StopService -RunId $RunId -Service Kitchen
 Phase 'interrupted'
 & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action StopService -RunId $RunId -Service Order
 $previousHostEnvironment=[Environment]::GetEnvironmentVariable('DOTNET_ENVIRONMENT','Process')
 try{
  [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT','Production','Process')
  & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Restart -RunId $RunId -Service Order
  & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Restart -RunId $RunId -Service Kitchen
 }finally{[Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT',$previousHostEnvironment,'Process')}
 Phase 'recovered'
 Owned-Broker 'stop'
 Phase 'broker-paused'
 $pendingSql="SELECT count(*) FROM outbox_messages WHERE event_type='order.manual-tender-settled.v1' AND published_at_utc IS NULL;"
 if([int](Owner-Sql 'order' $pendingSql) -lt 1){throw 'Broker outage did not retain an unpublished manual settlement.'}
 Owned-Broker 'start'
 Phase 'broker-resumed'
 $memo=Get-Content (Join-Path $run 'reference.json.checkout.json') -Raw|ConvertFrom-Json
 $variables=@{organization=$reference.organizationId;cancel_order=$memo.orders.cancel.command.orderId;compensation_order=$memo.orders.compensation.command.orderId;interrupted_order=$memo.orders.interrupted.command.orderId}
 $queries=@{
  order="SELECT count(*) FROM order_manual_tender_settlements WHERE organization_id=:'organization'::uuid;"
  kitchen="SELECT count(*) FROM kitchen_tickets WHERE organization_id=:'organization'::uuid;"
  inventory="SELECT count(*) FROM inventory_audit_records WHERE organization_id=:'organization'::uuid AND action='inventory.reservation.released';"
  pos="SELECT count(*) FROM pos_order_settlements WHERE organization_id=:'organization'::uuid;"
  reporting="SELECT count(*) FROM sales_facts WHERE organization_id=:'organization'::uuid;"
  payment="SELECT count(*) FROM payment_intents WHERE organization_id=:'organization'::uuid;"
 }
 $expected=@{order=3;kitchen=10;inventory=2;pos=3;reporting=3;payment=1}
 $until=[DateTimeOffset]::UtcNow.AddSeconds(60)
 foreach($suffix in $queries.Keys){do{$actual=[int](Owner-Sql $suffix $queries[$suffix] $variables);if($actual -eq $expected[$suffix]){break};Start-Sleep -Milliseconds 500}while([DateTimeOffset]::UtcNow -lt $until);if($actual -ne $expected[$suffix]){throw "Owning $suffix effect count mismatch."}}
 if((Owner-Sql 'inventory' "SELECT count(DISTINCT reservation_id) FROM inventory_reservation_lines WHERE organization_id=:'organization'::uuid AND order_id=:'interrupted_order'::uuid;" $variables) -ne '1'){throw 'Interrupted checkout duplicated Inventory reservation.'}
 if((Owner-Sql 'order' "SELECT count(*) FROM orders WHERE organization_id=:'organization'::uuid AND receipt_snapshot IS NOT NULL;" $variables) -ne '3'){throw 'Paid receipt count mismatch.'}
 $checks.owningDatabaseEffects=$true;$passed=$true
}finally{
 Restore-DraftEnvironment $old
 if(Test-Path (Join-Path $run 'state.json')){
  & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Reset -RunId $RunId -ConfirmTestResources
  $cleanup=(Get-Content (Join-Path $run 'state.json') -Raw|ConvertFrom-Json).status -eq 'Reset'
  @{runId=$RunId;passed=($passed -and $cleanup);checks=$checks;cleanupVerified=$cleanup;sourceRevision=(& git -C $root rev-parse HEAD);sourceDirty=[bool](& git -C $root status --porcelain);providerAdapter='Disabled';externalMoneyTransferVerified=$false;windowsPrintingVerified=$false}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $run 'checkout-verification.json')
 }
 if(-not $cleanup){throw 'Joined draft cleanup could not be verified.'}
}
Write-Output "Joined draft checkout verification passed: $run/checkout-verification.json"
