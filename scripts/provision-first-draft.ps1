#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunId,[switch]$ConfirmTestResources,[switch]$NoBuild)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$run=Get-DraftRunDirectory $root $RunId
if(-not $ConfirmTestResources -or -not $IsWindows){throw 'Explicit local Windows test-resource confirmation required.'}
$lock=Open-DraftLifecycleLock $run
$old=@{}
try {
 $state=Get-Content (Join-Path $run 'state.json') -Raw | ConvertFrom-Json -AsHashtable
 if($state.runId -ne $RunId -or $state.project -ne "nexa-draft-$RunId" -or $state.realm -ne "nexa-review-it-$RunId" -or $state.status -ne 'Ready'){throw 'Only the owned Ready draft may be provisioned.'}
 Assert-DraftUrls $state.urls
 $secrets=Read-DraftSecrets (Join-Path $run 'secrets.clixml')
 $composeEnvironment=Set-DraftComposeEnvironment $secrets $RunId ([uri]$state.urls.Bff).Port $state.infrastructure
 foreach($entry in $composeEnvironment.GetEnumerator()){$old[$entry.Key]=$entry.Value}
 if($env:DOCKER_HOST -or (& docker context inspect --format '{{.Endpoints.docker.Host}}') -notmatch '^npipe://'){throw 'Local Docker Desktop required.'}
 $compose=Get-DraftComposeArguments $root $RunId
 $containers=@(& docker @compose ps -q)
 if($LASTEXITCODE -ne 0 -or $containers.Count -ne $(if($state.Contains('mediaEnabled') -and $state.mediaEnabled){7}else{5})){throw 'All five owned draft containers must be running.'}
 foreach($id in $containers) {
  if((& docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' $id) -ne $state.project){throw 'Draft container ownership mismatch.'}
 }
 if($LASTEXITCODE -ne 0){throw 'Draft resource lookup failed.'}
 foreach($entry in $state.processes.GetEnumerator()){
  if(-not (Assert-DraftProcess $entry.Value (Get-Process -Id $entry.Value.id -ErrorAction SilentlyContinue))){throw 'Draft process is no longer running.'}
 }
 $referencePath=Join-Path $run 'reference.json'
 $completed=(Test-Path $referencePath) -and (Get-Content $referencePath -Raw | ConvertFrom-Json).completed
 if(-not $completed) {
  $admin=Invoke-RestMethod "http://127.0.0.1:$($state.infrastructure.keycloak)/realms/master/protocol/openid-connect/token" -Method Post -Body @{grant_type='password';client_id='admin-cli';username='acceptance-admin';password=$secrets.KEYCLOAK_ADMIN_PASSWORD}
  $headers=@{Authorization='Bearer '+$admin.access_token};$adminBase="http://127.0.0.1:$($state.infrastructure.keycloak)/admin/realms/$($state.realm)"
  foreach($role in @('cashier','kitchen-operator','store-manager','accountant')) {
   $key=switch($role){'cashier'{'READER_PASSWORD'} 'kitchen-operator'{'SECONDMANAGER_PASSWORD'} 'store-manager'{'RESOLVER_PASSWORD'} 'accountant'{'ACCOUNTANT_PASSWORD'}}
   $username='draft-'+$role
   $users=@(Invoke-RestMethod "$adminBase/users?username=$username&exact=true" -Headers $headers | ForEach-Object {$_})
   if($users.Count -eq 0){
    $user=@{username=$username;enabled=$true;emailVerified=$true;firstName='Draft';lastName=$role;email="$username@nexa.invalid";credentials=@(@{type='password';value=$secrets[$key];temporary=$false})}
    Invoke-RestMethod "$adminBase/users" -Method Post -Headers $headers -ContentType 'application/json' -Body ($user|ConvertTo-Json -Depth 5) | Out-Null
    $users=@(Invoke-RestMethod "$adminBase/users?username=$username&exact=true" -Headers $headers | ForEach-Object {$_})
   }
   if($users.Count -ne 1){throw 'Draft user identity mismatch.'}
   $realmRole=Invoke-RestMethod "$adminBase/roles/customer-user" -Headers $headers
   Invoke-RestMethod "$adminBase/users/$($users[0].id)/role-mappings/realm" -Method Post -Headers $headers -ContentType 'application/json' -Body (ConvertTo-Json -InputObject @($realmRole)) | Out-Null
   $name='NEXACONNECT_DRAFT_SUBJECT_'+$role;$old[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$users[0].id,'Process')
  }
 }
 foreach($suffix in @('platform','restaurant','authorization','catalog','inventory','pos')) {
  $builder=[System.Data.Common.DbConnectionStringBuilder]::new()
  $builder['Host']='127.0.0.1';$builder['Port']=$state.infrastructure.postgres;$builder['Database']="nexa_review_it_${RunId}_$suffix"
  $builder['Username']=if($suffix -eq 'platform'){'platform_directory_app'}else{"nexaconnect_${suffix}_app"};$builder['Password']=$secrets.RUNTIME_PASSWORD
  $name='NEXACONNECT_DRAFT_DB_'+$suffix;$old[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$builder.ConnectionString,'Process')
 }
 foreach($entry in @{DOTNET_ENVIRONMENT='Development';NEXACONNECT_DRAFT_RUN=$RunId;NEXACONNECT_DRAFT_REFERENCE=$referencePath}.GetEnumerator()){
  $old[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key,'Process');[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')
 }
 $artifact=Join-Path $run 'provisioner'
 if(-not $NoBuild){& dotnet build (Join-Path $root 'src/Tools/NexaConnect.FirstDraftProvisioning') -c Release --output $artifact --verbosity quiet -m:1;if($LASTEXITCODE -ne 0){throw 'Draft provisioner build failed.'}}
 & dotnet (Join-Path $artifact 'NexaConnect.FirstDraftProvisioning.dll') --confirm
 if($LASTEXITCODE -ne 0){throw 'Draft reference provisioning failed; inspect protected journal.'}
 if($state.Contains('mediaEnabled') -and $state.mediaEnabled){& dotnet (Join-Path $artifact 'NexaConnect.FirstDraftProvisioning.dll') --media-access;if($LASTEXITCODE -ne 0){throw 'Draft Media permission provisioning failed.'}}
 $reference=Get-Content $referencePath -Raw | ConvertFrom-Json
 if(-not $reference.completed){throw 'Draft reference incomplete.'}
 $pos=Get-Content (Join-Path $run 'pos-configuration.json') -Raw | ConvertFrom-Json
 foreach($name in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){$pos.Pos.$name=[string]$reference.$name}
 foreach($path in @('pos-configuration.json','pos/appsettings.json')){$pos|ConvertTo-Json -Depth 8|Set-Content (Join-Path $run $path)}
 $state.provisionedBranch=$true;$state|ConvertTo-Json -Depth 8|Set-Content (Join-Path $run 'state.json')
 Write-Output "Draft branch provisioned. RunId=$RunId Portal=$($state.urls.Bff)"
 Write-Output 'Use scripts/get-first-draft-login.ps1 locally for each account. No financial transactions were seeded.'
} finally {Restore-DraftEnvironment $old;$lock.Dispose()}
