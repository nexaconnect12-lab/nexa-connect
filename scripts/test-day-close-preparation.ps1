param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(!$ConfirmDisposableInfrastructure){throw 'ConfirmDisposableInfrastructure is required; this runner creates and removes only its generated local PostgreSQL container.'}
$dockerPath=Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$ownedContainer='nexa_day_close_it_'+[guid]::NewGuid().ToString('N')
$temporaryPassword=[guid]::NewGuid().ToString('N')
$variables=@('NEXACONNECT_ENVIRONMENT','NEXACONNECT_POS_INTEGRATION_DB','NEXACONNECT_REPORTING_INTEGRATION_DB')
$prior=@{};foreach($name in $variables){$prior[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    & $dockerPath run --pull never --detach --name $ownedContainer -e "POSTGRES_PASSWORD=$temporaryPassword" -e POSTGRES_DB=day_close_it -p '127.0.0.1::5432' postgres:17-alpine | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Disposable PostgreSQL failed to start.'}
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
    dotnet test tests/Integration/NexaConnect.IntegrationTests --no-restore --filter 'FullyQualifiedName~DayClosePostgresTests|FullyQualifiedName~EndOfDaySourcePostgresTests' -v quiet --logger 'console;verbosity=normal'
    if($LASTEXITCODE -ne 0){throw 'Day-close PostgreSQL acceptance failed.'}
} finally {
    foreach($name in $variables){[Environment]::SetEnvironmentVariable($name,$prior[$name])}
    if($ownedContainer -match '^nexa_day_close_it_[0-9a-f]{32}$'){& $dockerPath rm -f $ownedContainer | Out-Null;if($LASTEXITCODE -ne 0){throw 'Generated PostgreSQL container cleanup failed.'}}
}
