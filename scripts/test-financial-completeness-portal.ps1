#requires -Version 7.0
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure,[switch]$NoBuild,[string]$DockerExecutable='docker',[switch]$EndOfDay,[switch]$DayClose,[switch]$CashierDayClose,[switch]$CashierDayCutoff)
$ErrorActionPreference='Stop'
if($CashierDayCutoff){$CashierDayClose=$true};if($CashierDayClose){$DayClose=$true};if($DayClose){$EndOfDay=$true}
. (Join-Path $PSScriptRoot 'day-close-joined-helpers.ps1')
if(-not $ConfirmDisposableInfrastructure){throw 'Pass -ConfirmDisposableInfrastructure to authorize generated local infrastructure, fixture writes and cleanup.'}
if(-not(Get-Command $DockerExecutable -ErrorAction SilentlyContinue)){$DockerExecutable=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/DockerDesktop/resources/bin/docker.exe'}
. (Join-Path $PSScriptRoot 'payment-review-joined-helpers.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId=[Guid]::NewGuid().ToString('N');$kind=if($CashierDayCutoff){'cashier-day-cutoff'}elseif($CashierDayClose){'cashier-day-close'}elseif($DayClose){'day-close-portal'}elseif($EndOfDay){'end-of-day-portal'}else{'financial-portal'};$projectName="nexa-$kind-$runId"
$run=Join-Path $root ".runstate/$kind/$runId"
$compose=@('compose','-f',(Join-Path $root "docker/$(if($CashierDayClose){'cashier-day-close'}elseif($DayClose){'end-of-day-portal'}else{$kind})/compose.yaml"),'-p',$projectName)
$noRestore=@();if($EndOfDay){$noRestore=@('--no-restore')}
$previous=@{};$processes=@();$created=$false;$passed=$false;$cleanup=$false;$authorizationPassed=$false
function Set-RunSetting([string]$key,[string]$value){if(-not $previous.ContainsKey($key)){$previous[$key]=[Environment]::GetEnvironmentVariable($key)};[Environment]::SetEnvironmentVariable($key,$value)}
function Connection([string]$suffix,[string]$user,[string]$password){$b=[System.Data.Common.DbConnectionStringBuilder]::new();$b['Host']='127.0.0.1';$b['Port']=$pgPort;$b['Database']="nexa_review_it_${runId}_$suffix";$b['Username']=$user;$b['Password']=$password;return $b.ConnectionString}
function Start-App($name,$assembly,$working,$settings,$port){
    $info=[Diagnostics.ProcessStartInfo]::new('dotnet');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WorkingDirectory=$working
        if($CashierDayClose -and $name -match '^(Order(?:Restart[0-9]+)?|POS(?:Restart[0-9]+)?)$'){
        $info.ArgumentList.Add($fixtureDll);$info.ArgumentList.Add($(if($name-match'^Order'){'host-order'}else{'host-pos'}))
    }else{$info.ArgumentList.Add($assembly)};$info.ArgumentList.Add('--urls');$info.ArgumentList.Add("$(if($name-eq'Bff'){'https'}else{'http'})://127.0.0.1:$port")
    foreach($key in @($info.Environment.Keys)){if($key -match '^NEXACONNECT_' -or ($EndOfDay -and $key -match '__')){$info.Environment.Remove($key)|Out-Null}}
    foreach($e in $settings.GetEnumerator()){$info.Environment[$e.Key]=[string]$e.Value}
    # Local logs only; the CI artifact allowlist contains the bounded summary exclusively.
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $p=[Diagnostics.Process]::Start($info)
    $state=@{Process=$p;Out=$p.StandardOutput.ReadToEndAsync();Error=$p.StandardError.ReadToEndAsync();Name=$name}
    $script:processes+=$state
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(90)
    while([DateTimeOffset]::UtcNow-lt$deadline){
        if($p.HasExited){throw "$name exited before startup."}
        $tcp=[Net.Sockets.TcpClient]::new();try{if($tcp.ConnectAsync('127.0.0.1',$port).Wait(500)-and$tcp.Connected){return}}catch{}finally{$tcp.Dispose()}
        Start-Sleep -Milliseconds 300
    };throw "$name startup timed out."
}
try{
    $endpoint=& $DockerExecutable context inspect --format '{{.Endpoints.docker.Host}}'
    if($LASTEXITCODE-ne 0-or$endpoint-notmatch '^(npipe|unix)://'-or($env:DOCKER_HOST-and$env:DOCKER_HOST-notmatch '^(npipe|unix)://')){throw 'A local Docker socket is required.'}
    New-Item -ItemType Directory -Path $run -Force|Out-Null
    $ports=@(Get-ReviewJoinedFreePorts $(if($CashierDayClose){14}elseif($EndOfDay){10}else{6}));$bffPort=$ports[4]
    Set-RunSetting NEXACONNECT_JOINED_RUN_ID $runId
    Set-RunSetting NEXACONNECT_JOINED_BFF_PORT $bffPort
    foreach($name in @('ADMIN_PASSWORD','MIGRATION_PASSWORD','RUNTIME_PASSWORD','KEYCLOAK_DB_PASSWORD','KEYCLOAK_ADMIN_PASSWORD','CLIENT_SECRET','READER_PASSWORD','RESOLVER_PASSWORD','SECONDMANAGER_PASSWORD','ACCOUNTANT_PASSWORD','RABBITMQ_PASSWORD')){Set-RunSetting "NEXACONNECT_JOINED_$name" ('Aa1!'+[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N'))}
    $existing=@(& $DockerExecutable @compose ps -aq);if($LASTEXITCODE-ne 0-or$existing.Count-ne 0){throw 'Generated project must be empty.'}
    $created=$true;& $DockerExecutable @compose up -d --wait --wait-timeout 180
    if($LASTEXITCODE-ne 0){throw 'Disposable infrastructure startup failed.'}
    $pgPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @compose port postgres 5432)
    $keycloakPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @compose port keycloak 8080)
    $brokerPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @compose port rabbitmq 5672)
    $proxyListener=$ports[5]

    $targets=@{PlatformDirectory=@('platform',3);Restaurant=@('restaurant',3);Authorization=@('authorization',9);Order=@('order',11);Payment=@('payment',10);Reporting=@('reporting',20)}
    if($EndOfDay){$targets.POS=@('pos',$(if($DayClose){8}else{7}))}
    if($DayClose){$targets.Authorization=@('authorization',10)};if($CashierDayClose){$targets.Catalog=@('catalog',4);$targets.Inventory=@('inventory',5);$targets.Kitchen=@('kitchen',3)}
    if($CashierDayCutoff){$targets.Order=@('order',16);$targets.Payment=@('payment',15);$targets.POS=@('pos',14);$targets.Authorization=@('authorization',11)}
    foreach($e in $targets.GetEnumerator()){
        Set-RunSetting ('NEXACONNECT_'+$e.Key.ToUpperInvariant()+'_DB') (Connection $e.Value[0] nexaconnect_migration $env:NEXACONNECT_JOINED_MIGRATION_PASSWORD)
        & dotnet run @noRestore --project (Join-Path $root 'src/Tools/NexaConnect.DataMigration') -- --service $e.Key --scripts-root (Join-Path $root 'src/Tools/NexaConnect.DataMigration/Scripts') --target $e.Value[1] --confirm --application-version $(if($CashierDayCutoff){"0.30.0"}elseif($DayClose){"0.24.0"}else{"0.23.0"})
        if($LASTEXITCODE-ne 0){throw "Migration failed for $($e.Key)."}
    }
    Set-RunSetting NEXACONNECT_AUTHORIZATION_INTEGRATION_DB (Connection authorization nexaconnect_migration $env:NEXACONNECT_JOINED_MIGRATION_PASSWORD)
    Set-RunSetting NEXACONNECT_ENVIRONMENT Testing
    $authorizationTrx=Join-Path $run 'authorization.trx'
    & dotnet test @noRestore (Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests') --filter FullyQualifiedName~AuthorizationAssignmentPersistenceTests --logger "trx;LogFileName=$authorizationTrx" --verbosity minimal
    if($LASTEXITCODE-ne 0){throw 'Authorization persistence regression failed.'}
    [xml]$authorizationResults=Get-Content -LiteralPath $authorizationTrx -Raw
    $counts=$authorizationResults.TestRun.ResultSummary.Counters
    if([int]$counts.total-ne 6-or[int]$counts.passed-ne 6-or[int]$counts.notExecuted-ne 0){throw 'All six authorization persistence cases must pass.'}
    $authorizationPassed=$true
    # Identity provisioning uses the same run-specific imported realm as the joined Review harness.
    & $DockerExecutable @compose exec -T keycloak sh -c '/opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user acceptance-admin --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null'
    if($LASTEXITCODE-ne 0){throw 'Identity administrator authentication failed.'}
    $realm="nexa-review-it-$runId";$subjects=@{};$users=@{}
    foreach($role in $(if($CashierDayCutoff){@('reader','resolver','secondmanager','accountant')}elseif($DayClose){@('reader','resolver','secondmanager')}else{@('reader','resolver')})){
        $users[$role]="cash-$role-$($runId.Substring(0,8))";$user=$users[$role];$passwordVariable='NEXACONNECT_'+$role.ToUpperInvariant()+'_PASSWORD'
        $subject=& $DockerExecutable @compose exec -T keycloak sh -c "/opt/keycloak/bin/kcadm.sh create users -r '$realm' -s username='$user' -s firstName='Cash' -s lastName='Acceptance' -s email='$user@nexa.invalid' -s emailVerified=true -s enabled=true -i && /opt/keycloak/bin/kcadm.sh set-password -r '$realm' --username '$user' --new-password `"`$$passwordVariable`" >/dev/null"
        if($LASTEXITCODE-ne 0){throw 'Identity creation failed.'};$subjects[$role]=$subject.Trim()
        & $DockerExecutable @compose exec -T keycloak /opt/keycloak/bin/kcadm.sh add-roles -r $realm --uusername $user --rolename customer-viewer
        if($LASTEXITCODE-ne 0){throw 'Customer read role provisioning failed.'}
    }
    $fixtureProject=Join-Path $root 'src/Tools/NexaConnect.FinancialPortalAcceptance'
    $fixtureDll=Join-Path $fixtureProject 'bin/Debug/net10.0/NexaConnect.FinancialPortalAcceptance.dll'
    $runtime=@{platform='platform_directory_app';restaurant='nexaconnect_restaurant_app';authorization='nexaconnect_authorization_app';order='nexaconnect_order_app';payment='nexaconnect_payment_app';reporting='nexaconnect_reporting_app'}
    if($EndOfDay){$runtime.pos='nexaconnect_pos_app'}
    if($CashierDayClose){$runtime.catalog='nexaconnect_catalog_app';$runtime.inventory='nexaconnect_inventory_app';$runtime.kitchen='nexaconnect_kitchen_app'}
    foreach($e in $runtime.GetEnumerator()){Set-RunSetting ('NEXACONNECT_FINANCIAL_PORTAL_DB_'+$e.Key.ToUpperInvariant()) (Connection $e.Key $e.Value $env:NEXACONNECT_JOINED_RUNTIME_PASSWORD)}
    $settings=@{ENABLED='1';CONFIRM_DISPOSABLE='1';RUN_ID=$runId;STATE_PATH=(Join-Path $run 'fixture.json');READER_SUBJECT=$subjects.reader;RESOLVER_SUBJECT=$subjects.resolver;FIXTURE_DLL=$fixtureDll;RECOVERY_DLL=(Join-Path $root "src/Tools/NexaConnect.FinancialReportingRecovery/bin/Debug/net10.0/NexaConnect.FinancialReportingRecovery.dll")}
    foreach($e in $settings.GetEnumerator()){Set-RunSetting "NEXACONNECT_FINANCIAL_PORTAL_$($e.Key)" $e.Value}
    Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_CASHIER_DAY_CUTOFF $(if($CashierDayCutoff){'1'}else{'0'})
    if($CashierDayCutoff){Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_ACCOUNTANT_SUBJECT $subjects.accountant}
    Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_DAY_CLOSE $(if($DayClose){'1'}else{'0'})
    if($DayClose){Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_SECOND_MANAGER_SUBJECT $subjects.secondmanager}
    Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_END_OF_DAY $(if($EndOfDay){'1'}else{'0'})
    if(-not $NoBuild){& dotnet build $fixtureProject @noRestore --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw 'Fixture build failed.'}}
    & dotnet build (Join-Path $root 'src/Tools/NexaConnect.FinancialReportingRecovery') @noRestore --verbosity minimal -m:1
    if($LASTEXITCODE-ne 0){throw 'Recovery CLI build failed.'}
    Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_CASHIER_DAY_CLOSE $(if($CashierDayClose){'1'}else{'0'}); & dotnet $fixtureDll $(if($CashierDayClose){'provision-cashier'}else{'provision'}) | Out-Null;if($LASTEXITCODE-ne 0){throw 'Fixture provisioning failed.'}
    $fixture=Get-Content (Join-Path $run 'fixture.json') -Raw|ConvertFrom-Json
    $serviceNames=@('PlatformDirectory','Authorization','Restaurant','Reporting')
    $urls=@{};for($i=0;$i-lt 4;$i++){$urls[$serviceNames[$i]]="http://127.0.0.1:$($ports[$i])/"}
    if($EndOfDay){$urls.Order="http://127.0.0.1:$($ports[6])/";$urls.Payment="http://127.0.0.1:$($ports[7])/";$urls.POS="http://127.0.0.1:$($ports[8])/"}
    if($CashierDayClose){for($i=0;$i-lt3;$i++){$urls[@('Catalog','Inventory','Kitchen')[$i]]="http://127.0.0.1:$($ports[10+$i])/"}}
    $authority="http://127.0.0.1:$keycloakPort/realms/$realm"
    if($CashierDayClose){
        $client=(& $DockerExecutable @compose exec -T keycloak /opt/keycloak/bin/kcadm.sh get clients -r $realm -q clientId=nexaconnect-pos --fields id | ConvertFrom-Json)[0].id
        if($LASTEXITCODE-ne0-or -not $client){throw 'POS client lookup failed.'}
        & $DockerExecutable @compose exec -T keycloak /opt/keycloak/bin/kcadm.sh update "clients/$client" -r $realm -s "redirectUris=[`"http://127.0.0.1:$($ports[13])/callback`"]"
        if($LASTEXITCODE-ne0){throw 'Run-owned PKCE callback configuration failed.'}
    }
    $common=@{ASPNETCORE_ENVIRONMENT='Development';Authentication__Authority=$authority;Authentication__Audience='nexaconnect-api';Authentication__RequireHttpsMetadata='false';Services__PlatformDirectory=$urls.PlatformDirectory;Services__Authorization=$urls.Authorization;Services__Restaurant=$urls.Restaurant;WorkloadIdentity__Authority=$authority;WorkloadIdentity__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET;WorkloadIdentity__ClientId='nexaconnect-pos-service'}
    $broker="amqp://acceptance:$([Uri]::EscapeDataString($env:NEXACONNECT_JOINED_RABBITMQ_PASSWORD))@127.0.0.1:$brokerPort/"
    for($i=0;$i-lt 4;$i++){
        $name=$serviceNames[$i];$dir=Join-Path $root "src/Services/NexaConnect.Services.$name"
        if(-not $NoBuild){& dotnet build $dir @noRestore --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw "$name build failed."}}
        $suffix=if($name-eq'PlatformDirectory'){'platform'}else{$name.ToLowerInvariant()}
        $config=$common.Clone();$config["ConnectionStrings__$name"]=[Environment]::GetEnvironmentVariable('NEXACONNECT_FINANCIAL_PORTAL_DB_'+$suffix.ToUpperInvariant())
        if($EndOfDay -and $name-eq'Reporting'){$config.Services__Order=$urls.Order;$config.Services__Payment="http://127.0.0.1:$($ports[9])/";$config.Services__POS=$urls.POS}
        if($name-eq'PlatformDirectory'){$config.KeycloakAdmin__BaseUrl="http://127.0.0.1:$keycloakPort/";$config.KeycloakAdmin__Realm=$realm;$config.KeycloakAdmin__ClientId='platform-directory-admin';$config.KeycloakAdmin__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET}
        if($name-eq'Reporting'){$config.WorkloadIdentity__ClientId='nexaconnect-reporting-service';foreach($consumer in @('OrderSaleConsumer','PaymentRefundConsumer')){$config[$consumer+'__Enabled']='true';$config[$consumer+'__ConnectionString']=$broker};$config.CashCloseConsumer__Enabled='false';$config.ActivityConsumer__Enabled='false'}
        Start-App $name (Join-Path $dir "bin/Debug/net10.0/NexaConnect.Services.$name.dll") $dir $config $ports[$i]
    }
    if($EndOfDay){
        for($i=0;$i-lt 3;$i++){
            $name=@('Order','Payment','POS')[$i];$dir=Join-Path $root "src/Services/NexaConnect.Services.$name"
            if(-not $NoBuild){& dotnet build $dir @noRestore --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw "$name build failed."}}
            $config=$common.Clone();$config["ConnectionStrings__$name"]=[Environment]::GetEnvironmentVariable('NEXACONNECT_FINANCIAL_PORTAL_DB_'+$name.ToUpperInvariant());$config.Persistence__Provider='PostgreSQL'
            $config.Services__Order=$urls.Order;$config.Services__Payment=$urls.Payment
            if($DayClose -and $name-eq'POS'){$config.Services__Reporting="http://127.0.0.1:$proxyListener/"}
            $config.WorkloadIdentity__ClientId=if($name-eq'POS'){'nexaconnect-pos-service'}elseif($name-eq'Order'){'nexaconnect-order-service'}else{'nexaconnect-payment-service'}
            $config.Outbox__Enabled='false';$config.OrderSettlementConsumer__Enabled='false';$config.CashClosePublication__Enabled='false';$config.WorkflowRecovery__Enabled='false';$config.PaymentRecovery__Enabled='false'
            # Order always registers a dispatcher under PostgreSQL; reserve delivery for the fixture phase.
            if($name-eq'Order'){
                $config.Outbox__ConnectionString=$broker;$config.Outbox__BatchSize='0';$config.Workflow__UseHttpAdapters='true'
                foreach($unused in @('Catalog','Inventory','Kitchen')){$config['Services__'+$unused]='http://127.0.0.1:1/'}
            }
            if($DayClose -and $name-eq'POS'){$posConfiguration=$config.Clone();$posDirectory=$dir;$posAssembly=Join-Path $dir 'bin/Debug/net10.0/NexaConnect.Services.POS.dll'}
            if($CashierDayClose){
                if($name-eq'Order'){
                    $config.Outbox__BatchSize='100';foreach($capability in @('Catalog','Inventory','Kitchen')){$config['Services__'+$capability]=$urls[$capability]}
                    $config.Authentication__TokenEndpoint="$authority/protocol/openid-connect/token"
                    $config.Authentication__ClientId='nexaconnect-order-service';$config.Authentication__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET
                }
                if($name-eq'POS'){$config.OrderSettlementConsumer__Enabled='true';$config.OrderSettlementConsumer__ConnectionString=$broker}
                if($name-in@('Order','POS')){foreach($e in @{RunId=$runId;ConfirmDisposable='1';ClockPath=(Join-Path $run 'clock.json');Port=$ports[6+$i]}.GetEnumerator()){$config['Acceptance__'+$e.Key]=[string]$e.Value}}
                if($name-eq'POS'){$posConfiguration=$config.Clone()}
            }
            if($CashierDayCutoff){
                if($name-in@('Order','POS')){$config.Acceptance__Kind='cashier-day-cutoff'}
                if($name-eq'Order'){$config.Outbox__BatchSize='0';$orderConfiguration=$config.Clone();$orderDirectory=$dir;$orderAssembly=Join-Path $dir 'bin/Debug/net10.0/NexaConnect.Services.Order.dll'}
                if($name-eq'POS'){$config.Services__POS=$urls.POS;$config.Services__Payment="http://127.0.0.1:$($ports[9])/";$posConfiguration=$config.Clone()}
            }
            Start-App $name (Join-Path $dir "bin/Debug/net10.0/NexaConnect.Services.$name.dll") $dir $config $ports[6+$i]
        }
    }
    if($CashierDayClose){for($i=0;$i-lt3;$i++){
        $name=@('Catalog','Inventory','Kitchen')[$i];$dir=Join-Path $root "src/Services/NexaConnect.Services.$name"
        & dotnet build $dir @noRestore --verbosity minimal -m:1;if($LASTEXITCODE-ne0){throw "$name build failed."}
        $config=$common.Clone();$config.Persistence__Provider='PostgreSQL';$config["ConnectionStrings__$name"]=[Environment]::GetEnvironmentVariable('NEXACONNECT_FINANCIAL_PORTAL_DB_'+$name.ToUpperInvariant())
        $config.WorkloadIdentity__ClientId='nexaconnect-'+$name.ToLowerInvariant()+'-service';$config.Outbox__Enabled='false'
        Start-App $name (Join-Path $dir "bin/Debug/net10.0/NexaConnect.Services.$name.dll") $dir $config $ports[10+$i]
    }}
    Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_BROKER $broker
    $bound=$false;$deadline=[DateTimeOffset]::UtcNow.AddSeconds(45)
    while([DateTimeOffset]::UtcNow-lt$deadline){
        $bindings=& $DockerExecutable @compose exec -T rabbitmq rabbitmqctl list_bindings source_name destination_name routing_key
        if($LASTEXITCODE-eq 0-and($bindings -match 'nexaconnect.events\s+nexaconnect.reporting.order-sales.v1\s+order.sale-completed.v1')-and($bindings -match 'nexaconnect.events\s+nexaconnect.reporting.payment-refunds.v1\s+payment.refunded.v1')-and(-not $CashierDayClose -or ($bindings -match 'nexaconnect.events\s+nexaconnect.pos.order-manual-tender.v1\s+order.manual-tender-settled.v1'))){$bound=$true;break}
        Start-Sleep -Seconds 1
    }
    if(-not $bound){throw 'All required actual financial consumer bindings must exist before delivery.'}
    $bffDir=Join-Path $run 'bff'
    & dotnet publish (Join-Path $root 'src/Gateway/NexaConnect.CustomerBff') --output $bffDir @noRestore --verbosity minimal -m:1
    if($LASTEXITCODE-ne 0){throw 'BFF publish failed.'}
    $cert=Join-Path $run 'acceptance.pfx';$certPassword=[Guid]::NewGuid().ToString('N')
    & dotnet dev-certs https --export-path $cert --password $certPassword|Out-Null;if($LASTEXITCODE-ne 0){throw 'Acceptance certificate creation failed.'}
    $bff=$common+@{Bff__Authority=$authority;Bff__RequireHttpsMetadata='false';Bff__ClientId='nexaconnect-web-bff';Bff__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET;Services__Reporting="http://127.0.0.1:$proxyListener/";Kestrel__Certificates__Default__Path=$cert;Kestrel__Certificates__Default__Password=$certPassword}
    if($DayClose){$bff.Services__POS=$urls.POS}
    Start-App Bff (Join-Path $bffDir 'NexaConnect.CustomerBff.dll') $bffDir $bff $bffPort
    $live=@{BASE_URL="https://127.0.0.1:$bffPort";OIDC_ISSUER=$authority;PROXY_PORT=$proxyListener;REPORTING_PORT=$ports[3];READER_USERNAME=$users.reader;READER_PASSWORD=$env:NEXACONNECT_JOINED_READER_PASSWORD;RESOLVER_USERNAME=$users.resolver;RESOLVER_PASSWORD=$env:NEXACONNECT_JOINED_RESOLVER_PASSWORD}
    foreach($e in $live.GetEnumerator()){Set-RunSetting "NEXACONNECT_FINANCIAL_PORTAL_$($e.Key)" $e.Value}
    if($EndOfDay){Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_SOURCE_PROXY_PORT $ports[9];Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_PAYMENT_PORT $ports[7]}
    if($DayClose){foreach($entry in @{SECOND_MANAGER_USERNAME=$users.secondmanager;SECOND_MANAGER_PASSWORD=$env:NEXACONNECT_JOINED_SECONDMANAGER_PASSWORD;POS_PORT=$ports[8]}.GetEnumerator()){Set-RunSetting ("NEXACONNECT_FINANCIAL_PORTAL_"+$entry.Key) ([string]$entry.Value)}}
    if($CashierDayClose){foreach($name in @('POS','Order','Catalog','Inventory','Kitchen')){Set-RunSetting ("NEXACONNECT_FINANCIAL_PORTAL_"+$name.ToUpperInvariant()+"_URL") $urls[$name]};Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_CALLBACK_PORT $ports[13]}
    if($CashierDayCutoff){Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_ACCOUNTANT_USERNAME $users.accountant;Set-RunSetting NEXACONNECT_FINANCIAL_PORTAL_ACCOUNTANT_PASSWORD $env:NEXACONNECT_JOINED_ACCOUNTANT_PASSWORD}
    Push-Location (Join-Path $root 'src/Frontend')
    $suite=if($CashierDayCutoff){'cashier-day-cutoff-live'}elseif($CashierDayClose){'cashier-day-close-live'}elseif($DayClose){'day-close-live'}elseif($EndOfDay){'end-of-day-live'}else{'financial-completeness-live'}
    try{if($DayClose){Invoke-DayCloseJoinedBrowser}else{& npx playwright test --config "playwright.$suite.config.mjs"};if(-not $DayClose -and $LASTEXITCODE-ne 0){throw 'Joined browser acceptance failed.'}}finally{Pop-Location}
    $summary=Get-Content (Join-Path $root "src/Frontend/test-results/$suite/$runId/summary.json") -Raw|ConvertFrom-Json
    $expected=if($CashierDayCutoff){14}elseif($CashierDayClose){5}elseif($DayClose){11}elseif($EndOfDay){8}else{7}
    if(-not $summary.verified-or$summary.passed-ne $expected-or$summary.total-ne $expected){throw 'All distinct scenarios must pass without skips.'};$passed=$true
}
finally{
    $failed=$false
    try{
        foreach($state in $processes){try{if(-not $state.Process.HasExited){$state.Process.Kill($true);if(-not $state.Process.WaitForExit(10000)){throw 'Process cleanup timed out.'}};$state.Out.GetAwaiter().GetResult()|Set-Content (Join-Path $run "$($state.Name).log");$state.Error.GetAwaiter().GetResult()|Set-Content (Join-Path $run "$($state.Name).error.log")}catch{$failed=$true}}
        if($created){try{& $DockerExecutable @compose down --volumes --remove-orphans|Out-Null;if($LASTEXITCODE-ne 0){$failed=$true};$remaining=@(& $DockerExecutable @compose ps -aq);if($LASTEXITCODE-ne 0-or$remaining.Count-ne 0){$failed=$true}}catch{$failed=$true}}
        try{if($cert-and(Test-Path -LiteralPath $cert)){Remove-Item -LiteralPath $cert}}catch{$failed=$true}
    }
    finally{
        foreach($key in $previous.Keys){try{[Environment]::SetEnvironmentVariable($key,$previous[$key])}catch{$failed=$true}}
        $cleanup=-not $failed
        if(Test-Path $run){@{runId=$runId;passed=$passed;authorizationPassed=$authorizationPassed;cleanupVerified=$cleanup;productionVerified=$false;sourceRevision=(& git -C $root rev-parse HEAD);sourceDirty=[bool](& git -C $root status --porcelain);completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $run 'verification.json')}
    }
    if($failed){throw 'Disposable process, infrastructure, certificate or environment cleanup could not be verified.'}
}
Write-Output "Joined $kind acceptance passed; evidence: $run/verification.json"
