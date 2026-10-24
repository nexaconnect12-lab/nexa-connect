#requires -Version 7.4
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm the two isolated local manager acceptance environments and their exact cleanup.'}
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-manager-helpers.ps1')
$runId=[Guid]::NewGuid().ToString('N');$run=Join-Path $root ".runstate/first-draft-manager/$runId"
New-Item -ItemType Directory -Path $run | Out-Null
Protect-DraftDirectory $run
$passed=$false;$stages=[ordered]@{};$revision=(& git -C $root rev-parse HEAD).Trim()
try{
    Write-Output 'Verifying normal-clock checkout, mixed tenders and manager cash reconciliation.'
    $output=@(& (Join-Path $PSScriptRoot 'test-first-draft-checkout.ps1') -ConfirmDisposableInfrastructure -VerifyManagerFlow)
    $stages.normal=Read-DraftManagerEvidence $root $output 'draft-manager-normal'
    Write-Output 'Verifying guarded historical Singapore day close, settlement and CSV provenance.'
    $output=@(& (Join-Path $PSScriptRoot 'test-financial-completeness-portal.ps1') -CashierDayCutoff -BusinessTimeZone Asia/Singapore -ConfirmDisposableInfrastructure)
    $stages.historical=Read-DraftManagerEvidence $root $output 'draft-manager-historical'
    foreach($stage in $stages.Values){if($stage.sourceRevision -cne $revision){throw 'Source revision changed between manager acceptance stages.'}}
    $passed=$true
}finally{
    @{runId=$runId;passed=$passed;stages=$stages;sourceRevision=$revision;sourceDirty=[bool](& git -C $root status --porcelain);realNextDayStaffDrillVerified=$false;windowsPrintingVerified=$false;externalTransfersVerified=$false}|
        ConvertTo-Json -Depth 7|Set-Content (Join-Path $run 'verification.json')
}
Write-Output "First-draft manager verification passed: $run/verification.json"
