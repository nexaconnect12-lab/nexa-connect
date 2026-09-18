#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot '../../scripts/test-order-provider-recovery-live.ps1'
$tokens = @('AUTHORIZATION_RESPONSE','CAPTURE_RESPONSE','VOID_RESPONSE','VOID_PAID_PROTECTION') | ForEach-Object { 'NEXACONNECT_OMISE_' + $_ + '_TEST_TOKEN' }
$names = @('NEXACONNECT_OMISE_TEST_SECRET_KEY', 'NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN') + $tokens
$saved = @{}; foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$cases = @(
    @{ amount = 50; kind = 'confirmation'; expected = 'Pass all three confirmation switches*' },
    @{ amount = 0; kind = 'amount'; expected = 'Use an exact two-decimal THB amount*' },
    @{ amount = 0.001; kind = 'amount'; expected = 'Use an exact two-decimal THB amount*' },
    @{ amount = 10001; kind = 'amount'; expected = 'Use an exact two-decimal THB amount*' },
    @{ amount = 50; kind = 'live'; expected = 'Inject NEXACONNECT_OMISE_TEST_SECRET_KEY*' },
    @{ amount = 50; kind = 'missing'; expected = 'Inject a fresh test token*' },
    @{ amount = 50; kind = 'duplicate'; expected = 'Four distinct unused Omise test tokens*' },
    @{ amount = 50; kind = 'handoff_generic'; expected = 'Card token handoff acceptance requires -Adapter Omise*' },
    @{ amount = 50; kind = 'handoff_missing'; expected = 'Inject a fresh test token in NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN*' },
    @{ amount = 50; kind = 'handoff_duplicate'; expected = 'Four distinct unused Omise test tokens*' }
)
try {
    foreach ($case in $cases) {
        $env:NEXACONNECT_OMISE_TEST_SECRET_KEY = 'skey_test_aaaaaaaaaaaa'
        [Environment]::SetEnvironmentVariable('NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN', $null, 'Process')
        for ($i = 0; $i -lt $tokens.Count; $i++) { [Environment]::SetEnvironmentVariable($tokens[$i], 'tokn_test_aaaaaaaaaaa' + $i, 'Process') }
        if ($case.kind -eq 'live') { $env:NEXACONNECT_OMISE_TEST_SECRET_KEY = 'skey_live_aaaaaaaaaaaa' }
        if ($case.kind -eq 'missing') { [Environment]::SetEnvironmentVariable($tokens[3], $null, 'Process') }
        if ($case.kind -eq 'duplicate') { [Environment]::SetEnvironmentVariable($tokens[3], [Environment]::GetEnvironmentVariable($tokens[0]), 'Process') }
        $arguments = @{ Adapter = 'Omise'; OmiseAmount = $case.amount; DockerExecutable = 'must-never-run-docker' }
        if ($case.kind -like 'handoff_*') { $arguments.IncludeCardTokenHandoff = $true }
        if ($case.kind -eq 'handoff_generic') { $arguments.Adapter = 'GenericHttp' }
        if ($case.kind -eq 'handoff_duplicate') { $env:NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN = [Environment]::GetEnvironmentVariable($tokens[0]) }
        if ($case.kind -ne 'confirmation') {
            $arguments.ConfirmDisposableInfrastructure = $true
            $arguments.ConfirmProcessTermination = $true
            $arguments.ConfirmSandboxTransactions = $true
        }
        $failure = $null
        try { & $runner @arguments } catch { $failure = $_.Exception.Message }
        if ($null -eq $failure -or $failure -notlike $case.expected) { throw "Preflight case '$($case.kind)' did not reject before infrastructure/provider access." }
    }
    Write-Output 'Omise hosted preflight passed: 10 rejected cases; no infrastructure or provider access.'
}
finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') } }
