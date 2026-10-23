#requires -Version 7.4
param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm generated local PostgreSQL fixture creation and exact cleanup.'}
$endpoint=& docker context inspect --format '{{.Endpoints.docker.Host}}'
if($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://' -or ($env:DOCKER_HOST -and $env:DOCKER_HOST -notmatch '^(npipe|unix)://')){throw 'A local Docker socket is required.'}
$id=[guid]::NewGuid().ToString('N');$container="nexa-qr-it-$id";$database="nexa_qr_it_$id";$password=[guid]::NewGuid().ToString('N')
$repoRoot=Split-Path $PSScriptRoot -Parent
$run=Join-Path $repoRoot ".runstate/qr-entry/$id";New-Item -ItemType Directory -Path $run -Force|Out-Null
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
if($IsWindows){Protect-DraftDirectory $run}else{& chmod 700 $run;if($LASTEXITCODE -ne 0){throw 'Unable to restrict QR fixture diagnostics.'}}
$prior=$env:NEXACONNECT_QR_INTEGRATION_DB;$created=$false;$passed=$false;$cleanup=$false
try{
    & docker run --pull never -d --name $container -e "POSTGRES_PASSWORD=$password" -e "POSTGRES_DB=$database" -p '127.0.0.1::5432' postgres:17-alpine | Out-Null
    if($LASTEXITCODE -ne 0){throw 'QR fixture startup failed.'};$created=$true
    $portMap=& docker port $container 5432/tcp;$port=[int]($portMap.Split(':')[-1]);$ready=$false
    for($i=0;$i -lt 60;$i++){& docker exec $container pg_isready -U postgres -d $database 2>$null|Out-Null;if($LASTEXITCODE -eq 0){$ready=$true;break};Start-Sleep -Milliseconds 500}
    if(-not $ready){throw 'QR fixture was not ready.'}
    $env:NEXACONNECT_QR_INTEGRATION_DB="Host=127.0.0.1;Port=$port;Database=$database;Username=postgres;Password=$password"
    & dotnet test (Join-Path $repoRoot 'tests/Integration/NexaConnect.IntegrationTests') --no-restore --filter 'FullyQualifiedName~QrEntryPersistenceTests|FullyQualifiedName~QrMenuPersistenceTests' --verbosity quiet --logger 'trx;LogFileName=qr.trx' --results-directory $run *> (Join-Path $run 'persistence.private.log')
    if($LASTEXITCODE -ne 0){throw 'QR persistence test failed; private diagnostics retained.'}
    . (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
    [void](Read-DraftTestCounts (Join-Path $run 'qr.trx') 2);$passed=$true
}finally{
    $env:NEXACONNECT_QR_INTEGRATION_DB=$prior
    if($created){
        if($container -cnotmatch '^nexa-qr-it-[a-f0-9]{32}$'){throw 'Unexpected QR fixture identity.'}
        & docker rm -f -v $container|Out-Null
        if($LASTEXITCODE -ne 0){throw 'QR fixture cleanup failed.'}
    }
    $cleanup=$true
    @{runId=$id;passed=($passed -and $cleanup);cleanupVerified=$cleanup;caseCount=2;productionVerified=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'verification.json')
}
Write-Output "QR persistence gate passed with cleanup: $run/verification.json"
