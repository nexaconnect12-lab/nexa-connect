#requires -Version 7.4
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm generated draft, PostgreSQL/broker matrices and isolated restore cleanup.'}
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-manager-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
Set-Location $root
$id=[Guid]::NewGuid().ToString('N');$run=Join-Path $root ".runstate/first-draft-release/$id"
New-Item -ItemType Directory -Path $run | Out-Null
Protect-DraftDirectory $run
$candidate=Get-DraftCandidate $root
$candidate|ConvertTo-Json -Depth 5|Set-Content (Join-Path $run 'candidate.json')
$checks=[ordered]@{};$stages=[ordered]@{};$passed=$false
$prior=@{NEXACONNECT_ENVIRONMENT=$env:NEXACONNECT_ENVIRONMENT;NEXACONNECT_POS_DPAPI_ACCEPTANCE=$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE}
function Native([scriptblock]$Action){& $Action;if($LASTEXITCODE -ne 0){throw 'Release verification command failed; private run output contains diagnostics.'}}
try {
    Write-Output "Candidate $id input SHA-256: $($candidate.inputHash)"
    foreach($guard in @('test-first-draft-guards.ps1','test-first-draft-terminal-guards.ps1','test-first-draft-manager-guards.ps1','test-first-draft-release-guards.ps1','test-first-draft-uat-guards.ps1')){& (Join-Path $PSScriptRoot $guard)}
    $checks.guards=$true
    $env:NEXACONNECT_ENVIRONMENT='Testing';$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE='1'
    foreach($suite in @(@('Unit','NexaConnect.UnitTests',833),@('Architecture','NexaConnect.ArchitectureTests',6))){
        $project=Join-Path $root "tests/$($suite[0])/$($suite[1])"
        Native {dotnet test $project -c Release --artifacts-path (Join-Path $run 'artifacts') -m:1 --verbosity minimal --logger "trx;LogFileName=$($suite[0]).trx" --results-directory $run}
        $checks[$suite[0]]=Read-DraftTestCounts (Join-Path $run "$($suite[0]).trx") $suite[2]
    }
    Native {dotnet test tests/Integration/NexaConnect.IntegrationTests --filter 'FullyQualifiedName~PaymentHealthEndpointTests' --verbosity minimal --logger 'trx;LogFileName=readiness.trx' --results-directory $run}
    $checks.readiness=Read-DraftTestCounts (Join-Path $run 'readiness.trx') 2
    Restore-DraftEnvironment $prior
    Push-Location (Join-Path $root 'src/Frontend')
    try {
        Native {npm ci --ignore-scripts}
        foreach($command in @('check','test','build','audit:production','audit:all')){Native {npm run $command};$checks["frontend:$command"]=$true}
        Native {node --test e2e/cashier-day-cutoff-live/settings.test.mjs}
    } finally {Pop-Location}
    foreach($matrix in @(@('test-day-seals.ps1','draft-release-seals','day-seals',64),@('test-settlement-reporting.ps1','draft-release-reporting','settlement-reporting',9),@('test-settlement-replay.ps1','draft-release-replay','settlement-replay',6))){
        Write-Output "Running $($matrix[0])"
        $output=@(& (Join-Path $PSScriptRoot $matrix[0]) -ConfirmDisposableInfrastructure)
        $stages[$matrix[1]]=Read-DraftMatrixEvidence $root $output $matrix[1] $matrix[2] $matrix[3] $candidate.revision
    }
    & (Join-Path $PSScriptRoot 'test-cash-close-alerts.ps1')
    $checks.alertRules=$true
    Write-Output 'Running cash-close portal branch discovery and projection regression gate.'
    $output=@(& (Join-Path $PSScriptRoot 'test-cash-close-portal.ps1') -ConfirmDisposableInfrastructure)
    $stages.cashPortal=Read-DraftMatrixEvidence $root $output 'draft-release-cash-portal' 'cash-close-portal' 5 $candidate.revision
    Write-Output 'Running real checkout, Kitchen, manager and isolated restore drill.'
    $output=@(& (Join-Path $PSScriptRoot 'test-first-draft-checkout.ps1') -ConfirmDisposableInfrastructure -VerifyReleaseRecovery)
    $normal=Read-DraftManagerEvidence $root $output 'draft-manager-normal'
    $receipt=Get-Content $normal.evidencePath -Raw|ConvertFrom-Json
    foreach($check in @('restoreRecovery','manager-resumed')){if($receipt.checks.$check -isnot [bool] -or -not $receipt.checks.$check){throw 'Restore/resumed workflow evidence is missing.'}}
    $stages.normal=$normal
    $artifacts=@(Get-ChildItem (Join-Path (Split-Path $normal.evidencePath -Parent) 'apps'),(Join-Path (Split-Path $normal.evidencePath -Parent) 'pos') -Recurse -File|Where-Object Extension -in '.dll','.exe','.js','.css'|ForEach-Object {@{path=[IO.Path]::GetRelativePath($root,$_.FullName);sha256=(Get-FileHash $_.FullName).Hash.ToLowerInvariant()}})
    $artifacts|ConvertTo-Json -Depth 3|Set-Content (Join-Path $run 'build-artifacts.json')
    Write-Output 'Running historical Singapore cutoff, settlement and CSV matrix.'
    $output=@(& (Join-Path $PSScriptRoot 'test-financial-completeness-portal.ps1') -CashierDayCutoff -BusinessTimeZone Asia/Singapore -ConfirmDisposableInfrastructure)
    $stages.historical=Read-DraftManagerEvidence $root $output 'draft-manager-historical'
    if($normal.sourceRevision -cne $candidate.revision -or $stages.historical.sourceRevision -cne $candidate.revision){throw 'Candidate source revision changed.'}
    $checks.inputUnchanged=(Get-DraftCandidate $root).inputHash -ceq $candidate.inputHash
    if(-not $checks.inputUnchanged){throw 'Candidate executable inputs changed during verification.'}
    $passed=$true
} finally {
    Restore-DraftEnvironment $prior
    @{runId=$id;passed=$passed;candidateInputHash=$candidate.inputHash;sourceRevision=$candidate.revision;sourceDirty=$candidate.dirty;schemaTargets=(Get-DraftServiceCatalog $root);checks=$checks;stages=$stages;cleanCheckoutCiVerified=$false;externalAlertReceiverVerified=$false;windowsPrintingVerified=$false;realNextDayStaffDrillVerified=$false;productionVerified=$false;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $run 'verification.json')
}
Write-Output "Local draft release verification passed: $run/verification.json"
