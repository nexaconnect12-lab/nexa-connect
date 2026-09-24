[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $ConfirmDisposableInfrastructure) { throw 'This runner creates and destroys an isolated PostgreSQL/RabbitMQ project and kills its acceptance child processes. Pass -ConfirmDisposableInfrastructure.' }
$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
$dockerDirectory = $null
if (-not $dockerCommand) {
    $candidate = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/DockerDesktop/resources/bin/docker.exe'
    if (-not (Test-Path -LiteralPath $candidate)) { throw 'Docker is required; no live acceptance was performed.' }
    $dockerDirectory = Split-Path -Parent $candidate
}
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runId = [Guid]::NewGuid().ToString('N')
$projectName = "cashclose-$runId"
$run = Join-Path $root ".runstate/cash-close/$runId"
New-Item -ItemType Directory -Path $run -Force | Out-Null
$compose = Join-Path $root 'docker/cash-close-acceptance/compose.yaml'
$project = Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$settings = @('CASH_ACCEPTANCE_PASSWORD','NEXACONNECT_ENVIRONMENT','NEXACONNECT_CASH_CLOSE_ACCEPTANCE','NEXACONNECT_POS_INTEGRATION_DB','NEXACONNECT_REPORTING_INTEGRATION_DB','NEXACONNECT_RABBITMQ_INTEGRATION_URI','NEXACONNECT_CASH_RUN_DIR','NEXACONNECT_CASH_BROKER_CONTAINER','NEXACONNECT_CASH_HOST_DLL','NEXACONNECT_CASH_REPLAY_DLL','NEXACONNECT_CASH_MANAGEMENT_URI')
$previous = @{}
$settings += @('CASH_ACCEPTANCE_PG_PORT','CASH_ACCEPTANCE_BROKER_PORT','CASH_ACCEPTANCE_MANAGEMENT_PORT')
foreach ($key in $settings) { $previous[$key] = [Environment]::GetEnvironmentVariable($key) }
$previousPath = $env:PATH
$passed = $false
$counts = $null
$sourceRevision = (& git -C $root rev-parse HEAD)
$sourceDirty = [bool](& git -C $root status --porcelain)
try {
    if ($dockerDirectory) { $env:PATH = $dockerDirectory + [IO.Path]::PathSeparator + $env:PATH }
    $env:CASH_ACCEPTANCE_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    # Allocate distinct loopback ports, then keep those explicit bindings across Docker stop/start.
    $listeners = @()
    try {
        foreach ($setting in @('CASH_ACCEPTANCE_PG_PORT','CASH_ACCEPTANCE_BROKER_PORT','CASH_ACCEPTANCE_MANAGEMENT_PORT')) {
            $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
            $listener.Start(); $listeners += $listener
            [Environment]::SetEnvironmentVariable($setting,[string]$listener.LocalEndpoint.Port)
        }
    } finally { foreach ($listener in $listeners) { $listener.Stop() } }
    if (-not $NoBuild) {
        foreach ($target in @('src/Tools/NexaConnect.CashCloseReplay/NexaConnect.CashCloseReplay.csproj','src/Tools/NexaConnect.CashCloseAcceptance/NexaConnect.CashCloseAcceptance.csproj','tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj')) {
            & dotnet build (Join-Path $root $target) --verbosity minimal
            if ($LASTEXITCODE -ne 0) { throw 'Acceptance build failed.' }
        }
    }
    & docker compose -p $projectName -f $compose up -d --wait --wait-timeout 120
    if ($LASTEXITCODE -ne 0) { throw 'Disposable infrastructure failed readiness.' }
    $pgEndpoint = & docker compose -p $projectName -f $compose port postgres 5432
    if ($LASTEXITCODE -ne 0 -or $pgEndpoint -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Invalid PostgreSQL binding.' }
    $pgPort = $Matches[1]
    $rabbitEndpoint = & docker compose -p $projectName -f $compose port rabbitmq 5672
    if ($LASTEXITCODE -ne 0 -or $rabbitEndpoint -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Invalid broker binding.' }
    $rabbitPort = $Matches[1]
    $managementEndpoint = & docker compose -p $projectName -f $compose port rabbitmq 15672
    if ($LASTEXITCODE -ne 0 -or $managementEndpoint -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Invalid management binding.' }
    $env:NEXACONNECT_CASH_MANAGEMENT_URI = "http://127.0.0.1:$($Matches[1])/"
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    $env:NEXACONNECT_CASH_CLOSE_ACCEPTANCE = '1'
    $env:NEXACONNECT_POS_INTEGRATION_DB = "Host=127.0.0.1;Port=$pgPort;Database=cashclose_acceptance;Username=acceptance;Password=$env:CASH_ACCEPTANCE_PASSWORD;Timeout=5;Command Timeout=15"
    $env:NEXACONNECT_REPORTING_INTEGRATION_DB = $env:NEXACONNECT_POS_INTEGRATION_DB
    $env:NEXACONNECT_RABBITMQ_INTEGRATION_URI = "amqp://acceptance:$($env:CASH_ACCEPTANCE_PASSWORD)@127.0.0.1:$rabbitPort/"
    $env:NEXACONNECT_CASH_RUN_DIR = $run
    $env:NEXACONNECT_CASH_BROKER_CONTAINER = "$projectName-rabbitmq-1"
    $env:NEXACONNECT_CASH_HOST_DLL = Join-Path $root 'src/Tools/NexaConnect.CashCloseAcceptance/bin/Debug/net10.0/NexaConnect.CashCloseAcceptance.dll'
    $env:NEXACONNECT_CASH_REPLAY_DLL = Join-Path $root 'src/Tools/NexaConnect.CashCloseReplay/bin/Debug/net10.0/NexaConnect.CashCloseReplay.dll'
    $trx = Join-Path $run 'cash-close.trx'
    & dotnet test $project --no-build --no-restore --filter FullyQualifiedName~CashCloseProjectionPostgresTests --logger "trx;LogFileName=$trx" --verbosity minimal
    $testExitCode = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $trx)) { throw 'Cash-close live acceptance produced no result.' }
    [xml]$results = Get-Content -LiteralPath $trx -Raw
    $counts = $results.TestRun.ResultSummary.Counters
    if ($testExitCode -ne 0) { throw 'Cash-close live acceptance failed.' }
    if ([int]$counts.total -ne 2 -or [int]$counts.passed -ne 2 -or [int]$counts.notExecuted -ne 0) { throw 'Acceptance requires both real database/process cases to pass without skips.' }
    $passed = $true
}
finally {
    & docker compose -p $projectName -f $compose down --volumes --remove-orphans | Out-Null
    $cleanupFailed = $LASTEXITCODE -ne 0
    # Do not upload TRX/exception bodies: this allow-listed status contains no payloads or credentials.
    @{ testsPassed=$passed; total=[int]$counts.total; passed=[int]$counts.passed; skipped=[int]$counts.notExecuted; cleanupVerified=(-not $cleanupFailed); sourceRevision=$sourceRevision; sourceDirty=$sourceDirty } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'verification.json') -Encoding utf8
    foreach ($key in $settings) { [Environment]::SetEnvironmentVariable($key,$previous[$key]) }
    $env:PATH = $previousPath
    if ($cleanupFailed) { throw 'Disposable cash-close project cleanup failed; inspect the exact run project.' }
}
@{ runId=$runId; passed=2; completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); cleanupVerified=$true; restrictedReplayVerified=$true; sourceRevision=$sourceRevision; sourceDirty=$sourceDirty; productionVerified=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'evidence.json') -Encoding utf8
Write-Output "Cash-close recovery matrix passed; evidence: $run"
