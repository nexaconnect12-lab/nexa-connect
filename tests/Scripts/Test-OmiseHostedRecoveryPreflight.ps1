#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot '../../scripts/test-order-provider-recovery-live.ps1'
$tokens = @('AUTHORIZATION_RESPONSE','CAPTURE_RESPONSE','VOID_RESPONSE','VOID_PAID_PROTECTION') | ForEach-Object { 'NEXACONNECT_OMISE_' + $_ + '_TEST_TOKEN' }
$names = @('NEXACONNECT_OMISE_TEST_SECRET_KEY', 'NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN',
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL','NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY',
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT','NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY') + $tokens
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
    $validationCases = @('four','five','missing_fifth','duplicate_fifth','token_newline','secret_newline')
    foreach ($kind in $validationCases) {
        $env:NEXACONNECT_OMISE_TEST_SECRET_KEY = 'skey_test_aaaaaaaaaaaa'
        for ($i = 0; $i -lt $tokens.Count; $i++) { [Environment]::SetEnvironmentVariable($tokens[$i], 'tokn_test_aaaaaaaaaaa' + $i, 'Process') }
        $env:NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN = 'tokn_test_bbbbbbbbbbbb'
        $arguments = @{ Adapter='Omise'; ValidateOnly=$true; DockerExecutable='must-never-run-docker' }
        if ($kind -ne 'four') { $arguments.IncludeCardTokenHandoff=$true }
        if ($kind -eq 'missing_fifth') { $env:NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN=$null }
        if ($kind -eq 'duplicate_fifth') { $env:NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN=[Environment]::GetEnvironmentVariable($tokens[0]) }
        if ($kind -eq 'token_newline') { $env:NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN="tokn_test_bbbbbbbbbbbb`n" }
        if ($kind -eq 'secret_newline') { $env:NEXACONNECT_OMISE_TEST_SECRET_KEY="skey_test_aaaaaaaaaaaa`n" }
        $failure=$null; $output=$null
        try { $output = (& $runner @arguments | Out-String) } catch { $failure=$_.Exception.Message }
        if ($kind -in @('four','five')) {
            if ($failure) { throw "Validation-only positive case rejected: $kind" }
            if ($output -match 'skey_|tokn_') { throw 'Validation-only output exposed credentials.' }
            $result=$output | ConvertFrom-Json
            $expectedCount=if ($kind -eq 'four') {4} else {5}
            if (-not $result.configurationValidated -or $result.scenarioCount -ne $expectedCount -or
                $result.cardTokenHandoffIncluded -ne ($kind -eq 'five') -or
                $result.tokenFreshnessVerified -or $result.accountOwnershipVerified -or $result.infrastructureChecked -or
                $result.infrastructureStarted -or $result.providerRequestsSent -ne 0 -or $result.financialCommandsSent -ne 0 -or
                $result.processesTerminated -ne 0 -or $result.acceptancePassed) { throw 'Validation-only scope incorrectly reported.' }
        }
        elseif (-not $failure -or $failure -notmatch 'Inject a fresh test token|distinct unused Omise test tokens|Inject NEXACONNECT_OMISE_TEST_SECRET_KEY') {
            throw "Validation-only guard failed: $kind"
        }
    }
    foreach ($kind in @('generic_valid','generic_missing','generic_http')) {
        $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL='https://sandbox.example.invalid/'
        $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY='acceptance-placeholder'
        $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT='50.00'
        $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY='THB'
        if ($kind -eq 'generic_missing') { $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL=$null }
        if ($kind -eq 'generic_http') { $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL='http://sandbox.example.invalid/' }
        $failure=$null; $output=$null
        try { $output=(& $runner -ValidateOnly -DockerExecutable 'must-never-run-docker' | Out-String) } catch { $failure=$_.Exception.Message }
        if ($kind -eq 'generic_valid') {
            if ($failure) { throw 'Default GenericHttp validation rejected.' }
            $result=$output | ConvertFrom-Json
            if ($result.adapter -ne 'GenericHttp' -or $result.scenarioCount -ne 5 -or -not $result.configurationValidated -or $result.cardTokenHandoffIncluded -or
                $result.providerRequestsSent -ne 0 -or $result.acceptancePassed -or $output -match 'acceptance-placeholder|sandbox.example.invalid') { throw 'GenericHttp validation output is unsafe or inaccurate.' }
        }
        elseif (-not $failure -or $failure -notmatch 'Missing provider recovery setting|non-production HTTPS sandbox URL') { throw "GenericHttp validation guard failed: $kind" }
    }
    Write-Output 'Omise hosted preflight passed: 10 execution rejections and 9 validation-only cases; no infrastructure or provider access.'
}
finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') } }
