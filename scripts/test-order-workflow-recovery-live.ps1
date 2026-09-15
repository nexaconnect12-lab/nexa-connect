#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $ConfirmDisposableInfrastructure,
    [string] $DockerExecutable = 'docker'
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmDisposableInfrastructure) {
    throw 'Pass -ConfirmDisposableInfrastructure to authorize generated PostgreSQL/RabbitMQ resources, destructive migration tests, controlled Order child-process termination, and project-scoped cleanup.'
}

function Assert-GeneratedProject([string] $Name) {
    if ($Name -cnotmatch '^nexa-order-recovery-it-[a-f0-9]{32}$') {
        throw 'Order recovery acceptance requires a generated project identity.'
    }
}

function ConvertFrom-LoopbackPort([string] $PublishedAddress) {
    if ($PublishedAddress -cnotmatch '^127\.0\.0\.1:(\d{1,5})$') {
        throw 'Acceptance services must publish only an IPv4-loopback port.'
    }
    $port = [int] $Matches[1]
    if ($port -lt 1024 -or $port -gt 65535) { throw 'Acceptance service port is outside the allowed range.' }
    return $port
}

function New-AcceptanceConnection([int] $Port, [string] $Password) {
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder['Host'] = '127.0.0.1'
    $builder['Port'] = $Port
    $builder['Database'] = 'order_recovery'
    $builder['Username'] = 'postgres'
    $builder['Password'] = $Password
    return $builder.ConnectionString
}

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runId = [Guid]::NewGuid().ToString('N')
$project = 'nexa-order-recovery-it-' + $runId
Assert-GeneratedProject $project
$composeDirectory = Join-Path $root 'docker/order-recovery-acceptance'
$composeArguments = @('compose', '--env-file', (Join-Path $composeDirectory '.env.example'), '-f', (Join-Path $composeDirectory 'compose.yaml'), '-p', $project)
$runRoot = Join-Path $root ('.runstate/order-workflow-recovery-live/' + $runId)
$hostOutput = Join-Path $runRoot 'order-host/'
$testOutput = Join-Path $runRoot 'integration-bin/'
$trxPath = Join-Path $runRoot 'order-workflow-recovery-live.trx'
$detailPath = Join-Path $runRoot 'recovery-evidence.json'
$summaryPath = Join-Path $runRoot 'summary.json'
$orderProject = Join-Path $root 'src/Services/NexaConnect.Services.Order/NexaConnect.Services.Order.csproj'
$integrationProject = Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$environmentNames = @(
    'NEXACONNECT_ORDER_RECOVERY_ACCEPTANCE_PASSWORD',
    'NEXACONNECT_ORDER_INTEGRATION_DB',
    'NEXACONNECT_RABBITMQ_INTEGRATION_URI',
    'NEXACONNECT_ORDER_RECOVERY_HOST_DLL',
    'NEXACONNECT_ORDER_RECOVERY_EVIDENCE_PATH',
    'NEXACONNECT_ORDER_RECOVERY_LIVE_ACCEPTANCE',
    'NEXACONNECT_ENVIRONMENT'
)
$previous = @{}
foreach ($name in $environmentNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$created = $false
$matrixPassed = $false
$cleanupPassed = $false

try {
    $endpoint = $env:DOCKER_HOST
    if ([string]::IsNullOrWhiteSpace($endpoint)) {
        $endpoint = & $DockerExecutable context inspect --format '{{.Endpoints.docker.Host}}'
        if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the Docker context.' }
    }
    $endpoint = ([string] $endpoint).Trim()
    $localNamedPipe = $endpoint -match '^npipe:////\./pipe/[A-Za-z0-9._-]+$'
    $localUnixSocket = $endpoint -match '^unix:///'
    if (-not $localNamedPipe -and -not $localUnixSocket) {
        throw 'Order recovery acceptance requires a local Docker socket.'
    }

    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $env:NEXACONNECT_ORDER_RECOVERY_ACCEPTANCE_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $existing = @(& $DockerExecutable @composeArguments ps -aq)
    if ($LASTEXITCODE -ne 0 -or $existing.Count -ne 0) {
        throw 'Generated Order recovery acceptance project is not empty; refusing reuse.'
    }
    $created = $true
    & $DockerExecutable @composeArguments up -d --wait --wait-timeout 120
    if ($LASTEXITCODE -ne 0) { throw 'Disposable Order recovery infrastructure did not become healthy.' }

    $postgresPort = ConvertFrom-LoopbackPort (& $DockerExecutable @composeArguments port postgres 5432)
    if ($LASTEXITCODE -ne 0) { throw 'Could not discover the disposable PostgreSQL port.' }
    $rabbitPort = ConvertFrom-LoopbackPort (& $DockerExecutable @composeArguments port rabbitmq 5672)
    if ($LASTEXITCODE -ne 0) { throw 'Could not discover the disposable RabbitMQ port.' }

    $env:NEXACONNECT_ORDER_INTEGRATION_DB = New-AcceptanceConnection $postgresPort $env:NEXACONNECT_ORDER_RECOVERY_ACCEPTANCE_PASSWORD
    $env:NEXACONNECT_RABBITMQ_INTEGRATION_URI = 'amqp://acceptance:' + $env:NEXACONNECT_ORDER_RECOVERY_ACCEPTANCE_PASSWORD + '@127.0.0.1:' + $rabbitPort + '/'
    $env:NEXACONNECT_ORDER_RECOVERY_LIVE_ACCEPTANCE = '1'
    $env:NEXACONNECT_ORDER_RECOVERY_EVIDENCE_PATH = $detailPath
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'

    & dotnet build $orderProject --configuration Release --no-restore --verbosity minimal "-p:OutputPath=$hostOutput"
    if ($LASTEXITCODE -ne 0) { throw 'Order recovery acceptance host build failed.' }
    & dotnet build $integrationProject --configuration Release --no-restore --verbosity minimal "-p:OutputPath=$testOutput" -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Order recovery acceptance test build failed.' }
    $env:NEXACONNECT_ORDER_RECOVERY_HOST_DLL = Join-Path $hostOutput 'NexaConnect.Services.Order.dll'
    if (-not (Test-Path -LiteralPath $env:NEXACONNECT_ORDER_RECOVERY_HOST_DLL)) {
        throw 'The isolated Order host assembly was not produced.'
    }

    & dotnet test $integrationProject --configuration Release --no-build --no-restore --verbosity minimal `
        "-p:OutputPath=$testOutput" `
        --filter 'FullyQualifiedName~OrderWorkflowRecoveryLiveAcceptanceTests' `
        --logger "trx;LogFileName=$trxPath"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $trxPath) -or -not (Test-Path -LiteralPath $detailPath)) {
        throw 'Order workflow recovery live acceptance failed or produced incomplete evidence.'
    }
    [xml] $document = Get-Content -LiteralPath $trxPath -Raw
    $counters = $document.TestRun.ResultSummary.Counters
    if ([int] $counters.total -ne 1 -or [int] $counters.passed -ne 1 -or [int] $counters.failed -ne 0 -or [int] $counters.notExecuted -ne 0) {
        throw 'Order recovery evidence is incomplete; expected one executed and passing live matrix.'
    }
    $matrixPassed = $true
}
finally {
    try {
        if ($created) {
            Assert-GeneratedProject $project
            & $DockerExecutable @composeArguments down --volumes --remove-orphans
            if ($LASTEXITCODE -ne 0) { throw "Acceptance cleanup failed for generated project $project." }
            $remaining = @(& $DockerExecutable @composeArguments ps -aq)
            if ($LASTEXITCODE -ne 0 -or $remaining.Count -ne 0) {
                throw "Acceptance cleanup could not verify generated project $project is empty."
            }
            $cleanupPassed = $true
        }
    }
    finally {
        foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
        if (Test-Path -LiteralPath $runRoot) {
            [ordered]@{
                runId = $runId
                completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
                matrixPassed = $matrixPassed
                cleanupPassed = $cleanupPassed
                orderProcessInterruptions = 2
                retainedSecrets = $false
                rawServiceLogsRetained = $false
                detailEvidence = 'recovery-evidence.json'
                trxEvidence = 'order-workflow-recovery-live.trx'
            } | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding utf8
        }
    }
}

if (-not $matrixPassed -or -not $cleanupPassed) { throw 'Order recovery acceptance did not complete both verification and cleanup.' }
Write-Output "Order workflow recovery live acceptance passed. Sanitized evidence retained at '$runRoot'."
