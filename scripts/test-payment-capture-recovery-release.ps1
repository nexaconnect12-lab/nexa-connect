[CmdletBinding()]
param(
    [ValidateSet('Development', 'Testing', 'Staging')]
    [string] $TargetEnvironment = 'Staging',

    [switch] $ConfirmDisposableInfrastructure,

    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmDisposableInfrastructure) {
    throw 'Pass -ConfirmDisposableInfrastructure after verifying the configured PostgreSQL databases and RabbitMQ virtual host are non-production acceptance resources.'
}

$required = @(
    'NEXACONNECT_PAYMENT_INTEGRATION_DB',
    'NEXACONNECT_REPORTING_INTEGRATION_DB',
    'NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB',
    'NEXACONNECT_RABBITMQ_INTEGRATION_URI'
)

$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    throw "Missing acceptance settings: $($missing -join ', '). Provide them through the process environment or a secret-injection mechanism."
}

$rabbitUri = $null
if (-not [Uri]::TryCreate($env:NEXACONNECT_RABBITMQ_INTEGRATION_URI, [UriKind]::Absolute, [ref] $rabbitUri) -or
    $rabbitUri.Scheme -notin @('amqp', 'amqps')) {
    throw 'NEXACONNECT_RABBITMQ_INTEGRATION_URI must be an absolute amqp or amqps URI.'
}

foreach ($key in $required) {
    $value = [Environment]::GetEnvironmentVariable($key)
    if ($value -match '(?i)(^|[;:/@._-])(prod|production)([;:/@._-]|$)') {
        throw "Refusing to use $key because it appears to identify production infrastructure."
    }
}

$previousEnvironment = $env:NEXACONNECT_ENVIRONMENT
$previousPaymentAcceptance = $env:NEXACONNECT_PAYMENT_CLEAN_INSTALL_ACCEPTANCE
$previousOrderAcceptance = $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE
$previousRabbitAcceptance = $env:NEXACONNECT_RABBITMQ_ACCEPTANCE

try {
    # The tests accept only explicit safe test environment names. TargetEnvironment is retained
    # for operator evidence while the test process receives the safety-gated Testing value.
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    $env:NEXACONNECT_PAYMENT_CLEAN_INSTALL_ACCEPTANCE = '1'
    $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE = '1'
    $env:NEXACONNECT_RABBITMQ_ACCEPTANCE = '1'

    $repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    $integrationProject = Join-Path $repositoryRoot 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
    $unitProject = Join-Path $repositoryRoot 'tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj'
    $commonArguments = @('--configuration', 'Debug', '--verbosity', 'minimal')
    if ($NoBuild) { $commonArguments += '--no-build' }

    $integrationFilter = 'FullyQualifiedName~PaymentPostgresIntegrationTests|FullyQualifiedName~PaymentMigrationRunnerAcceptanceTests|FullyQualifiedName~OrderMigrationRunnerAcceptanceTests|FullyQualifiedName~ReportingActivityVocabularyPostgresTests'
    & dotnet test $integrationProject @commonArguments --filter $integrationFilter
    if ($LASTEXITCODE -ne 0) { throw 'The coordinated Payment capture-recovery integration matrix failed.' }

    & dotnet test $unitProject @commonArguments --filter 'FullyQualifiedName~OrderPaymentCaptureReconciliationTests'
    if ($LASTEXITCODE -ne 0) { throw 'The Payment reconciliation compensation unit matrix failed.' }

    Write-Output "Payment capture-recovery release verification passed for target environment '$TargetEnvironment'."
    Write-Output 'Provider-connected process termination, established-connection broker restart, and alert-delivery evidence must be recorded separately for this environment.'
}
finally {
    $env:NEXACONNECT_ENVIRONMENT = $previousEnvironment
    $env:NEXACONNECT_PAYMENT_CLEAN_INSTALL_ACCEPTANCE = $previousPaymentAcceptance
    $env:NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE = $previousOrderAcceptance
    $env:NEXACONNECT_RABBITMQ_ACCEPTANCE = $previousRabbitAcceptance
}
