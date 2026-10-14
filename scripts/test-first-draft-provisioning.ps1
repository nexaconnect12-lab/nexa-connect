#requires -Version 7.4
param([string]$RunId,[switch]$ConfirmTestResources)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
if(-not $ConfirmTestResources){throw 'Verification mutates and resets only the owned draft; explicit test-resource confirmation required.'}
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if(-not $RunId){$RunId=[Guid]::NewGuid().ToString('N');& (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Start -RunId $RunId -ConfirmTestResources}
$run=Get-DraftRunDirectory $root $RunId
$old=@{};$passed=$false;$cleanup=$false
function Browser-Verify([string]$Mode){
 [Environment]::SetEnvironmentVariable('NEXACONNECT_DRAFT_VERIFY_MODE',$Mode,'Process')
 Push-Location (Join-Path $root 'src/Frontend')
 try { & node e2e/first-draft/provisioning.mjs;if($LASTEXITCODE -ne 0){throw 'Live draft provisioning access verification failed.'} }finally{Pop-Location}
}
try {
 & (Join-Path $PSScriptRoot 'provision-first-draft.ps1') -RunId $RunId -ConfirmTestResources
 $state=Get-Content (Join-Path $run 'state.json') -Raw|ConvertFrom-Json -AsHashtable
 $secrets=Read-DraftSecrets (Join-Path $run 'secrets.clixml')
 $settings=@{NEXACONNECT_DRAFT_RUN=$RunId;NEXACONNECT_DRAFT_STATE=(Join-Path $run 'state.json');NEXACONNECT_DRAFT_REFERENCE=(Join-Path $run 'reference.json');NEXACONNECT_DRAFT_VERIFY_MODE='initial';NEXACONNECT_DRAFT_CASHIER_PASSWORD=$secrets.READER_PASSWORD;NEXACONNECT_DRAFT_KITCHEN_PASSWORD=$secrets.SECONDMANAGER_PASSWORD;NEXACONNECT_DRAFT_MANAGER_PASSWORD=$secrets.RESOLVER_PASSWORD;NEXACONNECT_DRAFT_ACCOUNTANT_PASSWORD=$secrets.ACCOUNTANT_PASSWORD;PGPASSWORD=$secrets.RUNTIME_PASSWORD}
 foreach($entry in $settings.GetEnumerator()){$old[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key,'Process');[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
 Browser-Verify 'initial'
 $before=(Get-FileHash (Join-Path $run 'reference.json')).Hash
 & (Join-Path $PSScriptRoot 'provision-first-draft.ps1') -RunId $RunId -ConfirmTestResources -NoBuild
 if((Get-FileHash (Join-Path $run 'reference.json')).Hash -ne $before){throw 'Completed provisioning changed reference identities.'}
 Browser-Verify 'repeat'
 # Owning Authorization test fixture: psql variables quote the subject as a data value. No cross-service mutation.
 $reference=Get-Content (Join-Path $run 'reference.json') -Raw|ConvertFrom-Json
 $previous=Set-DraftComposeEnvironment $secrets $RunId ([uri]$state.urls.Bff).Port
 try {
  $compose=Get-DraftComposeArguments $root $RunId
  "UPDATE authorization_role_assignments SET status='revoked' WHERE subject_id=:'subject'; UPDATE authorization_user_permission_overrides SET status='revoked' WHERE subject_id=:'subject';" |
    & docker @compose exec -T -e PGPASSWORD postgres psql -h 127.0.0.1 -U nexaconnect_authorization_app -d "nexa_review_it_${RunId}_authorization" --set=ON_ERROR_STOP=1 "--set=subject=$($reference.subjects.'kitchen-operator')" | Out-Null
  if($LASTEXITCODE -ne 0){throw 'Authorization revocation fixture failed.'}
 }finally{Restore-DraftEnvironment $previous}
 & (Join-Path $PSScriptRoot 'provision-first-draft.ps1') -RunId $RunId -ConfirmTestResources -NoBuild
 Browser-Verify 'revoked'
 $passed=$true
} finally {
 Restore-DraftEnvironment $old
 if(Test-Path (Join-Path $run 'state.json')){
  & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Reset -RunId $RunId -ConfirmTestResources
  $cleanup=(Get-Content (Join-Path $run 'state.json') -Raw|ConvertFrom-Json).status -eq 'Reset'
  @{runId=$RunId;passed=($passed -and $cleanup);cleanupVerified=$cleanup;realOidcAndPkce=$passed;roleAndTenantBoundaries=$passed;reProvisioningPreservesStockMenuAndRevocation=$passed;sourceRevision=(& git -C $root rev-parse HEAD);sourceDirty=[bool](& git -C $root status --porcelain);joinedCheckoutVerified=$false}|ConvertTo-Json|Set-Content (Join-Path $run 'provisioning-verification.json')
 }
 if(-not $cleanup){throw 'Owned draft cleanup was not verified.'}
}
Write-Output "Draft provisioning verification passed: $run/provisioning-verification.json"
