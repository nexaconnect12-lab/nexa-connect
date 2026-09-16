#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $ConfirmDisposableInfrastructure,
    [switch] $ConfirmProcessTermination,
    [switch] $ConfirmSandboxTransactions,
    [string] $DockerExecutable = 'docker'
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) {
    throw 'Provider recovery acceptance currently requires Windows for exact process-tree termination.'
}
if (-not $ConfirmDisposableInfrastructure -or -not $ConfirmProcessTermination -or -not $ConfirmSandboxTransactions) {
    throw 'Pass all three confirmation switches after verifying disposable Docker resources, authorizing termination of exact harness process trees, and confirming the provider endpoint accepts disposable sandbox authorization/capture transactions.'
}

$providerSettings = @(
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL',
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY',
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT',
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY'
)
foreach ($name in $providerSettings) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Missing provider recovery setting: $name. Inject it without printing its value."
    }
}
$sandboxAmount = [decimal] 0
if (-not [decimal]::TryParse($env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT,
        [Globalization.NumberStyles]::Number, [Globalization.CultureInfo]::InvariantCulture, [ref] $sandboxAmount) -or
    $sandboxAmount -le 0) {
    throw 'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT must be a positive invariant-culture decimal.'
}
if ($env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY -cnotmatch '^[A-Z]{3}$') {
    throw 'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY must be an uppercase three-letter currency code.'
}
$providerUri = [Uri] $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL
if ($providerUri.Scheme -ne 'https' -or -not [string]::IsNullOrEmpty($providerUri.UserInfo) -or
    -not [string]::IsNullOrEmpty($providerUri.Query) -or -not [string]::IsNullOrEmpty($providerUri.Fragment) -or
    $providerUri.Host -match '(?i)(^|[.\-_])(prod|production)([.\-_]|$)') {
    throw 'Provider recovery requires a non-production HTTPS sandbox URL without user information, query, or fragment.'
}

function Assert-GeneratedProject([string] $Name) {
    if ($Name -cnotmatch '^nexa-order-provider-recovery-it-[a-f0-9]{32}$') {
        throw 'Provider recovery acceptance requires a generated Compose project identity.'
    }
}

function ConvertFrom-LoopbackPort([string] $PublishedAddress) {
    if ($PublishedAddress -cnotmatch '^127\.0\.0\.1:(\d{1,5})$') { throw 'Acceptance ports must bind only to IPv4 loopback.' }
    $port = [int] $Matches[1]
    if ($port -lt 1024 -or $port -gt 65535) { throw 'Acceptance port is outside the allowed range.' }
    return $port
}

function New-PostgresConnection([int] $Port, [string] $Database, [string] $Password) {
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder['Host'] = '127.0.0.1'; $builder['Port'] = $Port; $builder['Database'] = $Database
    $builder['Username'] = 'postgres'; $builder['Password'] = $Password
    return $builder.ConnectionString
}

function Wait-ForMarker([string] $Path, [Diagnostics.Process] $Process, [int] $TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path) { return }
        if ($Process.HasExited) { throw "Provider recovery arm process exited before its marker for scenario '$env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO'." }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for provider recovery marker for scenario '$env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO'."
}

function Wait-ForMarkerPhase([string] $Path, [string] $Phase, [Diagnostics.Process] $Process, [int] $TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) { throw "Hosted recovery process exited before phase '$Phase' for '$env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO'." }
        if (Test-Path -LiteralPath $Path) {
            try {
                $marker = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
                if ($marker.phase -eq $Phase) { return $marker }
            } catch { }
        }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for hosted recovery phase '$Phase' for '$env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO'."
}

