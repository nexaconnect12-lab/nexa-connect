[CmdletBinding()]
param(
    [ValidateSet('Development', 'Testing', 'Staging')]
    [string] $TargetEnvironment = 'Staging',

    [switch] $ConfirmDisposableInfrastructure,
    [switch] $ConfirmProcessTermination,
    [switch] $ConfirmBrokerRestart,

    [string] $RabbitMqContainer = 'nexa-connect-rabbitmq-1',
    [string] $DockerExecutable = 'docker',
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmDisposableInfrastructure -or -not $ConfirmProcessTermination -or -not $ConfirmBrokerRestart) {
    throw 'Pass all three confirmation switches after verifying disposable PostgreSQL/RabbitMQ infrastructure and authorizing termination of the harness process plus restart of the exact RabbitMQ container.'
}

$required = @('NEXACONNECT_PAYMENT_INTEGRATION_DB', 'NEXACONNECT_RABBITMQ_INTEGRATION_URI')
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    throw "Missing fault-rehearsal settings: $($missing -join ', '). Inject them without printing their values."
}

$rabbitUri = $null
if (-not [Uri]::TryCreate($env:NEXACONNECT_RABBITMQ_INTEGRATION_URI, [UriKind]::Absolute, [ref] $rabbitUri) -or
    $rabbitUri.Scheme -notin @('amqp', 'amqps') -or
    $rabbitUri.Host -notin @('127.0.0.1', 'localhost', '::1')) {
    throw 'The automated broker restart rehearsal is restricted to a loopback amqp/amqps URI.'
}

