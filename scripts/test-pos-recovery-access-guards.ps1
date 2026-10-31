#requires -Version 7.4
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/pos-recovery-access-helpers.ps1"
$count=0
function Reject([scriptblock]$Work){try{& $Work}catch{$script:count++;return};throw 'Invalid UX20 producer evidence accepted.'}
function Regression {
    @{kind='automated-pos-regression';passed=$true;operatorAcceptance=$false;retainedPilotMutated=$false;providerRequests=$false;releaseReady=$false;productionVerified=$false;
        checks=@{guards=@{passed=$true};unit=@{passed=$true;count=1390};http=@{passed=$true;count=49};'windows-input'=@{passed=$true};'windows-print'=@{passed=$true}}}
}
Assert-PosRecoveryProducer regression (Regression);$count++
foreach($key in @('passed','operatorAcceptance','retainedPilotMutated','providerRequests','releaseReady','productionVerified')){
    $receipt=Regression;$receipt[$key]=-not $receipt[$key];Reject {Assert-PosRecoveryProducer regression $receipt}
}
foreach($key in @('guards','unit','http','windows-input','windows-print')){
    $receipt=Regression;$receipt.checks[$key].passed=$false;Reject {Assert-PosRecoveryProducer regression $receipt}
}
$receipt=Regression;$receipt.checks.unit.count=0;Reject {Assert-PosRecoveryProducer regression $receipt}
$receipt=Regression;$receipt.checks.http.count=48;Reject {Assert-PosRecoveryProducer regression $receipt}
$cashier=@{automatedBackendPassed=$true;verifierContractPassed=$true;cleanupVerified=$true;productionVerified=$false;protectedPosCases=21;orderReceiptAuthorizationAndMigrationCases=4}
Assert-PosRecoveryProducer cashier $cashier;$count++
foreach($key in @('automatedBackendPassed','verifierContractPassed','cleanupVerified')){
    $receipt=$cashier.Clone();$receipt[$key]=$false;Reject {Assert-PosRecoveryProducer cashier $receipt}
}
$receipt=$cashier.Clone();$receipt.productionVerified=$true;Reject {Assert-PosRecoveryProducer cashier $receipt}
$receipt=$cashier.Clone();$receipt.protectedPosCases=0;Reject {Assert-PosRecoveryProducer cashier $receipt}
$receipt=$cashier.Clone();$receipt.protectedPosCases=20;Reject {Assert-PosRecoveryProducer cashier $receipt}
$receipt=$cashier.Clone();$receipt.orderReceiptAuthorizationAndMigrationCases=3;Reject {Assert-PosRecoveryProducer cashier $receipt}
$workflow=@{matrixPassed=$true;cleanupPassed=$true;orderProcessInterruptions=2}
Assert-PosRecoveryProducer workflow $workflow;$count++
foreach($key in @('matrixPassed','cleanupPassed')){$receipt=$workflow.Clone();$receipt[$key]=$false;Reject {Assert-PosRecoveryProducer workflow $receipt}}
$receipt=$workflow.Clone();$receipt.orderProcessInterruptions=0;Reject {Assert-PosRecoveryProducer workflow $receipt}
Reject {Assert-PosRecoveryProducer other @{}}
Reject {Assert-PosRecoveryProducer regression @{}}
& pwsh -NoProfile -File "$PSScriptRoot/test-pos-recovery-access.ps1" *> $null
if($LASTEXITCODE -eq 0){throw 'Disposable infrastructure must require opt-in.'};$count++
Write-Output "PASS: $count UX20 producer and opt-in guards. No infrastructure or operator observations."