function Write-ControlPhase([string] $Path, [string] $Phase) {
    $temporary = $Path + '.tmp'
    [ordered]@{phase=$Phase} | ConvertTo-Json -Compress | Set-Content -LiteralPath $temporary -Encoding utf8
    [IO.File]::Move($temporary, $Path, $true)
}

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runId = [Guid]::NewGuid().ToString('N')
$composeProject = 'nexa-order-provider-recovery-it-' + $runId
Assert-GeneratedProject $composeProject
$composeDirectory = Join-Path $root 'docker/order-provider-recovery-acceptance'
$composeArguments = @('compose','--env-file',(Join-Path $composeDirectory '.env.example'),'-f',(Join-Path $composeDirectory 'compose.yaml'),'-p',$composeProject)
$runRoot = Join-Path $root ('.runstate/order-provider-recovery-live/' + $runId)
$testOutput = Join-Path $runRoot 'integration-bin/'
$orderOutput = Join-Path $runRoot 'order-host/'
$paymentOutput = Join-Path $runRoot 'payment-host/'
$evidencePath = Join-Path $runRoot 'provider-recovery-evidence.json'
$summaryPath = Join-Path $runRoot 'summary.json'
$integrationProject = Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$orderProject = Join-Path $root 'src/Services/NexaConnect.Services.Order/NexaConnect.Services.Order.csproj'
$paymentProject = Join-Path $root 'src/Services/NexaConnect.Services.Payment/NexaConnect.Services.Payment.csproj'
$markerPath = Join-Path $runRoot 'process-boundary.marker'
$controlPath = Join-Path $runRoot 'hosted-recovery.control'
$activeProcess = $null
$created = $false; $matrixPassed = $false; $cleanupPassed = $false
$providerBoundaryInterruptions = 0; $paymentHostInterruptions = 0

$environmentNames = @(
    'NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_PASSWORD',
    'NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_RABBIT_PORT',
    'NEXACONNECT_ENVIRONMENT','NEXACONNECT_ORDER_PROVIDER_RECOVERY_LIVE_ACCEPTANCE',
    'NEXACONNECT_ORDER_PROVIDER_RECOVERY_STAGE','NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO',
    'NEXACONNECT_ORDER_PROVIDER_RECOVERY_MARKER','NEXACONNECT_ORDER_PROVIDER_RECOVERY_CONTROL',
    'NEXACONNECT_ORDER_PROVIDER_RECOVERY_EVIDENCE',
    'NEXACONNECT_ORDER_PROVIDER_RECOVERY_ORDER_DB','NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_DB',
    'NEXACONNECT_ORDER_PROVIDER_RECOVERY_RABBITMQ','NEXACONNECT_ORDER_PROVIDER_RECOVERY_HOST_DLL',
    'NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_HOST_DLL'
)
$saved = @{}
foreach ($name in $environmentNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }

