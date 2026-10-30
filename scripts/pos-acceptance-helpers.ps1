# Installed-release operator observations. No authentication, device or financial commands.
Set-StrictMode -Version Latest
function Get-PosAcceptanceCases {
    @('cashier-signin','idle-lock-resume','session-expiry-fallback','cash-original-settlement',
      'promptpay-original-settlement','omise-sandbox-original','qr-cash-collection',
      'qr-promptpay-collection','restart-pending-payment','network-loss-original-retry',
      'receipt-pdf-reprint','printer-failure-recovery','barcode-input','dual-display-isolation',
      'locked-gallery','drawer-history-privacy','permission-scope-denial')
}
function Get-PosAcceptanceHash($Value) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes(($Value|ConvertTo-Json -Depth 20 -Compress)))).ToLowerInvariant()
}
function Assert-PosAcceptanceRecord($Record) {
    if($Record.version -cne 1 -or $Record.kind -cne 'installed-pos-operator-observations'){throw 'Unsupported POS acceptance record.'}
    Assert-UatId $Record.id
    foreach($key in @('runId','releaseId','pilotId')){Assert-UatId $Record.binding[$key]}
    foreach($key in @('activeManifestHash','packageManifestHash','configurationHash','backendArtifacts','qrActivationHash','profileHash')){
        if($Record.binding[$key] -cnotmatch '^[a-f0-9]{64}$'){throw 'Invalid installed binding.'}
    }
    if($Record.events -isnot [array] -or $Record.events.Count -gt 256){throw 'POS observation limit exceeded.'}
    $defects=@{};$sequence=0
    foreach($event in $Record.events){
        $sequence++
        if($event.sequence -ne $sequence -or $event.caseId -cnotin (Get-PosAcceptanceCases) -or $event.type -cnotin @('observe','defect','resolve')){throw 'Invalid POS observation sequence.'}
        Assert-UatId $event.evidenceId
        [void](Assert-UatTime $event.observedAtUtc)
        if($event.type -ceq 'observe'){
            if($event.status -cnotin @('passed','failed','blocked') -or $event.bindingValid -isnot [bool]){throw 'Invalid POS observation.'}
            if($event.status -ceq 'passed' -and (-not $event.bindingValid -or @($defects.Values|Where-Object { $_.caseId -ceq $event.caseId -and $_.open }).Count)){throw 'Passing observation requires valid binding and resolved defects.'}
        }elseif($event.type -ceq 'defect'){
            Assert-UatId $event.defectId
            if($defects.ContainsKey($event.defectId) -or $event.category -cnotin @('financial','security','data-loss','unrecoverable','usability') -or $event.severity -cnotin @('blocker','high','minor')){throw 'Invalid POS defect.'}
            $defects[$event.defectId]=@{caseId=$event.caseId;open=$true}
        }else{
            Assert-UatId $event.defectId
            if(-not $defects.ContainsKey($event.defectId) -or -not $defects[$event.defectId].open -or $defects[$event.defectId].caseId -cne $event.caseId){throw 'Resolve requires the original open POS defect.'}
            $defects[$event.defectId].open=$false
        }
    }
}
function Add-PosAcceptanceEvent($Record,$Event) {
    # Validate a copy first: rejected observations must not mutate the caller's record.
    $copy=ConvertFrom-UatJson ($Record|ConvertTo-Json -Depth 20)
    $eventCopy=ConvertFrom-UatJson ($Event|ConvertTo-Json -Depth 20)
    $eventCopy.sequence=$Record.events.Count+1
    $copy.events=@($copy.events)+@($eventCopy)
    Assert-PosAcceptanceRecord $copy
    return $copy
}
function Get-PosAcceptanceReport($Record,[bool]$BindingValid,[bool]$QrCandidateFresh) {
    Assert-PosAcceptanceRecord $Record
    $cases=[ordered]@{};foreach($id in (Get-PosAcceptanceCases)){$cases[$id]=@{id=$id;status='pending';kind='operator-attested'}}
    $defects=[ordered]@{}
    foreach($event in $Record.events){
        if($event.type -ceq 'observe'){
            $cases[$event.caseId]=@{id=$event.caseId;status=$event.status;kind='operator-attested';evidenceId=$event.evidenceId;observedAtUtc=$event.observedAtUtc}
        }elseif($event.type -ceq 'defect'){
            $defects[$event.defectId]=@{id=$event.defectId;caseId=$event.caseId;category=$event.category;severity=$event.severity;status='open'}
            $cases[$event.caseId]=@{id=$event.caseId;status='failed';kind='operator-attested'}
        }else{
            $defects[$event.defectId].status='resolved'
            $cases[$event.caseId]=@{id=$event.caseId;status='pending';kind='operator-attested';reason='retest-required'}
        }
    }
    $blockers=@($cases.Values|Where-Object status -CNE 'passed'|ForEach-Object {$_.id})
    if(-not $BindingValid){$blockers+='installed-binding-invalid'}
    if(@($defects.Values|Where-Object status -CEQ 'open').Count){$blockers+='open-defects'}
    # Explicit allowlist: neither private binding paths/endpoints nor arbitrary record fields are exported.
    return @{version=1;acceptanceId=$Record.id;kind='operator-attestations';bindingValid=$BindingValid;
        target=@{runId=$Record.binding.runId;releaseId=$Record.binding.releaseId;pilotId=$Record.binding.pilotId};
        cases=@($cases.Values);defects=@($defects.Values);blockers=$blockers;operatorCasesComplete=($blockers.Count -eq 0);
        qrCandidateFresh=$QrCandidateFresh;currentSourceVerified=$false;releaseReady=$false;productionVerified=$false}
}
function Write-PosAcceptanceRecord([string]$Directory,$Record) {
    Assert-PosAcceptanceRecord $Record
    $temporary=Assert-TerminalPath (Join-Path $Directory 'record.pending.json')
    $target=Assert-TerminalPath (Join-Path $Directory 'record.json')
    $Record|ConvertTo-Json -Depth 20|Set-Content -LiteralPath $temporary
    [IO.File]::Move($temporary,$target,$true)
}
function Assert-PosAcceptanceSnapshotEqual($Expected,$Actual) {
    foreach($key in @('runId','releaseId','pilotId','activeManifestHash','packageManifestHash','configurationHash','backendArtifacts','qrActivationHash','profileHash')){
        if($Expected[$key] -cne $Actual[$key]){throw 'Installed POS acceptance target changed.'}
    }
}