if ($env:NEXACONNECT_PAYMENT_INTEGRATION_DB -match '(?i)(^|[;:/@._-])(prod|production)([;:/@._-]|$)') {
    throw 'Refusing a Payment database setting that appears to identify production infrastructure.'
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$integrationProject = Join-Path $repositoryRoot 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$runRoot = Join-Path $repositoryRoot ('.runstate/payment-capture-faults/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$processMarker = Join-Path $runRoot 'process-kill.json'
$brokerReady = Join-Path $runRoot 'broker-ready'
$brokerContinue = Join-Path $runRoot 'broker-continue'
$activeProcess = $null
$completed = $false

function Wait-ForFile([string] $Path, [System.Diagnostics.Process] $Process, [int] $TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path) { return }
        if ($Process.HasExited) { throw "Fault-rehearsal child exited before marker '$([IO.Path]::GetFileName($Path))'." }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for marker '$([IO.Path]::GetFileName($Path))'."
}

function Start-FilteredTest([string] $Filter, [string] $OutputName) {
    $arguments = @('test', $integrationProject, '--no-build', '--no-restore', '--verbosity', 'minimal', '--filter', $Filter)
    return Start-Process -FilePath 'dotnet' -ArgumentList $arguments -PassThru -NoNewWindow `
        -RedirectStandardOutput (Join-Path $runRoot "$OutputName.out.log") `
        -RedirectStandardError (Join-Path $runRoot "$OutputName.err.log")
}

function Assert-SuccessfulChild([System.Diagnostics.Process] $Process, [string] $OutputName, [int] $TimeoutSeconds) {
    if (-not $Process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "Timed out waiting for $OutputName."
    }
    $output = Get-Content -LiteralPath (Join-Path $runRoot "$OutputName.out.log") -Raw
    if ($Process.ExitCode -ne 0) {
        $errors = Get-Content -LiteralPath (Join-Path $runRoot "$OutputName.err.log") -Raw
        throw "$OutputName failed with exit code $($Process.ExitCode).`n$output`n$errors"
    }
    Write-Output $output.Trim()
}

$saved = @{}
foreach ($name in @('NEXACONNECT_ENVIRONMENT','NEXACONNECT_PAYMENT_PROCESS_KILL_ACCEPTANCE','NEXACONNECT_PAYMENT_PROCESS_KILL_STAGE','NEXACONNECT_PAYMENT_PROCESS_KILL_MARKER','NEXACONNECT_RABBITMQ_ACCEPTANCE','NEXACONNECT_BROKER_RESTART_ACCEPTANCE','NEXACONNECT_BROKER_RESTART_READY_MARKER','NEXACONNECT_BROKER_RESTART_CONTINUE_MARKER')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}

try {
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    if (-not $NoBuild) {
        & dotnet build $integrationProject --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Fault-rehearsal integration build failed.' }
    }

    $env:NEXACONNECT_PAYMENT_PROCESS_KILL_ACCEPTANCE = '1'
    $env:NEXACONNECT_PAYMENT_PROCESS_KILL_STAGE = 'arm'
    $env:NEXACONNECT_PAYMENT_PROCESS_KILL_MARKER = $processMarker
    $activeProcess = Start-FilteredTest 'FullyQualifiedName~Arm_real_http_capture_boundary_for_external_process_kill' 'process-arm'
    Wait-ForFile $processMarker $activeProcess 60
    & taskkill.exe /PID $activeProcess.Id /T /F | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not $activeProcess.WaitForExit(10000)) {
        throw 'The launched capture-boundary process tree could not be terminated and verified within ten seconds.'
    }
    $activeProcess = $null

    $env:NEXACONNECT_PAYMENT_PROCESS_KILL_STAGE = 'recover'
    & dotnet test $integrationProject --no-build --no-restore --verbosity minimal --filter 'FullyQualifiedName~Recover_capture_after_external_process_kill'
    if ($LASTEXITCODE -ne 0) { throw 'Recovery after the externally killed capture process failed.' }
    if (Test-Path -LiteralPath $processMarker) {
        throw 'The recovery stage did not positively complete and remove its process-kill marker.'
    }

    $containerInspectJson = (& $DockerExecutable inspect $RabbitMqContainer)
    if ($LASTEXITCODE -ne 0) { throw "Container '$RabbitMqContainer' could not be inspected." }
    $containerInspect = @($containerInspectJson | ConvertFrom-Json)[0]
    $serviceLabel = $containerInspect.Config.Labels.'com.docker.compose.service'
    $amqpPortKey = "$($rabbitUri.Port)/tcp"
    $amqpBindings = @($containerInspect.NetworkSettings.Ports.$amqpPortKey)
    $matchingBinding = @($amqpBindings | Where-Object {
        $_.HostPort -eq [string]$rabbitUri.Port -and $_.HostIp -in @('127.0.0.1', '::1')
    })
    if ($serviceLabel -ne 'rabbitmq' -or $matchingBinding.Count -ne 1) {
        throw "Container '$RabbitMqContainer' is not the verified Docker Compose rabbitmq service."
    }

    $env:NEXACONNECT_RABBITMQ_ACCEPTANCE = '1'
    $env:NEXACONNECT_BROKER_RESTART_ACCEPTANCE = '1'
    $env:NEXACONNECT_BROKER_RESTART_READY_MARKER = $brokerReady
    $env:NEXACONNECT_BROKER_RESTART_CONTINUE_MARKER = $brokerContinue
    $activeProcess = Start-FilteredTest 'FullyQualifiedName~Established_publisher_recovers_after_full_broker_container_restart' 'broker-restart'
    Wait-ForFile $brokerReady $activeProcess 60
    & $DockerExecutable restart $RabbitMqContainer | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The verified RabbitMQ container restart failed.' }
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        & $DockerExecutable exec $RabbitMqContainer rabbitmq-diagnostics -q ping 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'RabbitMQ did not become ready after the container restart.' }
    New-Item -ItemType File -Path $brokerContinue -Force | Out-Null
    Assert-SuccessfulChild $activeProcess 'broker-restart' 90
    $activeProcess = $null

    Write-Output "Payment process-kill and full RabbitMQ restart verification passed for '$TargetEnvironment'."
    Write-Output 'The provider endpoint was a local HTTP fault fixture; concrete-provider credentials, TLS/rate-limit behavior, and production alert delivery remain separate release evidence.'
    $completed = $true
}
finally {
    if ($null -ne $activeProcess -and -not $activeProcess.HasExited) {
        & taskkill.exe /PID $activeProcess.Id /T /F 2>$null | Out-Null
    }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    $resolvedRunRoot = [IO.Path]::GetFullPath($runRoot)
    $resolvedAllowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.runstate/payment-capture-faults'))
    if ($completed -and $resolvedRunRoot.StartsWith($resolvedAllowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRunRoot)) {
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    } elseif (-not $completed) {
        Write-Warning "Fault-rehearsal artifacts were retained at '$resolvedRunRoot' for diagnosis and cleanup."
    }
}
