[CmdletBinding()]
param(
    [string] $EvidenceLabel = 'unclassified-disposable-environment',
    [switch] $ConfirmDisposableInfrastructure,
    [switch] $ConfirmAlertDelivery,
    [switch] $ConfirmDestructiveRollback,
    [string] $DockerExecutable = 'docker',
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmDisposableInfrastructure -or -not $ConfirmAlertDelivery -or -not $ConfirmDestructiveRollback) {
    throw 'Pass all three confirmation switches after verifying disposable PostgreSQL/RabbitMQ infrastructure and authorizing isolated alert delivery plus destructive rollback rehearsal.'
}

$required = @('NEXACONNECT_ORDER_INTEGRATION_DB','NEXACONNECT_REPORTING_INTEGRATION_DB','NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB','NEXACONNECT_RABBITMQ_INTEGRATION_URI')
foreach ($name in $required) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing acceptance setting: $name. Inject it without printing its value." }
    if ($value -match '(?i)(^|[;:/@._-])(prod|production)([;:/@._-]|$)') { throw "Refusing $name because it appears to identify production infrastructure." }
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repositoryRoot 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$runRoot = Join-Path $repositoryRoot ('.runstate/payment-review-operations/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$trxPath = Join-Path $runRoot 'payment-review-live-verification.trx'
$previousEnvironment = $env:NEXACONNECT_ENVIRONMENT
$previousOrderAcceptance = $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE
$previousRabbitAcceptance = $env:NEXACONNECT_RABBITMQ_ACCEPTANCE
$cleanupRequired = $false

function Wait-Until([scriptblock] $Condition, [int] $TimeoutSeconds, [string] $FailureMessage) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 500
    }
    throw $FailureMessage
}

function Get-RehearsalEvents {
    try { return @(Invoke-RestMethod -Uri 'http://127.0.0.1:19094/events' -TimeoutSec 2) } catch { return @() }
}

function Remove-RehearsalContainers {
    $containerIds = @(& $DockerExecutable compose --profile alert-rehearsal ps -aq alertmanager-rehearsal alert-webhook-receiver)
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect isolated alert-rehearsal containers.' }
    $containerIds = @($containerIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($containerIds.Count -eq 0) { return }

    & $DockerExecutable rm --force @containerIds 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Docker could not remove the isolated rehearsal container IDs: $($containerIds -join ', ')." }

    $remainingIds = @(& $DockerExecutable compose --profile alert-rehearsal ps -aq alertmanager-rehearsal alert-webhook-receiver)
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify isolated alert-rehearsal cleanup.' }
    if (@($remainingIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -ne 0) {
        throw 'One or more isolated alert-rehearsal containers remain after Docker removal.'
    }
}

try {
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE = '1'
    $env:NEXACONNECT_RABBITMQ_ACCEPTANCE = '1'
    if (-not $NoBuild) {
        & dotnet build $project --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Payment-review acceptance build failed.' }
    }

    $cleanupRequired = $true
    Remove-RehearsalContainers
    & $DockerExecutable compose --profile alert-rehearsal config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'The alert-rehearsal Compose profile is invalid.' }
    & $DockerExecutable compose --profile alert-rehearsal up -d alert-webhook-receiver alertmanager-rehearsal
    if ($LASTEXITCODE -ne 0) { throw 'The isolated Alertmanager rehearsal services failed to start.' }
    Wait-Until { try { (Invoke-WebRequest -Uri 'http://127.0.0.1:19094/health' -TimeoutSec 2).StatusCode -eq 200 } catch { $false } } 60 'The alert webhook receiver did not become healthy.'
    Wait-Until { try { (Invoke-WebRequest -Uri 'http://127.0.0.1:19093/-/ready' -TimeoutSec 2).StatusCode -eq 200 } catch { $false } } 60 'The isolated Alertmanager did not become ready.'

    $rehearsalId = [Guid]::NewGuid().ToString('N')
    $labels = @{ alertname='OrderPaymentReviewStale'; service='nexaconnect-order'; severity='warning'; rehearsal_id=$rehearsalId }
    $firing = @(@{ labels=$labels; annotations=@{ summary='Synthetic payment-review stale alert delivery rehearsal' }; startsAt=[DateTimeOffset]::UtcNow.ToString('O') })
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:19093/api/v2/alerts' -ContentType 'application/json' -Body (ConvertTo-Json -InputObject $firing -Depth 6) | Out-Null
    Wait-Until { @(Get-RehearsalEvents | Where-Object { $_.status -eq 'firing' -and $_.alerts[0].alertname -eq 'OrderPaymentReviewStale' -and $_.alerts[0].rehearsal_id -eq $rehearsalId }).Count -gt 0 } 30 'Alertmanager did not deliver the payment-review firing notification.'
    $resolved = @(@{ labels=$labels; annotations=@{ summary='Synthetic payment-review stale alert delivery rehearsal' }; startsAt=[DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O'); endsAt=[DateTimeOffset]::UtcNow.AddSeconds(-1).ToString('O') })
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:19093/api/v2/alerts' -ContentType 'application/json' -Body (ConvertTo-Json -InputObject $resolved -Depth 6) | Out-Null
    Wait-Until { @(Get-RehearsalEvents | Where-Object { $_.status -eq 'resolved' -and $_.alerts[0].alertname -eq 'OrderPaymentReviewStale' -and $_.alerts[0].rehearsal_id -eq $rehearsalId }).Count -gt 0 } 30 'Alertmanager did not deliver the payment-review resolved notification.'

    $filter = 'FullyQualifiedName~OrderMigrationRunnerAcceptanceTests|FullyQualifiedName~ReportingActivityVocabularyPostgresTests.Migration_13|FullyQualifiedName~ReportingActivityVocabularyPostgresTests.Hosted_consumer|FullyQualifiedName~OrderOutboxReplayPersistenceTests.Repository_payment_review_events'
    & dotnet test $project --no-build --no-restore --verbosity minimal --filter $filter --logger "trx;LogFileName=$trxPath"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $trxPath)) { throw 'Payment-review live verification failed.' }
    [xml] $trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -ne 4 -or [int]$counters.passed -ne 4 -or [int]$counters.notExecuted -ne 0) {
        throw "Payment-review evidence was incomplete: total=$($counters.total), passed=$($counters.passed), notExecuted=$($counters.notExecuted)."
    }
    Write-Output "Payment-review PostgreSQL, Reporting replay, RabbitMQ recovery, and alert delivery verification passed (operator evidence label: '$EvidenceLabel')."
    Write-Output 'The evidence label is descriptive only; the script does not validate environment identity or prove that resources belong to a named release environment.'
    Write-Output "Evidence retained at '$trxPath'. The webhook is isolated; production receiver authentication, paging, acknowledgement, and threshold calibration remain environment-owned."
}
finally {
    $cleanupFailed = $false
    $cleanupError = $null
    if ($cleanupRequired) {
        try { Remove-RehearsalContainers } catch { $cleanupFailed = $true; $cleanupError = $_.Exception.Message; Write-Warning $cleanupError }
    }
    $env:NEXACONNECT_ENVIRONMENT = $previousEnvironment
    $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE = $previousOrderAcceptance
    $env:NEXACONNECT_RABBITMQ_ACCEPTANCE = $previousRabbitAcceptance
    if ($cleanupFailed) { throw "Isolated alert cleanup failed: $cleanupError Inspect '$runRoot' and the Compose state." }
}
