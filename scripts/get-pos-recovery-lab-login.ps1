#requires -Version 7.4
param([Parameter(Mandatory)][string]$LabId,[Parameter(Mandatory)][ValidateSet('cashier','cashier-other','manager','restricted','wrong-branch')][string]$Account)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-uat-helpers.ps1"
. "$PSScriptRoot/pos-recovery-lab-helpers.ps1"
$root=Split-Path $PSScriptRoot -Parent
$lab=Read-PosRecoveryLab $root $LabId
if($lab.phase -cne 'Ready'){throw 'Credentials require a prepared live lab.'}
$secrets=Read-DraftSecrets "$root/.runstate/first-draft/$LabId/secrets.clixml"
$entry=switch($Account){
 cashier {@('draft-cashier','READER_PASSWORD')}
 cashier-other {@('lab-cashier-other','LAB_CASHIER_OTHER_PASSWORD')}
 manager {@('draft-store-manager','RESOLVER_PASSWORD')}
 restricted {@('draft-accountant','ACCOUNTANT_PASSWORD')}
 wrong-branch {@('lab-wrong-branch','LAB_WRONG_BRANCH_PASSWORD')}
}
# Explicit local retrieval only. Never invoke in CI, chat tool output, transcripts or shared logs.
[pscustomobject]@{username=$entry[0];password=$secrets[$entry[1]]}
