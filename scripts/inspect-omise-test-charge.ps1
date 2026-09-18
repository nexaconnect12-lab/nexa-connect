#Requires -Version 7.0
[CmdletBinding()]
param([decimal] $ExpectedAmount = 50.00)
$ErrorActionPreference = 'Stop'
if ($ExpectedAmount -le 0 -or $ExpectedAmount -gt 10000 -or [decimal]::Truncate($ExpectedAmount * 100) -ne $ExpectedAmount * 100) {
    throw 'Use an exact two-decimal THB amount between 0.01 and 10000.'
}
$secret = $env:NEXACONNECT_OMISE_TEST_SECRET_KEY
if ([string]::IsNullOrWhiteSpace($secret)) {
    $secret = [Net.NetworkCredential]::new('', (Read-Host 'Omise test secret key' -AsSecureString)).Password.Trim()
}
if ($secret -cnotmatch '^skey_test_[a-z0-9]{10,64}$') { throw 'A test secret key is required. Live keys are rejected.' }
$chargeId = [Net.NetworkCredential]::new('', (Read-Host 'Existing PAID test charge ID (hidden)' -AsSecureString)).Password.Trim()
if ($chargeId -cnotmatch '^chrg_(test_)?[a-z0-9]{10,64}$') { throw 'Enter only the existing charge ID, without a URL or quotes.' }
$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$runRoot = Join-Path $repository ('.runstate/payment-omise-inspection/' + [Guid]::NewGuid().ToString('N'))
$names = @('NEXACONNECT_ENVIRONMENT','NEXACONNECT_OMISE_TEST_SECRET_KEY','NEXACONNECT_OMISE_READ_ONLY_INSPECTION',
    'NEXACONNECT_OMISE_INSPECT_CHARGE_ID','NEXACONNECT_OMISE_INSPECT_AMOUNT','NEXACONNECT_OMISE_INSPECT_REPORT',
    'NEXACONNECT_OMISE_SANDBOX_ACCEPTANCE')
$saved = @{}; foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $buildLog = Join-Path $runRoot 'build.log'
    & dotnet build $project --no-restore --verbosity minimal 2>&1 | Tee-Object -FilePath $buildLog
    if ($LASTEXITCODE -ne 0) { throw "Read-only inspection build failed before any provider request. Inspect '$buildLog'." }
    $env:NEXACONNECT_ENVIRONMENT = 'Testing'
    $env:NEXACONNECT_OMISE_TEST_SECRET_KEY = $secret
    $env:NEXACONNECT_OMISE_READ_ONLY_INSPECTION = '1'
    $env:NEXACONNECT_OMISE_SANDBOX_ACCEPTANCE = $null
    $env:NEXACONNECT_OMISE_INSPECT_CHARGE_ID = $chargeId
    $env:NEXACONNECT_OMISE_INSPECT_AMOUNT = $ExpectedAmount.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:NEXACONNECT_OMISE_INSPECT_REPORT = Join-Path $runRoot 'summary.json'
    & dotnet test $project --no-build --no-restore --filter 'FullyQualifiedName~OmiseChargeReadOnlyInspectionTests' --logger 'trx;LogFileName=inspection.trx' --results-directory $runRoot --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Read-only inspection failed. Safe diagnostics retained at '$runRoot'. No financial command was sent." }
    [xml] $results = Get-Content -LiteralPath (Join-Path $runRoot 'inspection.trx') -Raw
    $counters = $results.TestRun.ResultSummary.Counters
    if ([int]$counters.executed -ne 1 -or [int]$counters.passed -ne 1 -or -not (Test-Path -LiteralPath $env:NEXACONNECT_OMISE_INSPECT_REPORT)) {
        throw 'Expected exactly one unskipped read-only inspection and a sanitized report.'
    }
    Get-Content -LiteralPath $env:NEXACONNECT_OMISE_INSPECT_REPORT -Raw | Write-Output
    Write-Output "Read-only charge inspection completed. Report retained at '$runRoot'. This is not payment acceptance."
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    $secret = $null; $chargeId = $null
}
