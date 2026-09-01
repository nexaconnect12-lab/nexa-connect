#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $ConfirmDisposableInfrastructure,
    [string] $DockerExecutable = 'docker',
    [switch] $NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $ConfirmDisposableInfrastructure) { throw 'Confirm creation, destructive test migrations, isolated alert delivery, and deletion of the generated acceptance environment with -ConfirmDisposableInfrastructure.' }
. (Join-Path $PSScriptRoot 'payment-review-acceptance-helpers.ps1')
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runId = [Guid]::NewGuid().ToString('N')
$projectName = 'nexa-review-it-' + $runId
$composeArguments = @(Get-ReviewAcceptanceComposeArguments $repositoryRoot $projectName)
$runRoot = Join-Path $repositoryRoot ('.runstate/payment-review-isolated/' + $runId)
$environmentNames = @('NEXACONNECT_REVIEW_ACCEPTANCE_PASSWORD','NEXACONNECT_ORDER_INTEGRATION_DB','NEXACONNECT_REPORTING_INTEGRATION_DB','NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB','NEXACONNECT_RABBITMQ_INTEGRATION_URI')
$previous = @{}
foreach ($name in $environmentNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$created = $false
$testPassed = $false
$cleanupPassed = $false

try {
    # Refuse remote Docker endpoints: loopback service URLs must refer to this host.
    $endpoint = $env:DOCKER_HOST
    if ([string]::IsNullOrWhiteSpace($endpoint)) {
        $endpoint = & $DockerExecutable context inspect --format '{{.Endpoints.docker.Host}}'
        if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Docker context.' }
    }
    if ($endpoint -notmatch '^(npipe|unix)://') { throw 'Isolated acceptance requires a local Docker socket, not a remote endpoint.' }
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    # Hex credentials need no URI escaping. Values never enter evidence or command arguments.
    $env:NEXACONNECT_REVIEW_ACCEPTANCE_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $existing = @(& $DockerExecutable @composeArguments ps -aq)
    if ($LASTEXITCODE -ne 0 -or $existing.Count -ne 0) { throw 'Generated acceptance project is not empty; refusing to reuse it.' }
    $created = $true
    & $DockerExecutable @composeArguments up -d --wait --wait-timeout 120 postgres rabbitmq
    if ($LASTEXITCODE -ne 0) { throw 'Disposable infrastructure did not become healthy.' }
    $published = & $DockerExecutable @composeArguments port postgres 5432
    if ($LASTEXITCODE -ne 0) { throw 'Could not discover isolated PostgreSQL port.' }
    $postgresPort = ConvertFrom-ReviewAcceptancePort $published
    $published = & $DockerExecutable @composeArguments port rabbitmq 5672
    if ($LASTEXITCODE -ne 0) { throw 'Could not discover isolated RabbitMQ port.' }
    $rabbitPort = ConvertFrom-ReviewAcceptancePort $published
    $env:NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB = New-ReviewAcceptanceConnection $postgresPort 'postgres' $env:NEXACONNECT_REVIEW_ACCEPTANCE_PASSWORD
    $env:NEXACONNECT_ORDER_INTEGRATION_DB = New-ReviewAcceptanceConnection $postgresPort 'review_order' $env:NEXACONNECT_REVIEW_ACCEPTANCE_PASSWORD
    $env:NEXACONNECT_REPORTING_INTEGRATION_DB = New-ReviewAcceptanceConnection $postgresPort 'review_reporting' $env:NEXACONNECT_REVIEW_ACCEPTANCE_PASSWORD
    $env:NEXACONNECT_RABBITMQ_INTEGRATION_URI = 'amqp://acceptance:' + $env:NEXACONNECT_REVIEW_ACCEPTANCE_PASSWORD + '@127.0.0.1:' + $rabbitPort + '/'
    & (Join-Path $PSScriptRoot 'test-payment-review-operations.ps1') -EvidenceLabel $projectName -ConfirmDisposableInfrastructure -ConfirmAlertDelivery -ConfirmDestructiveRollback -DockerExecutable $DockerExecutable -IsolatedProject $projectName -NoBuild:$NoBuild
    $testPassed = $true
}
finally {
    try {
        if ($created) {
            Assert-ReviewAcceptanceProject $projectName
            & $DockerExecutable @composeArguments --profile alert-rehearsal down --volumes --remove-orphans
            if ($LASTEXITCODE -ne 0) { throw "Acceptance cleanup failed for $projectName; retain this exact project identity for recovery." }
            $remaining = @(& $DockerExecutable @composeArguments --profile alert-rehearsal ps -aq)
            if ($LASTEXITCODE -ne 0 -or $remaining.Count -ne 0) { throw "Acceptance cleanup could not be verified for $projectName." }
            $cleanupPassed = $true
        }
    }
    finally {
        foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
        if (Test-Path -LiteralPath $runRoot) {
            [ordered]@{ project=$projectName; completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); matrixPassed=$testPassed; cleanupPassed=$cleanupPassed; liveBrowserVerified=$false; matrixTrx=(Join-Path $runRoot 'operations/payment-review-live-verification.trx') } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json')
            Write-Output "Sanitized acceptance summary: $runRoot"
        }
    }
}
