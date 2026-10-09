param([switch]$ConfirmDisposableInfrastructure,[string]$DockerExecutable='docker')
$ErrorActionPreference='Stop'
if(!$ConfirmDisposableInfrastructure){throw 'ConfirmDisposableInfrastructure is required; this runner creates and removes only its generated local PostgreSQL container.'}
$dockerCommand=Get-Command $DockerExecutable -ErrorAction SilentlyContinue
$dockerPath=if($dockerCommand){$dockerCommand.Source}elseif($IsWindows){Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'}else{throw 'Docker executable is required.'}
$ownedContainer='nexa_day_seal_it_'+[guid]::NewGuid().ToString('N')
$runId=[guid]::NewGuid().ToString('N')
$evidenceDirectory=Join-Path (Get-Location) ".runstate/day-seals/$runId"
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
$containerStarted=$false;$passed=$false;$cleanupVerified=$false;$caseCount=0
$sourceRevision=(& git rev-parse HEAD).Trim()
$sourceDirty=!!(& git status --porcelain)
$temporaryPassword=[guid]::NewGuid().ToString('N')
$variables=@('NEXACONNECT_ENVIRONMENT','NEXACONNECT_POS_INTEGRATION_DB','NEXACONNECT_REPORTING_INTEGRATION_DB')
$prior=@{};foreach($name in $variables){$prior[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    & $dockerPath run --pull never --detach --name $ownedContainer -e "POSTGRES_PASSWORD=$temporaryPassword" -e POSTGRES_DB=day_close_it -p '127.0.0.1::5432' postgres:17-alpine | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Disposable PostgreSQL failed to start.'}
    $containerStarted=$true
    $portMap=& $dockerPath port $ownedContainer 5432/tcp
    $dbPort=[int]($portMap.Split(':')[-1]);$healthy=$false
    for($probe=0;$probe -lt 40;$probe++){
        & $dockerPath exec $ownedContainer pg_isready -U postgres -d day_close_it 2>$null | Out-Null
        if($LASTEXITCODE -eq 0){$healthy=$true;break};Start-Sleep -Milliseconds 250
    }
    if(!$healthy){throw 'Disposable PostgreSQL did not become ready.'}
    $env:NEXACONNECT_ENVIRONMENT='Testing'
    $env:NEXACONNECT_POS_INTEGRATION_DB="Host=127.0.0.1;Port=$dbPort;Database=day_close_it;Username=postgres;Password=$temporaryPassword"
    $env:NEXACONNECT_REPORTING_INTEGRATION_DB=$env:NEXACONNECT_POS_INTEGRATION_DB
    dotnet test tests/Integration/NexaConnect.IntegrationTests --no-restore --filter '(FullyQualifiedName~DayCutoffPostgresTests|FullyQualifiedName~DaySealPostgresTests|FullyQualifiedName~EndOfDaySourcePostgresTests|FullyQualifiedName~DayCutoffPipelineTests|FullyQualifiedName~SourceFinancialRevisionPostgresTests|FullyQualifiedName~FinancialCompletenessPipelineTests)&FullyQualifiedName!~Hosted_outboxes_survive&FullyQualifiedName!~Hosted_correction_consumer' -v quiet --logger 'console;verbosity=normal' --logger 'trx;LogFileName=seals.trx' --results-directory $evidenceDirectory
    if($LASTEXITCODE -ne 0){throw 'Day-seal PostgreSQL acceptance failed.'}
    [xml]$testResult=Get-Content -LiteralPath (Join-Path $evidenceDirectory 'seals.trx') -Raw
    $counters=$testResult.TestRun.ResultSummary.Counters
    $caseCount=[int]$counters.total
    if($caseCount -lt 64 -or [int]$counters.passed -ne $caseCount -or [int]$counters.executed -ne $caseCount){throw 'The complete database/Application matrix must pass without skips.'}
    $passed=$true
} finally {
    foreach($name in $variables){[Environment]::SetEnvironmentVariable($name,$prior[$name])}
    try {
        if($containerStarted){
            if($ownedContainer -notmatch '^nexa_day_seal_it_[0-9a-f]{32}$'){throw 'Generated container identity is invalid.'}
            & $dockerPath rm -f $ownedContainer | Out-Null
            if($LASTEXITCODE -ne 0){throw 'Generated PostgreSQL container cleanup failed.'}
        }
        $cleanupVerified=$true
    } finally {
        @{runId=$runId;sourceRevision=$sourceRevision;sourceDirty=$sourceDirty;passed=($passed -and $cleanupVerified);caseCount=$caseCount;cleanupVerified=$cleanupVerified;productionVerified=$false;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'verification.json') -Encoding utf8
    }
}
