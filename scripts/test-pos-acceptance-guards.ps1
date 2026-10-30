#requires -Version 7.4
$ErrorActionPreference='Stop'
. "$PSScriptRoot/first-draft-uat-helpers.ps1"
. "$PSScriptRoot/pos-acceptance-helpers.ps1"
$script:count=0
function Require([bool]$Value){if(-not $Value){throw 'POS acceptance guard failed.'};$script:count++}
function Reject([scriptblock]$Work){try{& $Work|Out-Null}catch{$script:count++;return};throw 'Unsafe POS acceptance record accepted.'}
function Fresh {
    $binding=@{runId='a'*32;releaseId='b'*32;pilotId='c'*32}
    foreach($key in @('activeManifestHash','packageManifestHash','configurationHash','backendArtifacts','qrActivationHash','profileHash')){$binding[$key]='d'*64}
    return @{version=1;id='e'*32;kind='installed-pos-operator-observations';binding=$binding;events=@()}
}
function Observation([string]$Case,[string]$Status='passed',[bool]$Valid=$true){
    @{type='observe';caseId=$Case;status=$Status;bindingValid=$Valid;evidenceId=[guid]::NewGuid().ToString('N');observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
}
$record=Fresh
$report=Get-PosAcceptanceReport $record $true $false
Require ($report.cases.Count -eq 17 -and @($report.cases|Where-Object status -CNE 'pending').Count -eq 0)
Require (-not $report.operatorCasesComplete -and -not $report.releaseReady -and -not $report.productionVerified -and -not $report.currentSourceVerified)
Reject {Add-PosAcceptanceEvent $record (Observation cashier-signin passed $false)}
Require ($record.events.Count -eq 0)
foreach($status in @('failed','blocked')){$result=Add-PosAcceptanceEvent (Fresh) (Observation cashier-signin $status $false);Require ($result.events.Count -eq 1)}
foreach($case in (Get-PosAcceptanceCases)){$record=Add-PosAcceptanceEvent $record (Observation $case)}
$report=Get-PosAcceptanceReport $record $true $false
Require ($report.operatorCasesComplete -and -not $report.qrCandidateFresh -and -not $report.currentSourceVerified -and -not $report.releaseReady)
Require (-not (Get-PosAcceptanceReport $record $false $true).operatorCasesComplete)
$defect=@{type='defect';caseId='cash-original-settlement';category='financial';severity='high';defectId='f'*32;evidenceId='1'*32;observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
$record=Add-PosAcceptanceEvent $record $defect
Require (-not (Get-PosAcceptanceReport $record $true $true).operatorCasesComplete)
Require ((Get-PosAcceptanceReport $record $true $true).cases.Where({$_.id -ceq 'cash-original-settlement'})[0].status -ceq 'failed')
Reject {Add-PosAcceptanceEvent $record (Observation cash-original-settlement)}
$resolve=@{type='resolve';caseId='cash-original-settlement';defectId='f'*32;evidenceId='2'*32;observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
$record=Add-PosAcceptanceEvent $record $resolve
Require ((Get-PosAcceptanceReport $record $true $true).cases.Where({$_.id -ceq 'cash-original-settlement'})[0].status -ceq 'pending')
Require (-not (Get-PosAcceptanceReport $record $true $true).operatorCasesComplete)
Reject {Add-PosAcceptanceEvent $record $resolve}
$record=Add-PosAcceptanceEvent $record (Observation cash-original-settlement)
Require ((Get-PosAcceptanceReport $record $true $true).operatorCasesComplete)
Require ($record.events.Count -eq 20)
foreach($key in @('runId','releaseId','pilotId','activeManifestHash','packageManifestHash','configurationHash','backendArtifacts','qrActivationHash','profileHash')){
    $other=Fresh;$other.binding[$key]='0'*64
    Reject {Assert-PosAcceptanceSnapshotEqual (Fresh).binding $other.binding}
}
foreach($mutation in @(
    {param($e)$e.caseId='not-a-case'}, {param($e)$e.status='skipped'},
    {param($e)$e.evidenceId='private receipt'}, {param($e)$e.bindingValid='true'},
    {param($e)$e.observedAtUtc=[DateTimeOffset]::UtcNow.AddDays(1).ToString('O')}
)){$event=Observation cashier-signin;& $mutation $event;Reject {Add-PosAcceptanceEvent (Fresh) $event}}
$bad=Fresh;$bad.events=@((Observation cashier-signin));$bad.events[0].sequence=2;Reject {Assert-PosAcceptanceRecord $bad}
$bad=Fresh;$bad.events=@(1..257|ForEach-Object {Observation cashier-signin});Reject {Assert-PosAcceptanceRecord $bad}
$bad=Fresh;$bad.binding.backendArtifacts='invalid';Reject {Assert-PosAcceptanceRecord $bad}
$record.privateNotes='secret-fixture';$record.binding.endpoint='secret-fixture';$record.events[0].raw='secret-fixture'
$json=Get-PosAcceptanceReport $record $true $true|ConvertTo-Json -Depth 20
Require (-not $json.Contains('secret-fixture') -and -not $json.Contains('installDirectory'))
Write-Output "PASS: $script:count POS acceptance guards; synthetic observations are not operator acceptance."
