#requires -Version 7.4
[CmdletBinding()]
param([ValidateSet('Source','Joined')][string]$Profile='Source',[switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'The complete source profile includes Windows DPAPI checks. Use the documented Linux CI jobs for complementary gates.'}
if($Profile -eq 'Joined' -and -not $ConfirmDisposableInfrastructure){throw 'Joined verification requires authorization for generated disposable infrastructure and financial fixture operations.'}
$repoRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-manager-helpers.ps1')
$runId=[guid]::NewGuid().ToString()
$run=Join-Path $repoRoot ".runstate/next-iteration-verification/$runId"
New-Item -ItemType Directory -Path $run -Force | Out-Null
Protect-DraftDirectory $run
Push-Location $repoRoot
$sourceJson=& node scripts/release/source.mjs
if($LASTEXITCODE -ne 0){throw 'Source inventory failed.'}
$source=$sourceJson|ConvertFrom-Json
$checks=[ordered]@{};$passed=$false
$prior=@{};foreach($key in @('NEXACONNECT_ENVIRONMENT','NEXACONNECT_POS_DPAPI_ACCEPTANCE','PLAYWRIGHT_JSON_OUTPUT_FILE')){$prior[$key]=[Environment]::GetEnvironmentVariable($key)}
function Native([string]$Name,[string]$Command,[string[]]$Arguments){
    & $Command @Arguments *> (Join-Path $run "$Name.private.log")
    if($LASTEXITCODE -ne 0){throw "Gate failed: $Name (private diagnostics retained)."}
    $script:checks[$Name]=@{passed=$true;count=1}
}
function Trx([string]$Name,[string]$Project,[string]$Filter,[int]$Minimum){
    $arguments=@('test',$Project,'--no-restore','--verbosity','quiet','--logger',"trx;LogFileName=$Name.trx",'--results-directory',$run)
    if($Filter){$arguments+=@('--filter',$Filter)}
    Native $Name dotnet $arguments
    $script:checks[$Name].count=Read-DraftTestCounts (Join-Path $run "$Name.trx") $Minimum
}
function ResultCount([string]$Kind,[string]$Path,[int]$Minimum){
    $count=& node (Join-Path $PSScriptRoot 'release/results.mjs') $Kind $Path $Minimum
    if($LASTEXITCODE -ne 0){throw 'Test results were incomplete, skipped, retried or failed.'}
    return [int]$count
}
function Fixture([string]$Name,[string]$File,[hashtable]$Arguments){
    $log=Join-Path $run "$Name.private.log"
    try{& (Join-Path $PSScriptRoot $File) @Arguments 2>&1 | Tee-Object -FilePath $log}
    catch{$_|Out-String|Add-Content -LiteralPath $log;throw "Gate failed: $Name (private diagnostics retained)."}
}
try{
    $env:NEXACONNECT_ENVIRONMENT='Testing';$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE='1'
    Trx unit 'tests/Unit/NexaConnect.UnitTests' '' 937
    Trx architecture 'tests/Architecture/NexaConnect.ArchitectureTests' '' 6
    Trx http-boundaries 'tests/Integration/NexaConnect.IntegrationTests' 'FullyQualifiedName~CustomerGalleryBoundaryTests|FullyQualifiedName~CustomerKitchenBoundaryTests|FullyQualifiedName~PosShiftApiTests|FullyQualifiedName~CustomerDayCloseBoundaryTests' 12
    Native hosting-guards node @('--test','--test-reporter=tap','docker/pilot/hosting.test.mjs')
    $checks['hosting-guards'].count=ResultCount tap (Join-Path $run 'hosting-guards.private.log') 46
    Native release-guards node @('--test','--test-reporter=tap','scripts/release/next-release.test.mjs')
    $checks['release-guards'].count=ResultCount tap (Join-Path $run 'release-guards.private.log') 41
    Native identity-guards pwsh @('-NoProfile','-File','scripts/test-keycloak-production-guards.ps1')
    Native installer-guards pwsh @('-NoProfile','-File','scripts/test-pos-installer-guards.ps1')
    $identityLog=Get-Content -LiteralPath (Join-Path $run 'identity-guards.private.log') -Raw
    if($identityLog -notmatch 'Production identity guards: (\d+) passed' -or [int]$Matches[1] -lt 10){throw 'Incomplete identity guard inventory.'}
    $checks['identity-guards'].count=[int]$Matches[1]
    $installerLog=Get-Content -LiteralPath (Join-Path $run 'installer-guards.private.log') -Raw
    if($installerLog -notmatch '(\d+) installer tooling guards passed' -or [int]$Matches[1] -lt 15){throw 'Incomplete installer guard inventory.'}
    $checks['installer-guards'].count=[int]$Matches[1]
    $npmCli=Join-Path (Split-Path (Get-Command node).Source -Parent) 'node_modules/npm/bin/npm-cli.js'
    Push-Location (Join-Path $repoRoot 'src/Frontend')
    try{
        Native frontend-check node @($npmCli,'run','check')
        $unitJson=Join-Path $run 'frontend-unit.json'
        Native frontend-unit node @($npmCli,'test','--','--reporter=json',"--outputFile=$unitJson")
        $checks['frontend-unit'].count=ResultCount vitest $unitJson 76
        foreach($pair in @(@('frontend-build','build'),@('audit-production','audit:production'),@('audit-all','audit:all'))){Native $pair[0] node @($npmCli,'run',$pair[1])}
        foreach($pair in @(@('kitchen-browser','kitchen',19),@('gallery-browser','gallery',7),@('ui-browser','ui-foundation',10))){
            $json=Join-Path $run ($pair[0]+'.json');$env:PLAYWRIGHT_JSON_OUTPUT_FILE=$json
            Native $pair[0] node @('node_modules/@playwright/test/cli.js','test','--config',"playwright.$($pair[1]).config.mjs",'--reporter=json')
            $checks[$pair[0]].count=ResultCount playwright $json $pair[2]
        }
    }finally{Pop-Location}
    if($Profile -eq 'Joined'){
        foreach($matrix in @(@('settlement-reporting','test-settlement-reporting.ps1','draft-release-reporting','settlement-reporting',9),@('settlement-replay','test-settlement-replay.ps1','draft-release-replay','settlement-replay',6))){
            $output=@(Fixture $matrix[0] $matrix[1] @{ConfirmDisposableInfrastructure=$true})
            $evidence=Read-DraftMatrixEvidence $repoRoot $output $matrix[2] $matrix[3] $matrix[4] $source.revision
            $checks[$matrix[0]]=@{passed=$true;count=$matrix[4];cleanupVerified=$true;evidenceSha256=(Get-FileHash -LiteralPath $evidence.evidencePath).Hash}
        }
        $output=@(Fixture checkout-restore test-first-draft-checkout.ps1 @{ConfirmDisposableInfrastructure=$true;VerifyReleaseRecovery=$true})
        $normal=Read-DraftManagerEvidence $repoRoot $output 'draft-manager-normal'
        $receipt=Get-Content -LiteralPath $normal.evidencePath -Raw|ConvertFrom-Json
        if($receipt.checks.restoreRecovery -ne $true -or $receipt.checks.'manager-resumed' -ne $true){throw 'Checkout restore/resume evidence missing.'}
        $checks['checkout-restore']=@{passed=$true;count=1;cleanupVerified=$true;evidenceSha256=(Get-FileHash -LiteralPath $normal.evidencePath).Hash}
        $output=@(Fixture historical-cutoff test-financial-completeness-portal.ps1 @{CashierDayCutoff=$true;BusinessTimeZone='Asia/Singapore';ConfirmDisposableInfrastructure=$true})
        $history=Read-DraftManagerEvidence $repoRoot $output 'draft-manager-historical'
        $checks['historical-cutoff']=@{passed=$true;count=1;cleanupVerified=$true;evidenceSha256=(Get-FileHash -LiteralPath $history.evidencePath).Hash}
        Native gallery-persistence pwsh @('-NoProfile','-File','scripts/test-branch-gallery-persistence.ps1','-ConfirmDisposableInfrastructure')
        # This fixture refuses to return success unless its exact generated container is removed.
        $checks['gallery-persistence'].cleanupVerified=$true
    }
    $after=& node scripts/release/source.mjs
    if($LASTEXITCODE -ne 0 -or ($after -join "`n") -cne ($sourceJson -join "`n")){throw 'Candidate source changed during verification.'}
    $passed=$true
}finally{
    foreach($key in $prior.Keys){[Environment]::SetEnvironmentVariable($key,$prior[$key])}
    Pop-Location
    @{version=1;runId=$runId;profile=$Profile;source=$source;passed=$passed;checks=$checks;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');productionVerified=$false;userAcceptance='pending'}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $run 'verification.json')
}
Write-Output "Next-iteration $Profile verification passed: $run/verification.json"
