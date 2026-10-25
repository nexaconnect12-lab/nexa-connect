#requires -Version 7.4
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'The complete QR6 candidate includes current-user Windows DPAPI and WPF compilation.'}
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm generated local PostgreSQL/RabbitMQ fixture creation and exact cleanup.'}
$repoRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
$id=[guid]::NewGuid().ToString();$run=Join-Path $repoRoot ".runstate/qr6/$id"
New-Item -ItemType Directory -Path $run|Out-Null;Protect-DraftDirectory $run
$checks=[ordered]@{};$passed=$false;$outputs=$null
$prior=@{};foreach($key in @('NEXACONNECT_ENVIRONMENT','NEXACONNECT_POS_DPAPI_ACCEPTANCE','PLAYWRIGHT_JSON_OUTPUT_FILE')){$prior[$key]=[Environment]::GetEnvironmentVariable($key)}
function Native([string]$Name,[string]$Command,[string[]]$Arguments){
    & $Command @Arguments *> (Join-Path $run "$Name.private.log")
    if($LASTEXITCODE -ne 0){throw "QR6 gate failed: $Name; inspect private diagnostics."}
    $script:checks[$Name]=@{passed=$true;count=1}
}
function CountResult([string]$Kind,[string]$File,[int]$Minimum){
    $count=& node (Join-Path $PSScriptRoot 'release/results.mjs') $Kind $File $Minimum
    if($LASTEXITCODE -ne 0){throw 'QR6 results contain failed, missing, skipped or retried tests.'};return [int]$count
}
function Trx([string]$Name,[string]$Project,[string]$Filter,[int]$Minimum){
    $arguments=@('test',$Project,'--no-restore','--verbosity','quiet','--logger',"trx;LogFileName=$Name.trx",'--results-directory',$run)
    if($Filter){$arguments+=@('--filter',$Filter)}
    Native $Name dotnet $arguments
    $script:checks[$Name].count=Read-DraftTestCounts (Join-Path $run "$Name.trx") $Minimum
}
Push-Location $repoRoot
try{
    $sourceJson=& node scripts/release/source.mjs;if($LASTEXITCODE -ne 0){throw 'QR6 source inventory failed.'};$source=$sourceJson|ConvertFrom-Json
    $schemaJson=& node scripts/release/qr6.mjs catalog;if($LASTEXITCODE -ne 0){throw 'QR6 schema inventory failed.'};$schemas=$schemaJson|ConvertFrom-Json
    $env:NEXACONNECT_ENVIRONMENT='Testing';$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE='1'
    Trx unit 'tests/Unit/NexaConnect.UnitTests' '' 991
    Trx architecture 'tests/Architecture/NexaConnect.ArchitectureTests' '' 6
    Trx http 'tests/Integration/NexaConnect.IntegrationTests' 'FullyQualifiedName~QrGuestBoundaryTests|FullyQualifiedName~QrGatewayTransportTests|FullyQualifiedName~QrRestaurantBoundaryTests|FullyQualifiedName~QrMenuBoundaryTests|FullyQualifiedName~QrMenuTransportTests' 22
    Native hosting node @('--test','--test-reporter=tap','docker/pilot/hosting.test.mjs')
    $checks.hosting.count=CountResult tap (Join-Path $run 'hosting.private.log') 47
    Native candidate-guards node @('--test','--test-reporter=tap','scripts/release/qr6.test.mjs')
    $checks['candidate-guards'].count=CountResult tap (Join-Path $run 'candidate-guards.private.log') 20
    Native pos-build dotnet @('build','src/Clients/NexaConnect.POS','--no-restore','--verbosity','quiet')
    $npm=Join-Path (Split-Path (Get-Command node).Source -Parent) 'node_modules/npm/bin/npm-cli.js'
    Push-Location (Join-Path $repoRoot 'src/Frontend')
    try{
        Native frontend-check node @($npm,'run','check')
        $json=Join-Path $run 'frontend-unit.json';Native frontend-unit node @($npm,'test','--','--reporter=json',"--outputFile=$json")
        $checks['frontend-unit'].count=CountResult vitest $json 78
        foreach($pair in @(@('frontend-build','build'),@('audit-production','audit:production'),@('audit-all','audit:all'))){Native $pair[0] node @($npm,'run',$pair[1])}
        foreach($pair in @(@('qr-browser','qr-entry',54),@('day-close-browser','day-close',8),@('day-cutoff-browser','day-cutoff',8),@('day-seal-browser','day-seal',22),@('settlement-reporting-browser','settlement-reporting',7))){
            $json=Join-Path $run ($pair[0]+'.json');$env:PLAYWRIGHT_JSON_OUTPUT_FILE=$json
            Native $pair[0] node @('node_modules/@playwright/test/cli.js','test','--config',"playwright.$($pair[1]).config.mjs",'--reporter=json')
            $checks[$pair[0]].count=CountResult playwright $json $pair[2]
        }
    }finally{Pop-Location}
    try{$fixtureOutput=@(& (Join-Path $PSScriptRoot 'test-qr-entry-persistence.ps1') -ConfirmDisposableInfrastructure 2>&1 | Tee-Object -FilePath (Join-Path $run 'owning.private.log'))}
    catch{throw 'QR6 owning fixture failed; private diagnostics retained.'}
    $match=@($fixtureOutput|ForEach-Object { if($_ -is [string] -and $_ -match '^QR persistence gate passed with cleanup: (.+/verification\.json)$'){$Matches[1]} })
    if($match.Count -ne 1){throw 'QR6 owning receipt was not uniquely identified.'}
    $receiptPath=[IO.Path]::GetFullPath($match[0]);$receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
    $expected=Join-Path $repoRoot ".runstate/qr-entry/$($receipt.runId)/verification.json"
    if($receipt.runId -cnotmatch '^[a-f0-9]{32}$' -or $receiptPath -cne [IO.Path]::GetFullPath($expected) -or $receipt.passed -ne $true -or $receipt.cleanupVerified -ne $true -or $receipt.brokerRecoveryVerified -ne $true -or $receipt.caseCount -lt 5 -or $receipt.productionVerified -ne $false){throw 'QR6 owning evidence is incomplete.'}
    Copy-Item -LiteralPath $receiptPath -Destination (Join-Path $run 'owning.json')
    $checks.owning=@{passed=$true;count=[int]$receipt.caseCount;cleanupVerified=$true;brokerRecoveryVerified=$true;evidenceSha256=(Get-FileHash -LiteralPath (Join-Path $run 'owning.json') -Algorithm SHA256).Hash.ToLowerInvariant()}
    $outputJson=& node scripts/release/qr6.mjs artifacts;if($LASTEXITCODE -ne 0){throw 'QR6 compiled artifact inventory failed.'};$outputs=$outputJson|ConvertFrom-Json
    $after=& node scripts/release/source.mjs
    if($LASTEXITCODE -ne 0 -or ($after -join "`n") -cne ($sourceJson -join "`n")){throw 'QR6 source changed during verification.'}
    $passed=$true
}finally{
    foreach($key in $prior.Keys){[Environment]::SetEnvironmentVariable($key,$prior[$key])}
    Pop-Location
    @{version=1;kind='qr6';runId=$id;source=$source;schemas=$schemas;artifacts=$outputs;passed=$passed;checks=$checks;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');productionVerified=$false}|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $run 'verification.json')
}
& node (Join-Path $PSScriptRoot 'release/qr6.mjs') prepare $run
if($LASTEXITCODE -ne 0){throw 'QR6 candidate preparation failed.'}
Write-Output "QR6 local test candidate prepared: $run/candidate.json"
