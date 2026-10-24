#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $ConfirmDisposableInfrastructure,
    [switch] $ConfirmDestructiveRollback,
    [switch] $NoBuild,
    [string] $DockerExecutable = 'docker'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $ConfirmDisposableInfrastructure -or -not $ConfirmDestructiveRollback) {
    throw 'Pass both confirmation switches to authorize generated infrastructure and the tested destructive POS migration rollback.'
}
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not (Get-Command $DockerExecutable -ErrorAction SilentlyContinue)) {
    $DockerExecutable = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/DockerDesktop/resources/bin/docker.exe'
}
if (-not (Test-Path -LiteralPath $DockerExecutable -PathType Leaf) -and
    -not (Get-Command $DockerExecutable -ErrorAction SilentlyContinue)) { throw 'Docker CLI is unavailable.' }
if (-not [string]::IsNullOrWhiteSpace($env:DOCKER_HOST)) { throw 'DOCKER_HOST must be unset; only local Docker is accepted.' }
$context = (& $DockerExecutable context show 2>$null).Trim()
$endpoint = (& $DockerExecutable context inspect $context --format '{{.Endpoints.docker.Host}}' 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $context -notin @('default', 'desktop-linux') -or
    $endpoint -notin @('npipe:////./pipe/dockerDesktopLinuxEngine', 'npipe:////./pipe/docker_engine', 'unix:///var/run/docker.sock')) {
    throw 'A verified local Docker Desktop or local Unix socket is required.'
}

