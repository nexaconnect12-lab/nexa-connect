#Requires -Version 7.0
[CmdletBinding()]
param([switch] $ConfirmSandboxTransactions, [decimal] $Amount = 50.00)
$ErrorActionPreference = 'Stop'
if (-not $ConfirmSandboxTransactions) { throw 'Omise acceptance creates two test authorizations, captures one and reverses one. Supply -ConfirmSandboxTransactions.' }
if ($Amount -le 0 -or $Amount -gt 10000 -or [decimal]::Truncate($Amount*100) -ne $Amount*100) { throw 'Use an exact two-decimal THB amount between 0.01 and 10000.' }
if ($env:NEXACONNECT_OMISE_TEST_SECRET_KEY -cnotmatch '^skey_test_[a-z0-9]{10,64}$') { throw 'Inject NEXACONNECT_OMISE_TEST_SECRET_KEY without printing it. Live keys are rejected.' }
foreach ($name in @('NEXACONNECT_OMISE_CAPTURE_TEST_TOKEN','NEXACONNECT_OMISE_VOID_TEST_TOKEN')) {
    if ([Environment]::GetEnvironmentVariable($name) -cnotmatch '^tokn_test_[a-z0-9]{10,64}$') { throw "Inject a fresh test token in $name without printing it." }
}
if ($env:NEXACONNECT_OMISE_CAPTURE_TEST_TOKEN -ceq $env:NEXACONNECT_OMISE_VOID_TEST_TOKEN) { throw 'Two distinct unused test tokens are required.' }
$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$runRoot = Join-Path $repository ('.runstate/payment-omise-live/' + [Guid]::NewGuid().ToString('N'))
$names = @('NEXACONNECT_ENVIRONMENT','NEXACONNECT_OMISE_SANDBOX_ACCEPTANCE','NEXACONNECT_OMISE_SANDBOX_AMOUNT','NEXACONNECT_OMISE_SANDBOX_EVIDENCE')
$saved = @{}; foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $buildLog = Join-Path $runRoot 'build.log'
    & dotnet build $project --no-restore --verbosity minimal 2>&1 | Tee-Object -FilePath $buildLog
    if ($LASTEXITCODE -ne 0) { throw "Omise acceptance build failed before any provider request. Inspect '$buildLog'." }
    $env:NEXACONNECT_ENVIRONMENT='Testing'; $env:NEXACONNECT_OMISE_SANDBOX_ACCEPTANCE='1'
    $env:NEXACONNECT_OMISE_SANDBOX_AMOUNT=$Amount.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:NEXACONNECT_OMISE_SANDBOX_EVIDENCE=Join-Path $runRoot 'summary.json'
    & dotnet test $project --no-build --no-restore --filter 'FullyQualifiedName~OmisePaymentProviderLiveSandboxTests' --logger 'trx;LogFileName=acceptance.trx' --results-directory $runRoot --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Omise sandbox acceptance failed. Do not retry uncertain operations; inspect test-account charges and use fresh tokens for a new run.' }
    [xml] $results = Get-Content -LiteralPath (Join-Path $runRoot 'acceptance.trx') -Raw
    $counters=$results.TestRun.ResultSummary.Counters
    if ([int]$counters.executed -ne 1 -or [int]$counters.passed -ne 1 -or -not (Test-Path -LiteralPath $env:NEXACONNECT_OMISE_SANDBOX_EVIDENCE)) { throw 'Expected exactly one unskipped passing Omise acceptance test and sanitized summary.' }
    Remove-Item -LiteralPath (Join-Path $runRoot 'acceptance.trx') -Force
    Write-Output "Omise test-account acceptance passed. Sanitized evidence retained at '$runRoot'."
} finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') } }
