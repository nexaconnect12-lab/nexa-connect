[CmdletBinding()]
param(
    [switch] $ConfirmDisposableInfrastructure,
    [switch] $ConfirmDestructiveRollback,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmDisposableInfrastructure -or -not $ConfirmDestructiveRollback) {
    throw 'Confirm disposable infrastructure and destructive POS migration rollback before running.'
}

$required = @(
    'NEXACONNECT_POS_INTEGRATION_DB',
    'NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB',
    'NEXACONNECT_RABBITMQ_INTEGRATION_URI'
)
foreach ($name in $required) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing acceptance setting: $name. Inject it without printing its value."
    }
    if ($value -match '(?i)(^|[;:/@._-])(prod|production)([;:/@._-]|$)') {
        throw "Refusing $name because it appears to identify production infrastructure."
    }
}

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$run = Join-Path $root ('.runstate/pos-paid-workflow/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$unitTrx = Join-Path $run 'pos-paid-client-recovery.trx'
$integrationTrx = Join-Path $run 'pos-paid-backend-live.trx'
$unitProject = Join-Path $root 'tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj'
$integrationProject = Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$previousEnvironment = $env:NEXACONNECT_ENVIRONMENT
$previousRabbit = $env:NEXACONNECT_RABBITMQ_ACCEPTANCE
$previousMigration = $env:NEXACONNECT_POS_CLEAN_INSTALL_ACCEPTANCE
$previousDpapi = $env:NEXACONNECT_POS_DPAPI_ACCEPTANCE

try {
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    $env:NEXACONNECT_RABBITMQ_ACCEPTANCE = '1'
    $env:NEXACONNECT_POS_CLEAN_INSTALL_ACCEPTANCE = '1'
    $env:NEXACONNECT_POS_DPAPI_ACCEPTANCE = '1'
    if (-not $NoBuild) {
        & dotnet build $unitProject --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'POS Paid client recovery build failed.' }
        & dotnet build $integrationProject --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'POS Paid backend acceptance build failed.' }
    }

    & dotnet test $unitProject --no-build --no-restore --verbosity minimal `
        --filter 'FullyQualifiedName~PosPendingSettlementRecoveryTests|FullyQualifiedName~PosLocalSqliteStoreTests' `
        --logger "trx;LogFileName=$unitTrx"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $unitTrx)) {
        throw 'POS Paid protected-state recovery verification failed.'
    }

    $filter = 'FullyQualifiedName~PosPostgresStoreTests.Order_manual_tenders|FullyQualifiedName~PosPostgresStoreTests.Hosted_consumer|FullyQualifiedName~PosMigrationRunnerAcceptanceTests'
    & dotnet test $integrationProject --no-build --no-restore --verbosity minimal `
        --filter $filter --logger "trx;LogFileName=$integrationTrx"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $integrationTrx)) {
        throw 'POS Paid backend live verification failed.'
    }

    [xml] $unitDocument = Get-Content -LiteralPath $unitTrx -Raw
    [xml] $integrationDocument = Get-Content -LiteralPath $integrationTrx -Raw
    $unitCounters = $unitDocument.TestRun.ResultSummary.Counters
    $integrationCounters = $integrationDocument.TestRun.ResultSummary.Counters
    if ([int] $unitCounters.total -ne 14 -or [int] $unitCounters.passed -ne 14 -or
        [int] $integrationCounters.total -ne 3 -or [int] $integrationCounters.passed -ne 3) {
        throw 'POS Paid evidence is incomplete; expected 14 protected SQLite/recovery and 3 backend cases.'
    }

    [ordered]@{
        runId = Split-Path $run -Leaf
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        testsPassed = 17
        localSqliteRecoveryPassed = $true
        protectedStateRecoveryPassed = $true
        postgresProjectionPassed = $true
        rabbitMqRecoveryPassed = $true
        migrationLifecycle = '0-4-3-4'
        secretsPrinted = $false
        interactiveWpfVerified = $false
        liveOidcVerified = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'evidence.json') -Encoding utf8
    Write-Output "POS Paid workflow acceptance passed. Sanitized evidence retained at '$run'."
}
finally {
    $env:NEXACONNECT_ENVIRONMENT = $previousEnvironment
    $env:NEXACONNECT_RABBITMQ_ACCEPTANCE = $previousRabbit
    $env:NEXACONNECT_POS_CLEAN_INSTALL_ACCEPTANCE = $previousMigration
    $env:NEXACONNECT_POS_DPAPI_ACCEPTANCE = $previousDpapi
}
