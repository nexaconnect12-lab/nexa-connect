#requires -Version 7.4
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. "$PSScriptRoot/first-draft-uat-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/pos-acceptance-helpers.ps1"
. "$PSScriptRoot/pos-acceptance-worklist.ps1"
. "$PSScriptRoot/pos-recovery-lab-helpers.ps1"
$count=0
function Require([bool]$Condition){if(-not $Condition){throw 'Recovery lab guard failed.'};$script:count++}
function Reject([scriptblock]$Work){try{& $Work|Out-Null}catch{$script:count++;return};throw 'Unsafe lab input accepted.'}
function Fresh {
 $binding=@{runId='a'*32;labId='a'*32;releaseId='b'*32}
 foreach($key in @('labManifestHash','packageManifestHash','configurationHash','backendArtifacts','referenceHash')){$binding[$key]='c'*64}
 @{version=2;id='d'*32;kind='recovery-lab-operator-observations';binding=$binding;events=@()}
}
$record=Fresh;Assert-PosAcceptanceRecord $record;$count++
$report=Get-PosAcceptanceReport $record $true $true
Require ($report.version -eq 2 -and $report.targetKind -ceq 'recovery-lab' -and $report.target.labId -ceq ('a'*32))
Require (-not $report.qrCandidateFresh -and -not $report.qrCandidateApplicable -and -not $report.releaseReady -and $report.cases.Count -eq 17)
Require (-not $report.target.Contains('pilotId'))
Require ((Get-PosAcceptanceWorklist $record $true $true).targetKind -ceq 'recovery-lab')
Require (-not (Get-PosAcceptanceWorklist $record $true $true).qrCandidateFresh)
$event=@{type='observe';caseId='session-expiry-fallback';status='passed';bindingValid=$true;evidenceId='e'*32;observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
$updated=Add-PosAcceptanceEvent $record $event
Require ($updated.events.Count -eq 1 -and $record.events.Count -eq 0)
$event.bindingValid=$false;Reject {Add-PosAcceptanceEvent $record $event}
foreach($key in @('runId','labId','releaseId','labManifestHash','packageManifestHash','configurationHash','backendArtifacts','referenceHash')){
 $other=Fresh;$other.binding[$key]='f'*64;Reject {Assert-PosAcceptanceSnapshotEqual $record.binding $other.binding}
}
$record.privateSecret='fixture-sensitive';$record.binding.endpoint='fixture-sensitive'
Require (-not ((Get-PosAcceptanceReport $record $true $false|ConvertTo-Json -Depth 12).Contains('fixture-sensitive')))
$saved=@{};foreach($name in @('root','url','command')){$saved[$name]=@{exists=$true;value='original-fixture'}}
$result=Get-PosRecoveryProtocolRestore $saved 'lab-owned' 'lab-owned'
Require ($result.command.value -ceq 'original-fixture')
Reject {Get-PosRecoveryProtocolRestore $saved 'another-owner' 'lab-owned'}
$bad=@{root=@{exists='true';value='fixture'};url=$saved.url;command=$saved.command}
Reject {Get-PosRecoveryProtocolRestore $bad 'lab-owned' 'lab-owned'}
$id='a'*32;$scope=@{};foreach($key in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){$scope[$key]=[guid]::NewGuid().ToString('D')}
$state=@{runId=$id;realm="nexa-review-it-$id";authority="http://127.0.0.1:49001/realms/nexa-review-it-$id";urls=@{POS='http://127.0.0.1:49002/';Order='http://127.0.0.1:49003/';Catalog='http://127.0.0.1:49004/';Payment='http://127.0.0.1:49005/'}}
$profile=@{Identity=@{Authority=$state.authority;ClientId='nexaconnect-pos';RedirectUri='nexaconnect-pos://oauth/callback'};Services=@{PosApi=$state.urls.POS;OrderApi=$state.urls.Order;CatalogApi=$state.urls.Catalog;PaymentApi=$state.urls.Payment};Pos=$scope.Clone()}
$profile.Pos.Currency='THB';$profile.Pos.PaymentMethod='cash_manual';$profile.Pos.EnableOmiseTestCheckout=$false;$profile.Pos.UseIsolatedLocalPilotStorage=$true
$reference=@{};foreach($key in $scope.Keys){$reference[$key.Substring(0,1).ToLowerInvariant()+$key.Substring(1)]=$scope[$key]}
$reference=ConvertFrom-UatJson ($reference|ConvertTo-Json)
Assert-PosRecoveryLabProfile $profile $state $reference;$count++
$profile.Pos.UseIsolatedLocalPilotStorage=$false;Reject {Assert-PosRecoveryLabProfile $profile $state $reference};$profile.Pos.UseIsolatedLocalPilotStorage=$true
$profile.Pos.BranchId=[guid]::NewGuid().ToString('D');Reject {Assert-PosRecoveryLabProfile $profile $state $reference};$profile.Pos.BranchId=$scope.BranchId
$profile.Services.OrderApi='http://127.0.0.1:49009/';Reject {Assert-PosRecoveryLabProfile $profile $state $reference}
& pwsh -NoProfile -File "$PSScriptRoot/pos-recovery-lab.ps1" -Action Prepare *> $null
Require ($LASTEXITCODE -ne 0)
& pwsh -NoProfile -File "$PSScriptRoot/pos-recovery-lab.ps1" -Action Prepare -LabId $id -ConfirmTestResources *> $null
Require ($LASTEXITCODE -ne 0)
Write-Output "PASS: $count UX21 lab/target/callback/profile guards. No infrastructure, registry or operator records changed."
