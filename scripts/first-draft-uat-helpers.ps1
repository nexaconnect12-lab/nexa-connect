# Local operator records are attestations, never measured application results or signatures.
Set-StrictMode -Version Latest
function ConvertFrom-UatJson([string]$Json) {
    # 7.5+ otherwise converts ISO strings into DateTime, losing their original offset.
    if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){return ConvertFrom-Json -InputObject $Json -AsHashtable -DateKind String}
    return ConvertFrom-Json -InputObject $Json -AsHashtable
}
function Get-UatCases {
    @('setup','cashier-session','cash-shift','cash-checkout','promptpay-checkout','kitchen',
      'cancel-conflict','receipt-pdf','print-recovery','restart-recovery','cash-variance',
      'accountant-denial','tenant-denial','next-day','ledger-csv','clean-ci')
}
function Assert-UatId([string]$Value) {
    if($Value -cnotmatch '^[a-f0-9]{32}$'){throw 'Use a generated lowercase UUID reference.'}
}
function Assert-UatTime([string]$Value) {
    $time=[DateTimeOffset]::ParseExact($Value,'O',[Globalization.CultureInfo]::InvariantCulture)
    if($time.Offset -ne [TimeSpan]::Zero -or $time -gt [DateTimeOffset]::UtcNow){throw 'Observation must use a past or current UTC timestamp in round-trip format.'}
    return $time
}
function Assert-UatNextDay([string]$BusinessDate,[string]$StartedAtUtc,[string]$ObservedAtUtc) {
    $day=[DateTime]::ParseExact($BusinessDate,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $start=Assert-UatTime $StartedAtUtc;$end=Assert-UatTime $ObservedAtUtc
    # Singapore has a fixed UTC+08:00 business clock.
    if($start -ge $end -or $start.ToOffset([TimeSpan]::FromHours(8)).Date -ne $day.Date -or
       $end.ToOffset([TimeSpan]::FromHours(8)).Date -le $day.Date){throw 'Next-day attestation must span the selected actual Singapore business date and a later day.'}
}
function Assert-UatRecord($Record) {
    if($Record.format -cne 1){throw 'Unsupported UAT format.'}
    Assert-UatId $Record.id
    if($Record.candidate.revision -cnotmatch '^[a-f0-9]{40}$' -or $Record.candidate.inputHash -cnotmatch '^[a-f0-9]{64}$' -or $Record.candidate.dirty -isnot [bool]){throw 'Invalid candidate identity.'}
    if($Record.binding){
        foreach($key in @('runId','terminalReleaseId','releaseRunId')){Assert-UatId $Record.binding[$key]}
        foreach($key in @('packageManifestHash','releaseReceiptHash')){if($Record.binding[$key] -cnotmatch '^[A-Fa-f0-9]{64}$'){throw 'Invalid binding hash.'}}
    }
    $catalog=@(Get-UatCases)
    if($Record.cases.Count -ne $catalog.Count){throw 'Incomplete UAT catalog.'}
    foreach($id in $catalog){
        if(-not $Record.cases.Contains($id)){throw 'Missing UAT case.'}
        $case=$Record.cases[$id]
        if($case.status -cnotin @('pending','passed','failed','blocked')){throw 'Invalid case status.'}
        if($case.status -ne 'pending'){
            Assert-UatId $case.evidenceId
            [void](Assert-UatTime $case.observedAtUtc)
            if($case.kind -cne 'operator-attested'){throw 'Operator results cannot be labeled measured automation.'}
        }
        if($case.status -eq 'passed'){
            if(-not $Record.binding){throw 'Passed case requires a bound candidate/package.'}
            if($id -eq 'next-day'){Assert-UatNextDay $case.businessDate $case.startedAtUtc $case.observedAtUtc}
            if($id -eq 'clean-ci' -and $Record.candidate.dirty){throw 'Clean CI cannot attest a dirty candidate.'}
        }
    }
    foreach($defect in $Record.defects){
        Assert-UatId $defect.id
        if($defect.caseId -cnotin $catalog -or $defect.category -cnotin @('financial','security','data-loss','unrecoverable','usability') -or
           $defect.severity -cnotin @('blocker','high','minor') -or $defect.status -cnotin @('open','resolved')){throw 'Invalid defect.'}
        [void](Assert-UatTime $defect.openedAtUtc)
        if($defect.status -eq 'resolved'){
            if((Assert-UatTime $defect.resolvedAtUtc) -lt (Assert-UatTime $defect.openedAtUtc)){throw 'Resolution precedes defect observation.'}
        }
    }
    if(@($Record.defects|ForEach-Object {$_.id}|Sort-Object -Unique).Count -ne $Record.defects.Count){throw 'Duplicate defect identity.'}
}
function Get-UatBlockers($Record) {
    Assert-UatRecord $Record
    if(-not $Record.binding){'candidate-package-not-bound'}
    if($Record.candidate.dirty){'clean-candidate-required'}
    foreach($id in (Get-UatCases)){if($Record.cases[$id].status -cne 'passed'){"case:$id"}}
    foreach($defect in $Record.defects){
        $blocking=$defect.category -ne 'usability' -or $defect.severity -ne 'minor'
        if(-not $blocking){continue}
        if($defect.status -eq 'open'){"defect:$($defect.id)";continue}
        $case=$Record.cases[$defect.caseId]
        if($case.status -ne 'passed' -or (Assert-UatTime $case.observedAtUtc) -le (Assert-UatTime $defect.resolvedAtUtc)){"retest:$($defect.id)"}
    }
}
function Assert-UatCandidate($Expected,$Actual) {
    if($Actual.dirty -isnot [bool] -or $Expected.revision -cne $Actual.revision -or $Expected.inputHash -cne $Actual.inputHash -or $Expected.dirty -cne $Actual.dirty){throw 'Candidate changed; create a new UAT session and retest.'}
}
function Assert-UatBindingEvidence($State,$Package,$Configuration,$Receipt,$Candidate,[string]$Draft,[string]$Terminal,[string]$Release) {
    if($State.runId -cne $Draft -or $State.project -cne "nexa-draft-$Draft" -or $State.realm -cne "nexa-review-it-$Draft" -or
       $State.status -cne 'Ready' -or $State.provisionedBranch -isnot [bool] -or -not $State.provisionedBranch){throw 'Bind requires an owned provisioned Ready draft; Reset test runs cannot be handed over.'}
    if($Package.runId -cne $Draft -or $Package.releaseId -cne $Terminal){throw 'Package ownership mismatch.'}
    Assert-UatCandidate $Candidate @{revision=$Package.sourceRevision;dirty=$Package.sourceDirty;inputHash=$Package.sourceInputHash}
    Assert-TerminalScopeEqual $Package.scope (Get-TerminalScope $Configuration)
    if($Receipt.runId -cne $Release -or $Receipt.passed -isnot [bool] -or -not $Receipt.passed){throw 'A successful local release receipt is required.'}
    Assert-UatCandidate $Candidate @{revision=$Receipt.sourceRevision;dirty=$Receipt.sourceDirty;inputHash=$Receipt.candidateInputHash}
}
function Write-UatRecord([string]$Directory,$Record) {
    Assert-UatRecord $Record
    $temporary=Join-Path $Directory 'record.pending.json'
    $Record|ConvertTo-Json -Depth 20|Set-Content -LiteralPath $temporary
    [IO.File]::Move($temporary,(Join-Path $Directory 'record.json'),$true)
}
function Get-UatReport($Record,[bool]$BindingValid) {
    $blockers=@(Get-UatBlockers $Record)
    if(-not $BindingValid){$blockers+='candidate-or-binding-invalid'}
    # Allowlist exported fields. Never read private notes or arbitrary attachments.
    return @{uatId=$Record.id;revision=$Record.candidate.revision;inputHash=$Record.candidate.inputHash;
        readyForUserDecision=($blockers.Count -eq 0);acceptance='pending-user-decision';blockers=$blockers;
        binding=if($Record.binding){@{runId=$Record.binding.runId;terminalReleaseId=$Record.binding.terminalReleaseId;releaseRunId=$Record.binding.releaseRunId}}else{$null};
        cases=@(foreach($id in (Get-UatCases)){
            $item=@{id=$id;status=$Record.cases[$id].status;kind='operator-attested'}
            if($Record.cases[$id].status -ne 'pending'){$item.evidenceId=$Record.cases[$id].evidenceId;$item.observedAtUtc=$Record.cases[$id].observedAtUtc};$item
        });
        defects=@($Record.defects|ForEach-Object {@{id=$_.id;caseId=$_.caseId;category=$_.category;severity=$_.severity;status=$_.status}});
        minorBacklog=@($Record.defects|Where-Object {$_.status -eq 'open' -and $_.category -eq 'usability' -and $_.severity -eq 'minor'}|ForEach-Object {@{id=$_.id;caseId=$_.caseId}})}
}
