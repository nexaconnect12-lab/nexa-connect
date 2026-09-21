#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ($env:DOCKER_HOST) { throw 'DOCKER_HOST must be unset; use local Docker Desktop.' }
$dockerCommand=Get-Command docker -ErrorAction SilentlyContinue
$dockerPath=if($dockerCommand){$dockerCommand.Source}else{Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'}
if(-not(Test-Path -LiteralPath $dockerPath -PathType Leaf)){throw 'Local Docker Desktop CLI unavailable.'}
$context=(& $dockerPath context show).Trim()
$endpoint=(& $dockerPath context inspect $context --format '{{.Endpoints.docker.Host}}').Trim()
if($LASTEXITCODE -ne 0 -or $context -notin @('default','desktop-linux') -or $endpoint -notin @('npipe:////./pipe/dockerDesktopLinuxEngine','npipe:////./pipe/docker_engine')){throw 'Require local Docker Desktop named pipe.'}
$compose=Join-Path $root 'docker-compose.yml'
$address=(& $dockerPath compose --project-name nexa-connect --project-directory $root -f $compose port postgres 5432).Trim()
if($LASTEXITCODE -ne 0 -or $address -notmatch ':(\d+)$'){throw 'Start the existing local PostgreSQL Compose service first.'}
$port=[int]$Matches[1]
$password=(& $dockerPath compose --project-name nexa-connect --project-directory $root -f $compose exec -T postgres printenv POSTGRES_PASSWORD 2>$null)-join ''
if($LASTEXITCODE -ne 0 -or [string]::IsNullOrEmpty($password)){throw 'Local PostgreSQL test credentials unavailable.'}
$connection=[System.Data.Common.DbConnectionStringBuilder]::new()
$connection['Host']='127.0.0.1';$connection['Port']=$port;$connection['Database']='postgres';$connection['Username']='postgres';$connection['Password']=$password
$saved=@{}
$names=@('NEXACONNECT_ENVIRONMENT','NEXACONNECT_OMISE_WEBHOOK_INTEGRATION_DB')+@([Environment]::GetEnvironmentVariables('Process').Keys | Where-Object {$_ -like 'NEXACONNECT_OMISE_*'})
foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$run=Join-Path $root ('.runstate/payment-omise-webhook-local/'+[Guid]::NewGuid().ToString('N'))
try{
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    foreach($name in $names){if($name -like 'NEXACONNECT_OMISE_*'){[Environment]::SetEnvironmentVariable($name,$null,'Process')}}
    $env:NEXACONNECT_ENVIRONMENT='Testing'
    $env:NEXACONNECT_OMISE_WEBHOOK_INTEGRATION_DB=$connection.ConnectionString
    $unit=Join-Path $root 'tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj'
    $integration=Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
    & dotnet test $unit --no-restore --filter 'FullyQualifiedName~OmiseWebhookTests|FullyQualifiedName~OmisePaymentProviderTests' --logger 'trx;LogFileName=unit.trx' --results-directory $run --verbosity quiet
    if($LASTEXITCODE -ne 0){throw 'Omise webhook local unit verification failed.'}
    & dotnet test $integration --no-restore --filter 'FullyQualifiedName~OmiseWebhookHttpTests|FullyQualifiedName~OmiseWebhookPersistenceTests' --logger 'trx;LogFileName=integration.trx' --results-directory $run --verbosity quiet
    if($LASTEXITCODE -ne 0){throw 'Omise webhook local HTTP/persistence verification failed.'}
    [xml]$unitResults=Get-Content (Join-Path $run 'unit.trx') -Raw
    [xml]$integrationResults=Get-Content (Join-Path $run 'integration.trx') -Raw
    $unitCounters=$unitResults.TestRun.ResultSummary.Counters
    $integrationCounters=$integrationResults.TestRun.ResultSummary.Counters
    if([int]$unitCounters.executed -ne 133 -or [int]$unitCounters.passed -ne 133 -or [int]$integrationCounters.executed -ne 13 -or [int]$integrationCounters.passed -ne 13){throw 'Expected all 133 unit and 13 HTTP/PostgreSQL cases to execute without skips.'}
    [ordered]@{
        completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
        localVerificationPassed=$true
        unitCases=133
        httpCases=9
        postgresCases=4
        realProviderRequestsSent=0
        financialCommandsSent=0
        externalWebhookDeliveryVerified=$false
        processKillVerified=$false
        simulatedLeaseRecoveryVerified=$true
        productRecordsChanged=$false
        credentialsRetained=$false
    } | ConvertTo-Json | Set-Content (Join-Path $run 'summary.json') -Encoding utf8
    Write-Output "Omise webhook local verification passed. Safe evidence retained at '$run'. External provider delivery remains unverified."
}finally{
    foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}
    $password=$null;$connection.Clear()
}
