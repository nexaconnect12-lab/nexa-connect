param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference = 'Stop'
if (-not $ConfirmDisposableInfrastructure) { throw 'Explicit disposable infrastructure confirmation is required.' }
$pricingDocker = (Get-Command docker -ErrorAction SilentlyContinue).Source
if (-not $pricingDocker) { $pricingDocker = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/DockerDesktop/resources/bin/docker.exe' }
if (-not (Test-Path $pricingDocker)) { throw 'Docker CLI is unavailable.' }
$runId = [Guid]::NewGuid().ToString('N')
$containerName = "nexa-pricing-$runId"
$results = Join-Path $PSScriptRoot "../.runstate/order-pricing/$runId"
$names = @('POSTGRES_PASSWORD','NEXACONNECT_ORDER_INTEGRATION_DB','NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB',
    'NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE','NEXACONNECT_PRICING_ACCEPTANCE','DOTNET_ENVIRONMENT','NEXACONNECT_ENVIRONMENT')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$started = $false
try {
    $env:POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $container = & $pricingDocker run --detach --name $containerName --label "nexa.pricing.run=$runId" --publish '127.0.0.1::5432' --env POSTGRES_PASSWORD postgres:17-alpine
    if ($LASTEXITCODE -ne 0) { throw 'Disposable PostgreSQL startup failed.' }
    $started = $true
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        & $pricingDocker exec $containerName pg_isready -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'Disposable PostgreSQL did not become ready.' }
    $binding = (& $pricingDocker port $containerName 5432/tcp).Trim()
    if ($binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Unexpected PostgreSQL binding.' }
    $env:NEXACONNECT_ORDER_INTEGRATION_DB = "Host=127.0.0.1;Port=$($Matches[1]);Database=postgres;Username=postgres;Password=$env:POSTGRES_PASSWORD;SSL Mode=Disable"
    $env:NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB = $env:NEXACONNECT_ORDER_INTEGRATION_DB
    $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE = '1'
    $env:NEXACONNECT_PRICING_ACCEPTANCE = '1'
    $env:DOTNET_ENVIRONMENT = 'Testing'
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    & dotnet test (Join-Path $PSScriptRoot '../tests/Integration/NexaConnect.IntegrationTests') --no-restore --filter 'FullyQualifiedName~OrderPricingPostgresTests|FullyQualifiedName~OrderMigrationRunnerAcceptanceTests|FullyQualifiedName~PaidOrderReceiptPostgresTests' --results-directory $results --logger 'trx;LogFileName=pricing.trx' --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Pricing acceptance failed.' }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $results 'pricing.trx')
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -ne 4 -or [int]$counters.passed -ne 4 -or [int]$counters.notExecuted -ne 0) { throw 'Pricing acceptance did not execute all four required tests.' }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    if ($started) {
        $label = & $pricingDocker inspect --format '{{ index .Config.Labels "nexa.pricing.run" }}' $containerName
        if ($LASTEXITCODE -ne 0 -or $label.Trim() -ne $runId) { throw 'Cannot verify disposable container ownership for cleanup.' }
        & $pricingDocker rm --force --volumes $containerName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Disposable pricing container cleanup failed.' }
    }
}
Write-Host 'Pricing PostgreSQL acceptance passed; disposable container and volumes removed.'
