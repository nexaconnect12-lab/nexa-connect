[CmdletBinding()]
param([switch]$ValidateOnly, [switch]$StartInfrastructure, [switch]$EnableOmiseTestCheckout)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$settings = Get-Content -LiteralPath (Join-Path $root 'src/Clients/NexaConnect.POS/appsettings.json') -Raw | ConvertFrom-Json
$ports = [ordered]@{ PlatformDirectory=53357; Authorization=51223; Restaurant=51225; Catalog=5268; Inventory=5270; Kitchen=5274; Order=5230; POS=5225; Reporting=51227 }
$workloads = @('Catalog','Inventory','Kitchen','Order','POS')
if ($EnableOmiseTestCheckout) { $ports['Payment'] = 5272; $workloads += 'Payment' }
# Configuration and secrets remain in process environment; never interpolate secrets into command lines.
$savedEnvironment = @{}
function Set-RunEnvironment([string]$name, [string]$value) {
    if (!$savedEnvironment.ContainsKey($name)) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    [Environment]::SetEnvironmentVariable($name, $value, 'Process')
}
$children = @()
try {
    $envPath = Join-Path $root '.env'
    if (Test-Path -LiteralPath $envPath) {
        foreach ($line in Get-Content -LiteralPath $envPath) {
            if ($line -match '^\s*(#|$)') { continue }
            $equals = $line.IndexOf('=')
            if ($equals -le 0) { continue }
            $name = $line.Substring(0,$equals).Trim()
            if (![Environment]::GetEnvironmentVariable($name)) { Set-RunEnvironment $name $line.Substring($equals+1).Trim().Trim('"', "'") }
        }
    }
    $cardCheckout = $settings.Pos.PaymentMethod -eq 'card_omise_test'
    if ($settings.Pos.Currency -cne 'THB' -or $settings.Pos.PaymentMethod -notin @('cash_manual','promptpay_manual','card_omise_test')) { throw 'Checkout requires a supported THB payment method.' }
    if ($cardCheckout -ne [bool]$EnableOmiseTestCheckout -or ($cardCheckout -and $settings.Pos.EnableOmiseTestCheckout -ne $true)) { throw 'Omise test checkout requires matching client enablement and -EnableOmiseTestCheckout; use the default launcher for manual tender.' }
    if ($cardCheckout) {
        $omiseTestSecret = $env:NEXACONNECT_OMISE_TEST_SECRET_KEY
        if ($omiseTestSecret -cnotmatch '^skey_test_[a-z0-9]{10,64}$') { throw 'Inject NEXACONNECT_OMISE_TEST_SECRET_KEY without printing it. Live keys are rejected.' }
    }
    foreach ($name in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')) {
        $id = [guid]::Empty
        if (![guid]::TryParse([string]$settings.Pos.$name,[ref]$id) -or $id -eq [guid]::Empty) { throw "Invalid Pos:$name." }
    }
    foreach ($entry in @{ PosApi='POS'; OrderApi='Order'; CatalogApi='Catalog' }.GetEnumerator()) {
        $expected = "http://localhost:$($ports[$entry.Value])/"
        if ($settings.Services.($entry.Key) -ne $expected) { throw "Services:$($entry.Key) must be $expected for this launcher." }
    }
    if ($settings.Identity.Authority -ne 'http://localhost:8080/realms/nexa-dev') { throw 'This launcher requires the local nexa-dev identity realm.' }
    $missing = @()
    foreach ($service in $ports.Keys) {
        $name = "ConnectionStrings__$service"
        if (![Environment]::GetEnvironmentVariable($name)) {
            $alias = 'NEXACONNECT_' + $service.ToUpperInvariant() + '_IMPORT_DB'
            if ([Environment]::GetEnvironmentVariable($alias)) { Set-RunEnvironment $name ([Environment]::GetEnvironmentVariable($alias)) }
            else { $missing += $name }
        }
    }
    foreach ($service in $workloads) {
        $name = 'NEXACONNECT_' + $service.ToUpperInvariant() + '_SERVICE_CLIENT_SECRET'
        if (![Environment]::GetEnvironmentVariable($name)) { $missing += $name }
    }
    if (!$env:NEXACONNECT_CHECKOUT_RABBITMQ) { $missing += 'NEXACONNECT_CHECKOUT_RABBITMQ' }
    if ($missing.Count) { throw "Missing settings (values not shown): $($missing -join ', ')" }
    Write-Host 'Checkout configuration and required setting names validated. Database migrations, permissions, and seed data must already be provisioned.'
    if ($ValidateOnly) { return }
    if ($cardCheckout) {
        foreach ($name in @([Environment]::GetEnvironmentVariables('Process').Keys | Where-Object { $_ -like 'NEXACONNECT_OMISE_*' })) { Set-RunEnvironment $name $null }
    }
    if ($StartInfrastructure) {
        Push-Location $root
        try { docker compose up -d postgres redis rabbitmq keycloak; if ($LASTEXITCODE) { throw 'Infrastructure startup failed.' } }
        finally { Pop-Location }
    }
    $identityReady = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        # Keycloak discovery can take more than two seconds on Docker Desktop even
        # after the container health check passes. Keep this probe bounded without
        # rejecting an otherwise healthy local identity provider.
        try { $oidc = Invoke-RestMethod 'http://localhost:8080/realms/nexa-dev/.well-known/openid-configuration' -TimeoutSec 5; $identityReady = $oidc.issuer -eq $settings.Identity.Authority; if($identityReady) { break } } catch { }
        Start-Sleep -Seconds 1
    }
    if (!$identityReady) { throw 'Local Keycloak nexa-dev discovery is not ready.' }
    if ([string]::IsNullOrWhiteSpace([string]$oidc.token_endpoint)) { throw 'Local Keycloak discovery did not publish a token endpoint.' }
    foreach ($port in $ports.Values) {
        $probe = New-Object Net.Sockets.TcpClient
        try { if ($probe.ConnectAsync('localhost',$port).Wait(300) -and $probe.Connected) { throw "Port $port is occupied. Stop the existing development launcher before starting this stack." } }
        catch [AggregateException] { }
        finally { $probe.Dispose() }
    }
    $runDirectory = Join-Path $root ('.runstate/checkout-stack/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runDirectory | Out-Null
    Set-RunEnvironment 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-RunEnvironment 'Authentication__Authority' $settings.Identity.Authority
    Set-RunEnvironment 'Authentication__RequireHttpsMetadata' 'false'
    Set-RunEnvironment 'Persistence__Provider' 'PostgreSQL'
    Set-RunEnvironment 'Workflow__UseHttpAdapters' 'true'
    Set-RunEnvironment 'WorkloadIdentity__Authority' $settings.Identity.Authority
    Set-RunEnvironment 'Outbox__ConnectionString' $env:NEXACONNECT_CHECKOUT_RABBITMQ
    Set-RunEnvironment 'Outbox__Enabled' 'true'
    foreach ($service in $ports.Keys) { Set-RunEnvironment "Services__$service" "http://localhost:$($ports[$service])/" }
    foreach ($service in $ports.Keys) {
        $out = Join-Path $runDirectory $service
        Set-RunEnvironment 'CardCheckout__EnableOmiseTestCheckout' $(if($cardCheckout -and $service -eq 'Order') {'true'} else {'false'})
        Set-RunEnvironment 'PaymentProvider__Adapter' $(if($cardCheckout -and $service -eq 'Payment') {'Omise'} else {'Disabled'})
        Set-RunEnvironment 'PaymentProvider__OmiseSecretKey' $(if($cardCheckout -and $service -eq 'Payment') {$omiseTestSecret} else {$null})
        $project = Join-Path $root "src/Services/NexaConnect.Services.$service/NexaConnect.Services.$service.csproj"
        dotnet build $project --no-restore --verbosity quiet "-p:OutputPath=$out/"
        if ($LASTEXITCODE) { throw "Build failed for $service. Run dotnet restore NexaConnect.sln first if assets are missing." }
        Set-RunEnvironment 'WorkloadIdentity__ClientId' ('nexaconnect-' + $service.ToLowerInvariant() + '-service')
        Set-RunEnvironment 'WorkloadIdentity__ClientSecret' ([Environment]::GetEnvironmentVariable('NEXACONNECT_' + $service.ToUpperInvariant() + '_SERVICE_CLIENT_SECRET'))
        if ($service -in $workloads) {
            # Order's workflow adapters still consume the legacy Authentication
            # workload settings, while tenant adapters use WorkloadIdentity.
            # Keep both paths on the discovery-published endpoint and the same
            # service-owned credentials until the adapters share one provider.
            Set-RunEnvironment 'Authentication__TokenEndpoint' ([string]$oidc.token_endpoint)
            Set-RunEnvironment 'Authentication__ClientId' ('nexaconnect-' + $service.ToLowerInvariant() + '-service')
            Set-RunEnvironment 'Authentication__ClientSecret' ([Environment]::GetEnvironmentVariable('NEXACONNECT_' + $service.ToUpperInvariant() + '_SERVICE_CLIENT_SECRET'))
        }
        Set-RunEnvironment 'OrderSettlementConsumer__Enabled' $(if($service -eq 'POS') {'true'} else {'false'})
        Set-RunEnvironment 'OrderSettlementConsumer__ConnectionString' $env:NEXACONNECT_CHECKOUT_RABBITMQ
        Set-RunEnvironment 'WorkflowRecovery__Enabled' $(if($service -eq 'Order') {'true'} else {'false'})
        Set-RunEnvironment 'PaymentReconciliationConsumer__Enabled' $(if($service -eq 'Order') {'true'} else {'false'})
        Set-RunEnvironment 'PaymentReconciliationConsumer__ConnectionString' $env:NEXACONNECT_CHECKOUT_RABBITMQ
        Set-RunEnvironment 'ActivityConsumer__Enabled' $(if($service -eq 'Reporting') {'true'} else {'false'})
        Set-RunEnvironment 'ActivityConsumer__ConnectionString' $env:NEXACONNECT_CHECKOUT_RABBITMQ
        $dll = Join-Path $out "NexaConnect.Services.$service.dll"
        $child = Start-Process dotnet -ArgumentList @(('"' + $dll + '"'),'--urls',"http://localhost:$($ports[$service])") -WorkingDirectory $out -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'service.log') -RedirectStandardError (Join-Path $out 'service.error.log')
        $children += $child
        $ready = $false
        # Cold .NET startup can exceed 30 seconds on development workstations
        # after an isolated build. Keep the probe bounded while allowing the
        # host enough time to finish JIT and identity initialization.
        for($attempt=0; $attempt -lt 180; $attempt++) {
            if($child.HasExited) { throw "$service exited; inspect its restricted local logs." }
            $probe = New-Object Net.Sockets.TcpClient
            try {
                if ($probe.ConnectAsync('127.0.0.1', $ports[$service]).Wait(1000) -and $probe.Connected) {
                    $ready=$true
                    break
                }
            } catch { }
            finally { $probe.Dispose() }
            Start-Sleep -Seconds 1
        }
        if(!$ready) { throw "$service HTTP host did not become ready." }
        Write-Host "$service TCP listener ready on $($ports[$service])."
    }
    Write-Host 'Checkout hosts are running. Open POS in another terminal. TCP listener checks do not certify HTTP handling, dependencies, database schema, seeded permissions, or live checkout acceptance. Ctrl+C stops only these launcher-owned hosts.'
    while ($true) {
        if (@($children | Where-Object HasExited).Count) { throw 'A checkout host exited.' }
        Start-Sleep -Seconds 1
    }
}
finally {
    $omiseTestSecret = $null
    foreach ($child in $children) { if (!$child.HasExited) { $child.Kill(); $child.WaitForExit(5000) | Out-Null }; $child.Dispose() }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name,$savedEnvironment[$name],'Process') }
}
