#requires -Version 7.4
param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm generated local PostgreSQL fixture creation and exact cleanup.'}
$endpoint=& docker context inspect --format '{{.Endpoints.docker.Host}}'
if($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://' -or ($env:DOCKER_HOST -and $env:DOCKER_HOST -notmatch '^(npipe|unix)://')){throw 'A local Docker socket is required.'}
$id=[guid]::NewGuid().ToString('N');$container="nexa-qr-it-$id";$database="nexa_qr_it_$id";$password=[guid]::NewGuid().ToString('N')
$brokerContainer="nexa-qr-broker-it-$id";$brokerUser="qr_$id";$brokerPassword=[guid]::NewGuid().ToString('N')
$repoRoot=Split-Path $PSScriptRoot -Parent
$run=Join-Path $repoRoot ".runstate/qr-entry/$id";New-Item -ItemType Directory -Path $run -Force|Out-Null
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
if($IsWindows){Protect-DraftDirectory $run}else{& chmod 700 $run;if($LASTEXITCODE -ne 0){throw 'Unable to restrict QR fixture diagnostics.'}}
$prior=$env:NEXACONNECT_QR_INTEGRATION_DB;$priorBroker=$env:NEXACONNECT_QR_INTEGRATION_BROKER;$created=$false;$brokerCreated=$false;$passed=$false;$cleanup=$false
try{
    & docker run --pull never -d --name $container -e "POSTGRES_PASSWORD=$password" -e "POSTGRES_DB=$database" -p '127.0.0.1::5432' postgres:17-alpine | Out-Null
    if($LASTEXITCODE -ne 0){throw 'QR fixture startup failed.'};$created=$true
    $portMap=& docker port $container 5432/tcp;$port=[int]($portMap.Split(':')[-1]);$ready=$false
    for($i=0;$i -lt 60;$i++){& docker exec $container pg_isready -U postgres -d $database 2>$null|Out-Null;if($LASTEXITCODE -eq 0){$ready=$true;break};Start-Sleep -Milliseconds 500}
    if(-not $ready){throw 'QR fixture was not ready.'}
    & docker run --pull never -d --name $brokerContainer -e "RABBITMQ_DEFAULT_USER=$brokerUser" -e "RABBITMQ_DEFAULT_PASS=$brokerPassword" -p '127.0.0.1::5672' rabbitmq:4-management | Out-Null
    if($LASTEXITCODE -ne 0){throw 'QR broker fixture startup failed.'};$brokerCreated=$true
    $brokerPortMap=& docker port $brokerContainer 5672/tcp;$brokerPort=[int]($brokerPortMap.Split(':')[-1]);$brokerReady=$false
    for($i=0;$i -lt 80;$i++){& docker exec --user rabbitmq $brokerContainer rabbitmq-diagnostics -q ping 2>$null|Out-Null;if($LASTEXITCODE -eq 0){$brokerReady=$true;break};Start-Sleep -Milliseconds 500}
    if(-not $brokerReady){& docker logs $brokerContainer *> (Join-Path $run 'broker-startup.private.log');throw 'QR broker fixture was not ready.'}
    $env:NEXACONNECT_QR_INTEGRATION_DB="Host=127.0.0.1;Port=$port;Database=$database;Username=postgres;Password=$password"
    $env:NEXACONNECT_QR_INTEGRATION_BROKER="amqp://${brokerUser}:${brokerPassword}@127.0.0.1:$brokerPort"
    & dotnet test (Join-Path $repoRoot 'tests/Integration/NexaConnect.IntegrationTests') --no-restore --filter 'FullyQualifiedName~QrEntryPersistenceTests|FullyQualifiedName~QrMenuPersistenceTests|FullyQualifiedName~QrRequestPersistenceTests|FullyQualifiedName~QrAcceptancePersistenceTests|FullyQualifiedName~QrJourneyPersistenceTests|FullyQualifiedName~OrderListPersistenceTests|FullyQualifiedName~DraftMediaAccessPersistenceTests|FullyQualifiedName~BarcodePersistenceTests' --verbosity quiet --logger 'trx;LogFileName=qr.trx' --results-directory $run *> (Join-Path $run 'persistence.private.log')
    if($LASTEXITCODE -ne 0){throw 'QR persistence test failed; private diagnostics retained.'}
    . (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
    [void](Read-DraftTestCounts (Join-Path $run 'qr.trx') 8);$passed=$true
}finally{
    $env:NEXACONNECT_QR_INTEGRATION_DB=$prior
    $env:NEXACONNECT_QR_INTEGRATION_BROKER=$priorBroker
    $cleanup=$true
    foreach($target in @(@($created,$container,'^nexa-qr-it-[a-f0-9]{32}$'),@($brokerCreated,$brokerContainer,'^nexa-qr-broker-it-[a-f0-9]{32}$'))){
        if($target[0]){
            if($target[1] -cnotmatch $target[2]){$cleanup=$false;continue}
            & docker rm -f -v $target[1]|Out-Null
            if($LASTEXITCODE -ne 0){$cleanup=$false}
        }
    }
    @{runId=$id;passed=($passed -and $cleanup);cleanupVerified=$cleanup;caseCount=8;brokerRecoveryVerified=($passed -and $cleanup);productionVerified=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'verification.json')
    if(-not $cleanup){throw 'QR fixture cleanup failed.'}
}
Write-Output "QR persistence gate passed with cleanup: $run/verification.json"
