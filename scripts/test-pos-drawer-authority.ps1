#requires -Version 7.4
param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm creation and cleanup of an isolated local drawer PostgreSQL fixture.'}
$endpoint=& docker context inspect --format '{{.Endpoints.docker.Host}}'
if($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://' -or ($env:DOCKER_HOST -and $env:DOCKER_HOST -notmatch '^(npipe|unix)://')){throw 'A local Docker socket is required.'}
$repoRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
$id=[guid]::NewGuid().ToString('N');$container="nexa-drawer-it-$id";$database="nexa_drawer_it_$id";$password=[guid]::NewGuid().ToString('N')
$run=Join-Path $repoRoot ".runstate/drawer-authority/$id";New-Item -ItemType Directory -Path $run|Out-Null
if($IsWindows){Protect-DraftDirectory $run}else{& chmod 700 $run;if($LASTEXITCODE){throw 'Unable to restrict diagnostics.'}}
$prior=$env:NEXACONNECT_DRAWER_INTEGRATION_DB;$created=$false;$passed=$false;$cleanup=$false;$count=0
try{
 & docker run --pull never -d --name $container -e "POSTGRES_PASSWORD=$password" -e "POSTGRES_DB=$database" -p '127.0.0.1::5432' postgres:17-alpine|Out-Null
 if($LASTEXITCODE){throw 'Fixture startup failed'};$created=$true
 $map=& docker port $container 5432/tcp;if($LASTEXITCODE){throw 'Fixture port lookup failed'};$port=[int]($map.Split(':')[-1]);$ready=$false
 for($i=0;$i -lt 60;$i++){& docker exec $container pg_isready -U postgres -d $database 2>$null|Out-Null;if($LASTEXITCODE -eq 0){$ready=$true;break};Start-Sleep -Milliseconds 500}
 if(-not $ready){throw 'Fixture not ready'}
 $env:NEXACONNECT_DRAWER_INTEGRATION_DB="Host=127.0.0.1;Port=$port;Database=$database;Username=postgres;Password=$password"
 & dotnet test (Join-Path $repoRoot 'tests/Integration/NexaConnect.IntegrationTests') --no-restore -m:1 --filter 'FullyQualifiedName~DrawerIntentPersistenceTests' --verbosity quiet --logger 'trx;LogFileName=drawer.trx' --results-directory $run *> (Join-Path $run 'persistence.private.log')
 if($LASTEXITCODE){throw 'Drawer owning tests failed; inspect private diagnostics.'}
 $count=Read-DraftTestCounts (Join-Path $run 'drawer.trx') 4;$passed=$true
}finally{
 $env:NEXACONNECT_DRAWER_INTEGRATION_DB=$prior;$cleanup=$true
 if($created){if($container -cnotmatch '^nexa-drawer-it-[a-f0-9]{32}$'){$cleanup=$false}else{& docker rm -f -v $container|Out-Null;if($LASTEXITCODE){$cleanup=$false}}}
 @{runId=$id;passed=($passed -and $cleanup);caseCount=$count;cleanupVerified=$cleanup;productionVerified=$false}|ConvertTo-Json|Set-Content (Join-Path $run 'verification.json')
 if(-not $cleanup){throw 'Fixture cleanup failed'}
}
Write-Output "Drawer authority owning gate passed with cleanup: $run/verification.json"