$runId = [Guid]::NewGuid().ToString('N')
$project = "nexa-cashier-sale-$runId"
$run = Join-Path $root ".runstate/pos-cashier-sale/$runId"
$composeFile = Join-Path $root 'docker/pos-cashier-sale-acceptance/compose.yaml'
$compose = @('compose', '-f', $composeFile, '-p', $project)
$environmentNames = @(
    'NEXACONNECT_CASHIER_ACCEPTANCE_POSTGRES_PASSWORD', 'NEXACONNECT_CASHIER_ACCEPTANCE_RABBITMQ_PASSWORD',
    'NEXACONNECT_POS_INTEGRATION_DB', 'NEXACONNECT_ORDER_INTEGRATION_DB',
    'NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB', 'NEXACONNECT_RABBITMQ_INTEGRATION_URI',
    'NEXACONNECT_PRICING_ACCEPTANCE', 'NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE',
    'NEXACONNECT_ENVIRONMENT', 'DOTNET_ENVIRONMENT'
)
$previous = @{}
foreach ($name in $environmentNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$created = $false
$backendPassed = $false
$verifierContractPassed = $false
$cleanupPassed = $false
New-Item -ItemType Directory -Path $run -Force | Out-Null

try {
    $env:NEXACONNECT_CASHIER_ACCEPTANCE_POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $env:NEXACONNECT_CASHIER_ACCEPTANCE_RABBITMQ_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $existing = @(& $DockerExecutable @compose ps -aq)
    if ($LASTEXITCODE -ne 0 -or $existing.Count -ne 0) { throw 'Generated cashier acceptance project must be empty.' }
    $created = $true
    & $DockerExecutable @compose up -d --wait --wait-timeout 180
    if ($LASTEXITCODE -ne 0) { throw 'Disposable cashier acceptance infrastructure failed to start.' }
    $postgresBinding = (& $DockerExecutable @compose port postgres 5432).Trim()
    $rabbitBinding = (& $DockerExecutable @compose port rabbitmq 5672).Trim()
    if ($postgresBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Unexpected PostgreSQL binding.' }
    $postgresPort = [int]$Matches[1]
    if ($rabbitBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Unexpected RabbitMQ binding.' }
    $rabbitPort = [int]$Matches[1]
    $postgres = "Host=127.0.0.1;Port=$postgresPort;Database=postgres;Username=postgres;Password=$($env:NEXACONNECT_CASHIER_ACCEPTANCE_POSTGRES_PASSWORD);SSL Mode=Disable"
    $rabbitPassword = [Uri]::EscapeDataString($env:NEXACONNECT_CASHIER_ACCEPTANCE_RABBITMQ_PASSWORD)
    $env:NEXACONNECT_POS_INTEGRATION_DB = $postgres
    $env:NEXACONNECT_ORDER_INTEGRATION_DB = $postgres
    $env:NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB = $postgres
    $env:NEXACONNECT_RABBITMQ_INTEGRATION_URI = "amqp://acceptance:$rabbitPassword@127.0.0.1:$rabbitPort/"
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    $env:DOTNET_ENVIRONMENT = 'Testing'

    & (Join-Path $root 'scripts/test-pos-paid-workflow.ps1') -ConfirmDisposableInfrastructure -ConfirmDestructiveRollback
    if ($LASTEXITCODE -ne 0) { throw 'Protected POS recovery/projection acceptance failed.' }

    $env:NEXACONNECT_PRICING_ACCEPTANCE = '1'
    $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE = '1'
    $integrationProject = Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests'
    $receiptTrx = Join-Path $run 'receipt-and-migration.trx'
    $buildOption = @()
    if ($NoBuild) { $buildOption += '--no-build' }
    & dotnet test $integrationProject --no-restore @buildOption `
        --filter 'FullyQualifiedName~PaidOrderReceiptPostgresTests|FullyQualifiedName~PaidOrderReceiptHttpTests|FullyQualifiedName~OrderMigrationRunnerAcceptanceTests' `
        --logger "trx;LogFileName=$receiptTrx" --results-directory $run --verbosity minimal
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $receiptTrx)) {
        throw 'Order receipt/migration acceptance failed.'
    }
    [xml]$receiptResults = Get-Content -LiteralPath $receiptTrx -Raw
    $receiptCounts = $receiptResults.TestRun.ResultSummary.Counters
    if ([int]$receiptCounts.total -ne 3 -or [int]$receiptCounts.passed -ne 3 -or [int]$receiptCounts.notExecuted -ne 0) {
        throw 'All three Order receipt, authorization, and migration acceptance cases must execute and pass.'
    }
    $backendPassed = $true

    & pwsh -NoProfile -File (Join-Path $root 'tests/Scripts/Test-PosCashierAcceptance.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Cashier live-verifier contract tests failed.' }
    $verifierContractPassed = $true
}
finally {
    $cleanupFailed = $false
    try {
        if ($created) {
            & $DockerExecutable @compose down --volumes --remove-orphans | Out-Null
            if ($LASTEXITCODE -ne 0) { $cleanupFailed = $true }
            $remaining = @(& $DockerExecutable @compose ps -aq)
            if ($LASTEXITCODE -ne 0 -or $remaining.Count -ne 0) { $cleanupFailed = $true }
        }
    }
    finally {
        foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
        $cleanupPassed = -not $cleanupFailed
        [ordered]@{
            runId = $runId
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            automatedBackendPassed = $backendPassed
            verifierContractPassed = $verifierContractPassed
            protectedPosCases = 20
            orderReceiptAuthorizationAndMigrationCases = 3
            verifierFixtureCases = 15
            cleanupVerified = $cleanupPassed
            liveOidcVerified = $false
            wpfInteractionVerified = $false
            windowsPrintDialogVerified = $false
            productionVerified = $false
            secretsPrinted = $false
            sourceRevision = (& git -C $root rev-parse HEAD)
            sourceDirty = [bool](& git -C $root status --porcelain)
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'verification.json') -Encoding utf8
    }
    if ($cleanupFailed) { throw 'Disposable cashier acceptance cleanup could not be verified.' }
}

if (-not $backendPassed -or -not $verifierContractPassed) { throw 'POS cashier-sale automated gate did not complete.' }
Write-Output "POS cashier-sale automated gate passed; sanitized evidence: $run/verification.json"
