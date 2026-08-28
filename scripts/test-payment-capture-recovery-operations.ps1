[CmdletBinding()]
param(
    [ValidateSet('Development', 'Testing', 'Staging')]
    [string] $TargetEnvironment = 'Staging',

    [switch] $ConfirmDisposableInfrastructure,
    [switch] $ConfirmAlertDelivery,
    [switch] $ConfirmDestructiveRollback,

    [string] $DockerExecutable = 'docker',
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmDisposableInfrastructure -or -not $ConfirmAlertDelivery -or -not $ConfirmDestructiveRollback) {
    throw 'Pass all three confirmation switches after verifying the administrator target is disposable and authorizing the isolated alert and destructive migration rehearsals.'
}

$adminConnection = $env:NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB
if ([string]::IsNullOrWhiteSpace($adminConnection)) {
    throw 'NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB must be injected without printing its value.'
}
if ($adminConnection -match '(?i)(^|[;:/@._-])(prod|production)([;:/@._-]|$)') {
    throw 'Refusing an administrator setting that appears to identify production infrastructure.'
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$integrationProject = Join-Path $repositoryRoot 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$runRoot = Join-Path $repositoryRoot ('.runstate/payment-capture-operations/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$trxPath = Join-Path $runRoot 'rollback-forward.trx'
$rehearsalCleanupRequired = $false
$completed = $false
$previousEnvironment = $env:NEXACONNECT_ENVIRONMENT
$previousPaymentAcceptance = $env:NEXACONNECT_PAYMENT_CLEAN_INSTALL_ACCEPTANCE
$previousOrderAcceptance = $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE

function Wait-Until([scriptblock] $Condition, [int] $TimeoutSeconds, [string] $FailureMessage) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 500
    }
    throw $FailureMessage
}

function Get-RehearsalEvents {
    try { return @(Invoke-RestMethod -Uri 'http://127.0.0.1:19094/events' -TimeoutSec 2) }
    catch { return @() }
}

try {
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    $env:NEXACONNECT_PAYMENT_CLEAN_INSTALL_ACCEPTANCE = '1'
    $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE = '1'

    if (-not $NoBuild) {
        & dotnet build $integrationProject --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Operational-rehearsal integration build failed.' }
    }

    $rehearsalCleanupRequired = $true
    & $DockerExecutable compose --profile alert-rehearsal rm -sf alertmanager-rehearsal alert-webhook-receiver 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not reset prior isolated alert-rehearsal containers.' }
    & $DockerExecutable compose --profile alert-rehearsal config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'The alert-rehearsal Compose profile is invalid.' }
    & $DockerExecutable compose --profile alert-rehearsal up -d alert-webhook-receiver alertmanager-rehearsal
    if ($LASTEXITCODE -ne 0) { throw 'The isolated Alertmanager rehearsal services failed to start.' }

    Wait-Until { try { (Invoke-WebRequest -Uri 'http://127.0.0.1:19094/health' -TimeoutSec 2).StatusCode -eq 200 } catch { $false } } 60 'The alert webhook receiver did not become healthy.'
    Wait-Until { try { (Invoke-WebRequest -Uri 'http://127.0.0.1:19093/-/ready' -TimeoutSec 2).StatusCode -eq 200 } catch { $false } } 60 'The isolated Alertmanager did not become ready.'

    $rehearsalId = [Guid]::NewGuid().ToString('N')
    $labels = @{ alertname = 'PaymentCaptureRecoveryRehearsal'; service = 'nexaconnect-payment'; severity = 'critical'; rehearsal_id = $rehearsalId }
    $firing = @(@{ labels = $labels; annotations = @{ summary = 'Synthetic capture-recovery delivery rehearsal' }; startsAt = [DateTimeOffset]::UtcNow.ToString('O') })
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:19093/api/v2/alerts' -ContentType 'application/json' -Body ($firing | ConvertTo-Json -Depth 6 -AsArray) | Out-Null
    Wait-Until { @(Get-RehearsalEvents | Where-Object { $_.status -eq 'firing' -and $_.alerts[0].alertname -eq 'PaymentCaptureRecoveryRehearsal' -and $_.alerts[0].rehearsal_id -eq $rehearsalId }).Count -gt 0 } 30 'Alertmanager did not deliver the current synthetic firing notification.'

    $resolved = @(@{ labels = $labels; annotations = @{ summary = 'Synthetic capture-recovery delivery rehearsal' }; startsAt = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O'); endsAt = [DateTimeOffset]::UtcNow.AddSeconds(-1).ToString('O') })
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:19093/api/v2/alerts' -ContentType 'application/json' -Body ($resolved | ConvertTo-Json -Depth 6 -AsArray) | Out-Null
    Wait-Until { @(Get-RehearsalEvents | Where-Object { $_.status -eq 'resolved' -and $_.alerts[0].alertname -eq 'PaymentCaptureRecoveryRehearsal' -and $_.alerts[0].rehearsal_id -eq $rehearsalId }).Count -gt 0 } 30 'Alertmanager did not deliver the current synthetic resolved notification.'

    $filter = 'FullyQualifiedName~PaymentMigrationRunnerAcceptanceTests|FullyQualifiedName~OrderMigrationRunnerAcceptanceTests'
    & dotnet test $integrationProject --no-build --no-restore --verbosity minimal --filter $filter --logger "trx;LogFileName=$trxPath"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $trxPath)) {
        throw 'The disposable Payment/Order rollback-forward matrix failed.'
    }
    [xml] $trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -ne 2 -or [int]$counters.passed -ne 2 -or [int]$counters.notExecuted -ne 0) {
        throw "Rollback-forward evidence was incomplete: total=$($counters.total), passed=$($counters.passed), notExecuted=$($counters.notExecuted)."
    }

    Write-Output "Alert firing/resolution delivery and Payment/Order rollback-forward verification passed for '$TargetEnvironment'."
    Write-Output 'The receiver is an isolated local webhook. Production receiver authentication, escalation, acknowledgement, and paging remain environment-owned evidence.'
    $completed = $true
}
finally {
    $cleanupFailed = $false
    if ($rehearsalCleanupRequired) {
        & $DockerExecutable compose --profile alert-rehearsal rm -sf alertmanager-rehearsal alert-webhook-receiver 2>$null | Out-Null
        $cleanupFailed = $LASTEXITCODE -ne 0
    }
    $env:NEXACONNECT_ENVIRONMENT = $previousEnvironment
    $env:NEXACONNECT_PAYMENT_CLEAN_INSTALL_ACCEPTANCE = $previousPaymentAcceptance
    $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE = $previousOrderAcceptance
    $resolvedRunRoot = [IO.Path]::GetFullPath($runRoot)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.runstate/payment-capture-operations'))
    if ($completed -and -not $cleanupFailed -and $resolvedRunRoot.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    } else {
        Write-Warning "Operational-rehearsal artifacts were retained at '$resolvedRunRoot'."
    }
    if ($cleanupFailed) { throw 'The isolated alert-rehearsal containers could not be removed; inspect the retained artifacts and Compose state.' }
}
