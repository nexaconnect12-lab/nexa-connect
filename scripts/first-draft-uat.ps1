#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('New','Bind','Record','Defect','Resolve','Report')][string]$Action,
    [string]$UatId,[string]$RunId,[string]$TerminalReleaseId,[string]$ReleaseRunId,
    [ValidateSet('setup','cashier-session','cash-shift','cash-checkout','promptpay-checkout','kitchen','cancel-conflict','receipt-pdf','print-recovery','restart-recovery','cash-variance','accountant-denial','tenant-denial','next-day','ledger-csv','clean-ci')][string]$CaseId,
    [ValidateSet('pending','passed','failed','blocked')][string]$Status,
    [string]$EvidenceId,[string]$BusinessDate,[string]$StartedAtUtc,
    [ValidateSet('financial','security','data-loss','unrecoverable','usability')][string]$Category,
    [ValidateSet('blocker','high','minor')][string]$Severity,
    [string]$DefectId,[switch]$ConfirmObserved
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-release-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-uat-helpers.ps1"
function Read-Json([string]$Path){ConvertFrom-UatJson (Get-Content -LiteralPath (Assert-TerminalPath $Path) -Raw)}
function Get-Binding([string]$Draft,[string]$Terminal,[string]$Release,$Candidate){
    foreach($value in @($Draft,$Terminal,$Release)){Assert-UatId $value}
    $run=Get-DraftRunDirectory $root $Draft
    $draftLock=Open-DraftLifecycleLock (Assert-TerminalPath $run)
    try{
        $state=Read-Json "$run/state.json"
        $packagePath=Assert-TerminalPath "$run/terminal-packages/$Terminal"
        $package=Read-TerminalPackage $packagePath
        $configuration=Read-Json "$run/pos-configuration.json"
        $packagedConfiguration=Read-Json "$packagePath/appsettings.json"
        foreach($config in @($configuration,$packagedConfiguration)){
            if($config.Identity.Authority -cne $state.authority){throw 'Configured identity authority differs from the retained draft.'}
            foreach($pair in @(@('PosApi','POS'),@('OrderApi','Order'),@('CatalogApi','Catalog'))){
                if(([uri]$config.Services[$pair[0]]).AbsoluteUri -cne ([uri]$state.urls[$pair[1]]).AbsoluteUri){throw 'Configured service URL differs from the retained draft.'}
            }
        }
        $receiptPath=Assert-TerminalPath "$root/.runstate/first-draft-release/$Release/verification.json"
        $receipt=Read-Json $receiptPath
        Assert-UatBindingEvidence $state $package $configuration $receipt $Candidate $Draft $Terminal $Release
        return @{runId=$Draft;terminalReleaseId=$Terminal;releaseRunId=$Release;
            packageManifestHash=(Get-FileHash "$packagePath/terminal-package.json").Hash;
            releaseReceiptHash=(Get-FileHash $receiptPath).Hash}
    }finally{$draftLock.Dispose()}
}
if($Action -eq 'New'){
    $UatId=[Guid]::NewGuid().ToString('N')
    $directory=Assert-TerminalPath "$root/.runstate/first-draft-uat/$UatId"
    New-Item -ItemType Directory -Path $directory|Out-Null
    Protect-DraftDirectory $directory
    $candidate=Get-DraftCandidate $root
    $cases=[ordered]@{};foreach($id in (Get-UatCases)){$cases[$id]=@{status='pending'}}
    $record=@{format=1;id=$UatId;candidate=@{revision=$candidate.revision;dirty=$candidate.dirty;inputHash=$candidate.inputHash};binding=$null;cases=$cases;defects=@()}
    Write-UatRecord $directory $record
    'Keep evidence indexed by UUID here. Never share credentials, private recovery, raw logs, PDF/CSV or unrestricted personal/payment data.'|Set-Content "$directory/private-notes.md"
    Write-Output "UAT session: $UatId"
    return
}
Assert-UatId $UatId
$directory=Assert-TerminalPath "$root/.runstate/first-draft-uat/$UatId"
$lock=Open-DraftLifecycleLock $directory
try{
    $record=Read-Json "$directory/record.json"
    Assert-UatRecord $record
    if($record.id -cne $UatId){throw 'UAT ownership mismatch.'}
    $bindingProblem=$null
    try{
        Assert-UatCandidate $record.candidate (Get-DraftCandidate $root)
        if($record.binding){
            $current=Get-Binding $record.binding.runId $record.binding.terminalReleaseId $record.binding.releaseRunId $record.candidate
            Assert-TerminalScopeEqual $record.binding $current
        }
    }catch{if($Action -ne 'Report'){throw};$bindingProblem='candidate-or-binding-invalid'}
    if($Action -eq 'Bind'){
        if($record.binding){throw 'Binding is immutable; create a new session for another build.'}
        $record.binding=Get-Binding $RunId $TerminalReleaseId $ReleaseRunId $record.candidate
    }elseif($Action -eq 'Record'){
        if(-not $CaseId -or -not $Status){throw 'CaseId and Status are required.'}
        if($Status -eq 'pending'){$record.cases[$CaseId]=@{status='pending'}}else{
            if(-not $ConfirmObserved){throw 'Explicit ConfirmObserved is required; results are operator attestations.'}
            Assert-UatId $EvidenceId
            $case=@{status=$Status;kind='operator-attested';evidenceId=$EvidenceId;observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
            if($CaseId -eq 'next-day' -and $Status -eq 'passed'){$case.businessDate=$BusinessDate;$case.startedAtUtc=$StartedAtUtc}
            $record.cases[$CaseId]=$case
        }
    }elseif($Action -eq 'Defect'){
        if(-not $CaseId -or -not $Category -or -not $Severity){throw 'CaseId, Category and Severity are required.'}
        $id=[Guid]::NewGuid().ToString('N')
        $record.defects+=@{id=$id;caseId=$CaseId;category=$Category;severity=$Severity;status='open';openedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
        Write-Output "Defect: $id"
    }elseif($Action -eq 'Resolve'){
        Assert-UatId $DefectId
        $matches=@($record.defects|Where-Object id -CEQ $DefectId)
        if($matches.Count -ne 1 -or $matches[0].status -ne 'open'){throw 'Resolve requires one open defect.'}
        if(-not $ConfirmObserved){throw 'ConfirmObserved is required for a verified resolution; retest afterward.'}
        $matches[0].status='resolved';$matches[0].resolvedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
    }elseif($Action -eq 'Report'){
        $report=Get-UatReport $record (-not $bindingProblem)
        $report|ConvertTo-Json -Depth 8|Set-Content "$directory/report.json"
        $report|ConvertTo-Json -Depth 8
        return
    }
    Write-UatRecord $directory $record
}finally{$lock.Dispose()}
