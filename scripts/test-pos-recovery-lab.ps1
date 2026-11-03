#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$LabId,[switch]$ConfirmTestResources)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
if(-not $ConfirmTestResources){throw 'Confirm owned lab browser sign-ins, cashier-session termination and controlled Order interruption.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-release-helpers.ps1"
. "$PSScriptRoot/first-draft-uat-helpers.ps1"
. "$PSScriptRoot/pos-recovery-lab-helpers.ps1"
$lab=Read-PosRecoveryLab $root $LabId
$directory=Assert-TerminalPath "$root/.runstate/pos-recovery-lab/$LabId"
$run=Assert-TerminalPath (Get-DraftRunDirectory $root $LabId)
$target=ConvertFrom-UatJson (Get-Content -LiteralPath "$directory/target.json" -Raw)
$exe=Assert-TerminalPath "$run/terminal-packages/$($target.releaseId)/NexaConnect.POS.exe"
if(@(Get-Process -Name NexaConnect.POS -ErrorAction SilentlyContinue|Where-Object Path -CEQ $exe).Count){throw 'Close lab POS before automated identity/fault checks. Retained POS is not selected.'}
[void](Get-PosRecoveryLabBinding $root $LabId)
$source=Get-DraftCandidate $root;Assert-TerminalCandidateUnchanged $target.source $source
$id=[guid]::NewGuid().ToString('N');$test=Assert-TerminalPath "$directory/verification/$id"
New-Item -ItemType Directory -Path $test|Out-Null;Protect-DraftDirectory $test
$settings=@{};$previous=@{};$passed=$false;$cut=$false;$restored=$false
try{
 & pwsh -NoProfile -File "$PSScriptRoot/test-pos-recovery-lab-guards.ps1" *> "$test/guards.private.log"
 if($LASTEXITCODE){throw 'Lab guards failed.'}
 $state=ConvertFrom-UatJson (Get-Content -LiteralPath "$run/state.json" -Raw)
 $secrets=Read-DraftSecrets "$run/secrets.clixml"
 $admin=Invoke-RestMethod "http://127.0.0.1:$($state.infrastructure.keycloak)/realms/master/protocol/openid-connect/token" -Method Post -Body @{grant_type='password';client_id='admin-cli';username='acceptance-admin';password=$secrets.KEYCLOAK_ADMIN_PASSWORD}
 $settings=@{NEXACONNECT_LAB_ID=$LabId;NEXACONNECT_LAB_STATE="$run/state.json";NEXACONNECT_LAB_REFERENCE="$run/reference.json";NEXACONNECT_LAB_EVIDENCE="$test/oidc.json";NEXACONNECT_LAB_ADMIN_TOKEN=$admin.access_token;
  NEXACONNECT_LAB_PASSWORD_CASHIER=$secrets.READER_PASSWORD;NEXACONNECT_LAB_PASSWORD_OTHER=$secrets.LAB_CASHIER_OTHER_PASSWORD;NEXACONNECT_LAB_PASSWORD_MANAGER=$secrets.RESOLVER_PASSWORD;
  NEXACONNECT_LAB_PASSWORD_RESTRICTED=$secrets.ACCOUNTANT_PASSWORD;NEXACONNECT_LAB_PASSWORD_WRONG=$secrets.LAB_WRONG_BRANCH_PASSWORD}
 foreach($entry in $settings.GetEnumerator()){$previous[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key);[Environment]::SetEnvironmentVariable($entry.Key,[string]$entry.Value)}
 Push-Location "$root/src/Frontend"
 try{& node e2e/first-draft/recovery-lab.mjs *> "$test/browser.private.log";if($LASTEXITCODE){throw 'Real lab identity/access verification failed.'}}finally{Pop-Location;Restore-DraftEnvironment $previous;$previous=@{};$settings.Clear()}
 $oidc=ConvertFrom-UatJson (Get-Content -LiteralPath "$test/oidc.json" -Raw)
 if($oidc.passed -cne $true -or $oidc.checks -lt 24 -or $oidc.operatorAcceptance -cne $false){throw 'Incomplete lab OIDC evidence.'}
 $cut=$true
 & "$PSScriptRoot/pos-recovery-lab.ps1" -Action CutOrder -LabId $LabId -ConfirmTestResources *> "$test/cut.private.log"
 $unavailable=$false;try{[void](Get-PosRecoveryLabBinding $root $LabId)}catch{$unavailable=$true}
 if(-not $unavailable){throw 'Stopped lab dependency was still accepted by the binding.'}
 & "$PSScriptRoot/pos-recovery-lab.ps1" -Action RestoreOrder -LabId $LabId -ConfirmTestResources *> "$test/restore.private.log"
 $restored=$true;[void](Get-PosRecoveryLabBinding $root $LabId)
 Assert-TerminalCandidateUnchanged $source (Get-DraftCandidate $root)
 $passed=$true
}finally{
 Restore-DraftEnvironment $previous
 if($cut -and -not $restored){
  try{& "$PSScriptRoot/pos-recovery-lab.ps1" -Action RestoreOrder -LabId $LabId -ConfirmTestResources *> "$test/emergency-restore.private.log";$restored=$true}catch{$restored=$false}
 }
 @{version=1;kind='automated-real-oidc-recovery-lab';labId=$LabId;runId=$id;passed=$passed;source=$source;orderRestored=$restored;
  completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');retainedPilotMutated=$false;financialTransactionsSeeded=$false;
  nativePosObserved=$false;operatorAcceptance=$false;releaseReady=$false;productionVerified=$false}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$test/verification.json"
}
Write-Output "Real identity lab verification passed: $test/verification.json. Operator cases remain separate."
