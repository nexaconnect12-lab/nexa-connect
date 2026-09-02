[CmdletBinding()]
param(
    [switch] $ConfirmSandboxTransactions,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmSandboxTransactions) {
    throw 'Pass -ConfirmSandboxTransactions after confirming the supplied references are disposable sandbox authorizations and may be captured or voided.'
}

$required = @(
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL',
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY',
    'NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_AUTHORIZATION_ID',
    'NEXACONNECT_PAYMENT_PROVIDER_VOID_AUTHORIZATION_ID',
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT',
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY'
)
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Missing live provider acceptance setting: $name. Inject it without printing its value."
    }
}
$sandboxUri = [Uri]([Environment]::GetEnvironmentVariable('NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL'))
if ($sandboxUri.Scheme -ne 'https') { throw 'The live provider sandbox URL must use HTTPS.' }
if (-not [string]::IsNullOrEmpty($sandboxUri.UserInfo) -or -not [string]::IsNullOrEmpty($sandboxUri.Query) -or -not [string]::IsNullOrEmpty($sandboxUri.Fragment)) {
    throw 'The live provider sandbox URL must not contain user information, a query, or a fragment.'
}
if ($sandboxUri.Host -match '(?i)(^|[.\-_])(prod|production)([.\-_]|$)') {
    throw 'Refusing a payment-provider host that appears to identify production.'
}
$captureAuthorization = [Environment]::GetEnvironmentVariable('NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_AUTHORIZATION_ID')
$voidAuthorization = [Environment]::GetEnvironmentVariable('NEXACONNECT_PAYMENT_PROVIDER_VOID_AUTHORIZATION_ID')
if ([string]::Equals($captureAuthorization, $voidAuthorization, [StringComparison]::Ordinal)) {
    throw 'Capture and void require distinct disposable sandbox authorization references.'
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repositoryRoot 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$runId = [Guid]::NewGuid().ToString('N')
$runRoot = Join-Path $repositoryRoot ('.runstate/payment-provider-live/' + $runId)
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$trxPath = Join-Path $runRoot 'payment-provider-live.trx'
$previousEnvironment = $env:NEXACONNECT_ENVIRONMENT
$previousAcceptance = $env:NEXACONNECT_PAYMENT_PROVIDER_LIVE_ACCEPTANCE
try {
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    $env:NEXACONNECT_PAYMENT_PROVIDER_LIVE_ACCEPTANCE = '1'
    if (-not $NoBuild) {
        & dotnet build $project --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Live payment-provider acceptance build failed.' }
    }
    & dotnet test $project --no-build --no-restore --verbosity minimal `
        --filter 'FullyQualifiedName~PaymentProviderLiveSandboxTests' --logger "trx;LogFileName=$trxPath"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $trxPath)) {
        throw 'Live payment-provider sandbox verification failed.'
    }
    [xml] $trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -ne 1 -or [int]$counters.passed -ne 1 -or [int]$counters.notExecuted -ne 0) {
        throw "Live provider evidence was incomplete: total=$($counters.total), passed=$($counters.passed), notExecuted=$($counters.notExecuted)."
    }
    [ordered]@{
        runId = $runId
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        liveTlsHandshakeVerified = $true
        bearerCredentialAccepted = $true
        captureReplayIdempotencyVerified = $true
        captureStatusReconciliationVerified = $true
        voidReplayIdempotencyVerified = $true
        voidStatusReconciliationVerified = $true
        rateLimitBehaviorVerified = $false
        lostResponseRecoveryVerified = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
    Write-Output "Live payment-provider sandbox verification passed. Sanitized evidence retained at '$runRoot'."
} finally {
    $env:NEXACONNECT_ENVIRONMENT = $previousEnvironment
    $env:NEXACONNECT_PAYMENT_PROVIDER_LIVE_ACCEPTANCE = $previousAcceptance
}
