#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sandbox = Join-Path $repository ('.runstate/omise-launcher-preflight/' + [Guid]::NewGuid().ToString('N'))
$scriptDirectory = Join-Path $sandbox 'scripts'
$settingsDirectory = Join-Path $sandbox 'src/Clients/NexaConnect.POS'
New-Item -ItemType Directory -Path $scriptDirectory,$settingsDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'scripts/run-checkout-development.ps1') -Destination $scriptDirectory
$runner = Join-Path $scriptDirectory 'run-checkout-development.ps1'
$settingsPath = Join-Path $settingsDirectory 'appsettings.json'
$configuration = @{
    Identity = @{ Authority = 'http://localhost:8080/realms/nexa-dev' }
    Services = @{ PosApi = 'http://localhost:5225/'; OrderApi = 'http://localhost:5230/'; CatalogApi = 'http://localhost:5268/' }
    Pos = @{ Currency = 'THB'; PaymentMethod = 'card_omise_test'; EnableOmiseTestCheckout = $true }
}
foreach ($name in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')) { $configuration.Pos[$name] = [Guid]::NewGuid().ToString('D') }
$values = @{ NEXACONNECT_OMISE_TEST_SECRET_KEY = 'skey_test_aaaaaaaaaaaa'; NEXACONNECT_CHECKOUT_RABBITMQ = 'acceptance-placeholder'; NEXACONNECT_PAYMENT_IMPORT_DB = $null; NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET = [Convert]::ToBase64String([byte[]](1..32)) }
foreach ($service in @('PlatformDirectory','Authorization','Restaurant','Catalog','Inventory','Kitchen','Order','POS','Reporting','Payment')) { $values['ConnectionStrings__' + $service] = 'acceptance-placeholder' }
foreach ($service in @('Catalog','Inventory','Kitchen','Order','POS','Payment')) { $values['NEXACONNECT_' + $service.ToUpperInvariant() + '_SERVICE_CLIENT_SECRET'] = 'acceptance-placeholder' }
$saved = @{}; foreach ($name in $values.Keys) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$cases = @(
    @{ kind = 'enabled'; expected = $null },
    @{ kind = 'missing_switch'; expected = 'Omise test checkout requires matching*' },
    @{ kind = 'missing_client_flag'; expected = 'Omise test checkout requires matching*' },
    @{ kind = 'live_key'; expected = 'Inject NEXACONNECT_OMISE_TEST_SECRET_KEY*' },
    @{ kind = 'missing_payment_db'; expected = 'Missing settings*ConnectionStrings__Payment*' },
    @{ kind = 'missing_payment_workload'; expected = 'Missing settings*NEXACONNECT_PAYMENT_SERVICE_CLIENT_SECRET*' },
    @{ kind = 'manual_default'; expected = $null }
    @{ kind = 'webhook_enabled'; expected = $null },
    @{ kind = 'webhook_missing'; expected = 'Inject a base64 test webhook secret*' },
    @{ kind = 'webhook_without_checkout'; expected = 'Omise test webhooks require*' }
)
try {
    foreach ($case in $cases) {
        foreach ($name in $values.Keys) { [Environment]::SetEnvironmentVariable($name, $values[$name], 'Process') }
        $configuration.Pos.PaymentMethod = 'card_omise_test'; $configuration.Pos.EnableOmiseTestCheckout = $true
        if ($case.kind -eq 'missing_client_flag') { $configuration.Pos.EnableOmiseTestCheckout = $false }
        if ($case.kind -eq 'live_key') { $env:NEXACONNECT_OMISE_TEST_SECRET_KEY = 'skey_live_aaaaaaaaaaaa' }
        if ($case.kind -eq 'missing_payment_db') { [Environment]::SetEnvironmentVariable('ConnectionStrings__Payment', $null, 'Process') }
        if ($case.kind -eq 'missing_payment_workload') { [Environment]::SetEnvironmentVariable('NEXACONNECT_PAYMENT_SERVICE_CLIENT_SECRET', $null, 'Process') }
        if ($case.kind -eq 'manual_default') { $configuration.Pos.PaymentMethod = 'cash_manual'; $configuration.Pos.EnableOmiseTestCheckout = $false }
        if ($case.kind -eq 'webhook_missing') { $env:NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET=$null }
        $configuration | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath
        $failure = $null
        try { & $runner -ValidateOnly -EnableOmiseTestCheckout:($case.kind -notin @('missing_switch','manual_default','webhook_without_checkout')) -EnableOmiseTestWebhooks:($case.kind -like 'webhook_*') | Out-Null }
        catch { $failure = $_.Exception.Message }
        if (($null -eq $case.expected -and $null -ne $failure) -or ($null -ne $case.expected -and ($null -eq $failure -or $failure -notlike $case.expected))) { throw "Launcher preflight case '$($case.kind)' did not match its safe configuration boundary." }
    }
    Write-Output 'Checkout launcher preflight passed: 10 cases; isolated settings; no hosts, infrastructure or provider requests.'
}
finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') } }
