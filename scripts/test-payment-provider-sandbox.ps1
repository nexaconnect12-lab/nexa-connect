[CmdletBinding()]
param(
    [switch] $ConfirmLocalSandbox,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmLocalSandbox) {
    throw 'Pass -ConfirmLocalSandbox after confirming that this run may exercise the synthetic payment-provider contract.'
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runId = [Guid]::NewGuid().ToString('N')
$runRoot = Join-Path $repositoryRoot ('.runstate/payment-provider-sandbox/' + $runId)
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$unitProject = Join-Path $repositoryRoot 'tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj'
$unitTrx = Join-Path $runRoot 'provider-contract.trx'

if (-not $NoBuild) {
    & dotnet build $unitProject --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Payment-provider sandbox build failed.' }
}

& dotnet test $unitProject --no-build --no-restore --verbosity minimal `
    --filter 'FullyQualifiedName~PaymentProviderRecoveryTests' --logger "trx;LogFileName=$unitTrx"
if ($LASTEXITCODE -ne 0) { throw 'Payment-provider contract verification failed.' }

$total = 0
$passed = 0
foreach ($path in @($unitTrx)) {
    [xml] $trx = Get-Content -LiteralPath $path -Raw
    $total += [int]$trx.TestRun.ResultSummary.Counters.total
    $passed += [int]$trx.TestRun.ResultSummary.Counters.passed
}
if ($total -ne 18 -or $passed -ne 18) {
    throw "Payment-provider evidence was incomplete: total=$total, passed=$passed."
}

$summary = [ordered]@{
    runId = $runId
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    contractTestsPassed = $true
    httpsConfigurationValidated = $false
    liveTlsHandshakeVerified = $false
    bearerCredentialInjectionVerified = $true
    uncertainTimeoutClassified = $true
    idempotentCaptureVerified = $true
    totalTests = $total
}
$summaryPath = Join-Path $runRoot 'summary.json'
$summary | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Output "Payment-provider sandbox verification passed. Sanitized evidence: '$summaryPath'."
