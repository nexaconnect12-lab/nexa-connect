#requires -Version 7.4
$ErrorActionPreference='Stop'
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-uat-helpers.ps1"
$checks=0
function Rejected([scriptblock]$Action){$bad=$false;try{& $Action|Out-Null}catch{$bad=$true};if(-not $bad){throw 'UAT guard accepted invalid evidence.'};$script:checks++}
function Check([bool]$Condition){if(-not $Condition){throw 'UAT guard assertion failed.'};$script:checks++}
function Copy-Record($Value){ConvertFrom-UatJson ($Value|ConvertTo-Json -Depth 20)}
foreach($file in @('first-draft-uat.ps1','first-draft-uat-helpers.ps1','test-first-draft-uat-guards.ps1')){
    $tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile("$PSScriptRoot/$file",[ref]$tokens,[ref]$errors)
    Check (-not $errors)
}
$candidate=@{revision='a'*40;inputHash='b'*64;dirty=$false}
$cases=[ordered]@{};foreach($id in (Get-UatCases)){$cases[$id]=@{status='pending'}}
$record=@{format=1;id=[Guid]::NewGuid().ToString('N');candidate=$candidate;cases=$cases;defects=@();binding=$null}
Check (@(Get-UatBlockers $record).Count -eq 17)
$bad=Copy-Record $record;$bad.cases.Remove('setup');Rejected {Assert-UatRecord $bad}
$bad=Copy-Record $record;$bad.cases.setup.status='waived';Rejected {Assert-UatRecord $bad}
$now=[DateTimeOffset]::UtcNow.AddSeconds(-1).ToString('O');$evidence=[Guid]::NewGuid().ToString('N')
$pass=@{status='passed';kind='operator-attested';evidenceId=$evidence;observedAtUtc=$now}
$bad=Copy-Record $record;$bad.cases.setup=$pass;Rejected {Assert-UatRecord $bad}
$record.binding=@{runId=[Guid]::NewGuid().ToString('N');terminalReleaseId=[Guid]::NewGuid().ToString('N');releaseRunId=[Guid]::NewGuid().ToString('N');packageManifestHash='a'*64;releaseReceiptHash='b'*64}
foreach($id in (Get-UatCases)){$record.cases[$id]=Copy-Record $pass}
$day=[DateTimeOffset]::UtcNow.AddDays(-2).ToOffset([TimeSpan]::FromHours(8)).Date
$start=[DateTimeOffset]::new($day.AddHours(10),[TimeSpan]::FromHours(8)).ToUniversalTime().ToString('O')
$record.cases['next-day'].businessDate=$day.ToString('yyyy-MM-dd');$record.cases['next-day'].startedAtUtc=$start
Check (@(Get-UatBlockers $record).Count -eq 0)
$report=Get-UatReport $record $true
Check ($report.readyForUserDecision -and $report.acceptance -ceq 'pending-user-decision')
Check (-not (Get-UatReport $record $false).readyForUserDecision)
$record.privateNotes='must never export'
Check ((Get-UatReport $record $true|ConvertTo-Json -Depth 12) -notmatch 'must never export')
$bad=Copy-Record $record;$bad.candidate.dirty=$true;Rejected {Assert-UatRecord $bad}
$bad=Copy-Record $record;$bad.cases.setup.kind='measured';Rejected {Assert-UatRecord $bad}
$bad=Copy-Record $record;$bad.cases.setup.evidenceId='../secret';Rejected {Assert-UatRecord $bad}
$bad=Copy-Record $record;$bad.cases.setup.observedAtUtc=[DateTimeOffset]::UtcNow.AddDays(1).ToString('O');Rejected {Assert-UatRecord $bad}
Rejected {Assert-UatNextDay $day.ToString('yyyy-MM-dd') $start $start}
Rejected {Assert-UatNextDay ([DateTime]::UtcNow.AddDays(1).ToString('yyyy-MM-dd')) $start $now}
Assert-UatNextDay $day.ToString('yyyy-MM-dd') $start $now;$checks++
foreach($field in @('revision','inputHash','dirty')){$bad=Copy-Record $candidate;$bad[$field]=if($field -eq 'dirty'){$true}else{'c'*64};Rejected {Assert-UatCandidate $candidate $bad}}
$bad=Copy-Record $candidate;$bad.dirty='false';Rejected {Assert-UatCandidate $candidate $bad}
foreach($category in @('financial','security','data-loss','unrecoverable','usability')){
    $defect=@{id=[Guid]::NewGuid().ToString('N');caseId='cash-checkout';category=$category;severity='minor';status='open';openedAtUtc=$now}
    $record.defects=@($defect)
    Check (@(Get-UatBlockers $record).Count -eq $(if($category -eq 'usability'){0}else{1}))
}
Check ((Get-UatReport $record $true).minorBacklog.Count -eq 1)
$defect.severity='high';Check (@(Get-UatBlockers $record).Count -eq 1)
$defect.status='resolved';$defect.resolvedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');Check (@(Get-UatBlockers $record)[0] -like 'retest:*')
$defect.openedAtUtc=[DateTimeOffset]::UtcNow.AddMinutes(-2).ToString('O');$defect.resolvedAtUtc=[DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O');Check (@(Get-UatBlockers $record).Count -eq 0)
$bad=Copy-Record $record;$bad.binding.runId='private arbitrary text';Rejected {Assert-UatRecord $bad}
$bad=Copy-Record $record;$bad.defects[0].resolvedAtUtc=[DateTimeOffset]::UtcNow.AddDays(-1).ToString('O');Rejected {Assert-UatRecord $bad}
$record.defects+=Copy-Record $defect;Rejected {Assert-UatRecord $record};$record.defects=@()
$draft=[Guid]::NewGuid().ToString('N');$terminal=[Guid]::NewGuid().ToString('N');$release=[Guid]::NewGuid().ToString('N')
$config=@{Identity=@{Authority="http://127.0.0.1:18500/realms/nexa-review-it-$draft";ClientId='nexaconnect-pos';RedirectUri='nexaconnect-pos://oauth/callback'};Services=@{PosApi='http://127.0.0.1:18501/';OrderApi='http://127.0.0.1:18502/';CatalogApi='http://127.0.0.1:18503/'};Pos=@{OrganizationId=[Guid]::NewGuid();RestaurantId=[Guid]::NewGuid();BranchId=[Guid]::NewGuid();StoreId=[Guid]::NewGuid();TerminalId=[Guid]::NewGuid();Currency='THB';PaymentMethod='cash_manual';EnableOmiseTestCheckout=$false}}
$state=@{runId=$draft;project="nexa-draft-$draft";realm="nexa-review-it-$draft";status='Ready';provisionedBranch=$true}
$package=@{runId=$draft;releaseId=$terminal;sourceRevision=$candidate.revision;sourceDirty=$false;sourceInputHash=$candidate.inputHash;scope=(Get-TerminalScope $config)}
$receipt=@{runId=$release;passed=$true;sourceRevision=$candidate.revision;sourceDirty=$false;candidateInputHash=$candidate.inputHash}
Assert-UatBindingEvidence $state $package $config $receipt $candidate $draft $terminal $release;$checks++
foreach($status in @('Reset','Stopped','Failed')){$bad=Copy-Record $state;$bad.status=$status;Rejected {Assert-UatBindingEvidence $bad $package $config $receipt $candidate $draft $terminal $release}}
$bad=Copy-Record $state;$bad.provisionedBranch='true';Rejected {Assert-UatBindingEvidence $bad $package $config $receipt $candidate $draft $terminal $release}
$bad=Copy-Record $receipt;$bad.passed='true';Rejected {Assert-UatBindingEvidence $state $package $config $bad $candidate $draft $terminal $release}
$bad=Copy-Record $receipt;$bad.candidateInputHash='c'*64;Rejected {Assert-UatBindingEvidence $state $package $config $bad $candidate $draft $terminal $release}
$bad=Copy-Record $package;$bad.runId=[Guid]::NewGuid().ToString('N');Rejected {Assert-UatBindingEvidence $state $bad $config $receipt $candidate $draft $terminal $release}
$bad=Copy-Record $package;$bad.Remove('sourceInputHash');Rejected {Assert-UatBindingEvidence $state $bad $config $receipt $candidate $draft $terminal $release}
$bad=Copy-Record $config;$bad.Pos.TerminalId=[Guid]::NewGuid().ToString();Rejected {Assert-UatBindingEvidence $state $package $bad $receipt $candidate $draft $terminal $release}
Write-Output "$checks UAT safety checks passed; synthetic fixtures are not user acceptance."