function Invoke-Stage([string] $Method, [string] $Stage) {
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_STAGE = $Stage
    & dotnet test $integrationProject --configuration Release --no-build --no-restore --verbosity minimal `
        "-p:OutputPath=$testOutput" --filter "FullyQualifiedName~$Method"
    if ($LASTEXITCODE -ne 0) { throw "Provider recovery stage '$Stage' failed for '$env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO'." }
}

try {
    $endpoint = $env:DOCKER_HOST
    if ([string]::IsNullOrWhiteSpace($endpoint)) {
        $endpoint = & $DockerExecutable context inspect --format '{{.Endpoints.docker.Host}}'
        if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the Docker context.' }
    }
    $endpoint = ([string] $endpoint).Trim()
    if ($endpoint -notmatch '^npipe:////\./pipe/[A-Za-z0-9._-]+$' -and $endpoint -notmatch '^unix:///') {
        throw 'Provider recovery acceptance requires a local Docker socket.'
    }

    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $rabbitListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    try {
        $rabbitListener.Start()
        $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_RABBIT_PORT = [string]$rabbitListener.LocalEndpoint.Port
    } finally { $rabbitListener.Stop() }
    $existing = @(& $DockerExecutable @composeArguments ps -aq)
    if ($LASTEXITCODE -ne 0) { throw 'Docker is unavailable while checking the generated provider recovery project.' }
    if ($existing.Count -ne 0) { throw 'Generated provider recovery project is not empty; refusing reuse.' }
    $created = $true
    & $DockerExecutable @composeArguments up -d --wait --wait-timeout 120
    if ($LASTEXITCODE -ne 0) { throw 'Disposable provider recovery infrastructure did not become healthy.' }

    $postgresPort = ConvertFrom-LoopbackPort (& $DockerExecutable @composeArguments port postgres 5432)
    $rabbitPort = ConvertFrom-LoopbackPort (& $DockerExecutable @composeArguments port rabbitmq 5672)
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_ORDER_DB = New-PostgresConnection $postgresPort 'order_provider_recovery' $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_PASSWORD
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_DB = New-PostgresConnection $postgresPort 'payment_provider_recovery' $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_PASSWORD
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_RABBITMQ = 'amqp://acceptance:' + $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_PASSWORD + '@127.0.0.1:' + $rabbitPort + '/'
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_LIVE_ACCEPTANCE = '1'
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_MARKER = $markerPath
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_CONTROL = $controlPath
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_EVIDENCE = $evidencePath
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'

    & dotnet build $orderProject --configuration Release --no-restore --verbosity minimal "-p:OutputPath=$orderOutput"
    if ($LASTEXITCODE -ne 0) { throw 'Provider recovery Order host build failed.' }
    & dotnet build $paymentProject --configuration Release --no-restore --verbosity minimal "-p:OutputPath=$paymentOutput"
    if ($LASTEXITCODE -ne 0) { throw 'Provider recovery Payment host build failed.' }
    & dotnet build $integrationProject --configuration Release --no-restore --verbosity minimal "-p:OutputPath=$testOutput" -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Provider recovery integration build failed.' }
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_HOST_DLL = Join-Path $orderOutput 'NexaConnect.Services.Order.dll'
    if (-not (Test-Path -LiteralPath $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_HOST_DLL)) { throw 'The isolated Order host was not produced.' }
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_HOST_DLL = Join-Path $paymentOutput 'NexaConnect.Services.Payment.dll'
    if (-not (Test-Path -LiteralPath $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_HOST_DLL)) { throw 'The isolated Payment host was not produced.' }

    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO = 'matrix'
    Invoke-Stage 'Initialize_provider_payment_process_interruption_acceptance' 'initialize'

    foreach ($scenario in @('intent_created','authorization_response','capture_response')) {
        $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO = $scenario
        $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_STAGE = 'arm'
        if (Test-Path -LiteralPath $markerPath) { Remove-Item -LiteralPath $markerPath -Force }
        $outLog = Join-Path $runRoot "$scenario-arm.out.log"; $errLog = Join-Path $runRoot "$scenario-arm.err.log"
        $arguments = @('test',$integrationProject,'--configuration','Release','--no-build','--no-restore','--verbosity','minimal',"-p:OutputPath=$testOutput",'--filter','FullyQualifiedName~Arm_provider_payment_process_interruption')
        $activeProcess = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput $outLog -RedirectStandardError $errLog
        Wait-ForMarker $markerPath $activeProcess 90
        & taskkill.exe /PID $activeProcess.Id /T /F | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not $activeProcess.WaitForExit(10000)) { throw "Could not terminate and verify the exact '$scenario' harness process tree." }
        $providerBoundaryInterruptions++
        $activeProcess.Dispose(); $activeProcess = $null
        Remove-Item -LiteralPath $markerPath -Force
        if (Test-Path -LiteralPath $controlPath) { Remove-Item -LiteralPath $controlPath -Force }

        $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_STAGE = 'hosted_recover'
        $hostedOutLog = Join-Path $runRoot "$scenario-hosted.out.log"
        $hostedErrLog = Join-Path $runRoot "$scenario-hosted.err.log"
        $hostedArguments = @('test',$integrationProject,'--configuration','Release','--no-build','--no-restore','--verbosity','minimal',"-p:OutputPath=$testOutput",'--filter','FullyQualifiedName~Recover_provider_payment_through_hosted_workers_and_outbox')
        $activeProcess = Start-Process -FilePath 'dotnet' -ArgumentList $hostedArguments -PassThru -WindowStyle Hidden -RedirectStandardOutput $hostedOutLog -RedirectStandardError $hostedErrLog
        Wait-ForMarkerPhase $markerPath 'order_ready' $activeProcess 45 | Out-Null

        & $DockerExecutable @composeArguments stop --timeout 20 rabbitmq
        if ($LASTEXITCODE -ne 0) { throw "Could not stop the generated RabbitMQ container for '$scenario'." }
        Write-ControlPhase $controlPath 'broker_stopped'

        $persisted = Wait-ForMarkerPhase $markerPath 'outbox_persisted' $activeProcess 60
        $paymentProcessId = [int] $persisted.paymentProcessId
        if ($paymentProcessId -le 0) { throw "Hosted recovery did not report a valid Payment process for '$scenario'." }
        & taskkill.exe /PID $paymentProcessId /T /F | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not terminate the exact Payment host process tree for '$scenario'." }
        $paymentHostInterruptions++

        & $DockerExecutable @composeArguments up -d --wait --wait-timeout 120 rabbitmq
        if ($LASTEXITCODE -ne 0) { throw "Could not restart the generated RabbitMQ container for '$scenario'." }
        $restartedRabbitPort = ConvertFrom-LoopbackPort (& $DockerExecutable @composeArguments port rabbitmq 5672)
        if ($restartedRabbitPort -ne $rabbitPort) { throw 'RabbitMQ loopback port changed across restart.' }
        Write-ControlPhase $controlPath 'broker_restarted'
        if (-not $activeProcess.WaitForExit(150000) -or $activeProcess.ExitCode -ne 0) {
            throw "Hosted Payment recovery failed for '$scenario'. Inspect the failed test output at '$hostedOutLog' and '$hostedErrLog'."
        }
        $activeProcess.Dispose(); $activeProcess = $null
        Remove-Item -LiteralPath $markerPath,$controlPath,$outLog,$errLog,$hostedOutLog,$hostedErrLog -Force
    }

    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO = 'matrix'
    Invoke-Stage 'Verify_provider_payment_process_interruption_matrix' 'verify'
    if (-not (Test-Path -LiteralPath $evidencePath)) { throw 'Provider recovery verification did not produce sanitized evidence.' }
    $matrixPassed = $true
}
finally {
    try {
        if ($null -ne $activeProcess -and -not $activeProcess.HasExited) { & taskkill.exe /PID $activeProcess.Id /T /F 2>$null | Out-Null }
        if ($created) {
            Assert-GeneratedProject $composeProject
            & $DockerExecutable @composeArguments down --volumes --remove-orphans
            if ($LASTEXITCODE -ne 0) { throw "Cleanup failed for generated project '$composeProject'." }
            $remaining = @(& $DockerExecutable @composeArguments ps -aq)
            if ($LASTEXITCODE -ne 0 -or $remaining.Count -ne 0) { throw 'Generated provider recovery project was not empty after cleanup.' }
            $cleanupPassed = $true
        }
    }
    finally {
        foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') }
        if (Test-Path -LiteralPath $runRoot) {
            $rawLogsRetained = @(Get-ChildItem -LiteralPath $runRoot -File -Filter '*.log' -ErrorAction SilentlyContinue).Count -gt 0
            [ordered]@{
                runId=$runId; completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
                matrixPassed=$matrixPassed; cleanupPassed=$cleanupPassed
                providerBoundaryInterruptions=$providerBoundaryInterruptions
                paymentHostInterruptions=$paymentHostInterruptions
                hostedPaymentRecoveryVerified=$matrixPassed; transactionalOutboxRestartVerified=$matrixPassed
                providerAuthorizationBoundaryVerified=$matrixPassed; providerCaptureBoundaryVerified=$matrixPassed
                duplicateDurableCommandStartsDetected=if($matrixPassed){$false}else{$null}
                retainedSecrets=$false; rawServiceLogsRetained=$rawLogsRetained
                detailEvidence='provider-recovery-evidence.json'
            } | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding utf8
        }
    }
}

if (-not $matrixPassed -or -not $cleanupPassed) { throw 'Provider recovery acceptance did not complete both verification and cleanup.' }
Write-Output "Order provider-payment recovery live acceptance passed. Sanitized evidence retained at '$runRoot'."
