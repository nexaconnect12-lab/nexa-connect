#requires -Version 7.0
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure,[switch]$NoBuild,[switch]$ConfirmProcessTermination,[switch]$ConfirmOmiseTest,[string]$DockerExecutable='docker')
$ErrorActionPreference='Stop'
if(-not $ConfirmProcessTermination){throw 'Pass -ConfirmProcessTermination for exact owned Payment-host restart.'}
if($ConfirmOmiseTest){if($env:NEXACONNECT_REFUND_OMISE_SECRET-notmatch '^skey_test_'-or@(0..2|Where-Object{[string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('NEXACONNECT_REFUND_OMISE_TOKEN_'+$_))}).Count){throw 'Omise test mode requires a test secret and three fresh tokens before fixture writes.'}}
if(-not $ConfirmDisposableInfrastructure){throw 'Pass -ConfirmDisposableInfrastructure to authorize generated local infrastructure, fixture writes and cleanup.'}
if(-not(Get-Command $DockerExecutable -ErrorAction SilentlyContinue)){$DockerExecutable=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/DockerDesktop/resources/bin/docker.exe'}
. (Join-Path $PSScriptRoot 'payment-review-joined-helpers.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId=[Guid]::NewGuid().ToString('N');$projectName="nexa-refund-hosted-$runId"
$run=Join-Path $root ".runstate/refund-hosted/$runId"
$compose=@('compose','-f',(Join-Path $root 'docker/financial-portal/compose.yaml'),'-p',$projectName)
$previous=@{};$processes=@();$created=$false;$passed=$false;$cleanup=$false;$authorizationPassed=$false;$paymentStarted=$false
function Set-RunSetting([string]$key,[string]$value){if(-not $previous.ContainsKey($key)){$previous[$key]=[Environment]::GetEnvironmentVariable($key)};[Environment]::SetEnvironmentVariable($key,$value)}
function Connection([string]$suffix,[string]$user,[string]$password){$b=[System.Data.Common.DbConnectionStringBuilder]::new();$b['Host']='127.0.0.1';$b['Port']=$pgPort;$b['Database']="nexa_review_it_${runId}_$suffix";$b['Username']=$user;$b['Password']=$password;return $b.ConnectionString}
function Start-App($name,$assembly,$working,$settings,$port){
    $info=[Diagnostics.ProcessStartInfo]::new('dotnet');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WorkingDirectory=$working
    $info.ArgumentList.Add($assembly);$info.ArgumentList.Add('--urls');$info.ArgumentList.Add("$(if($name-eq'Simulator'){'https'}else{'http'})://127.0.0.1:$port")
    foreach($key in @($info.Environment.Keys)){if($key -match '^NEXACONNECT_'){$info.Environment.Remove($key)|Out-Null}}
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
    $ports=@(Get-ReviewJoinedFreePorts 8);$bffPort=$ports[4]
    Set-RunSetting NEXACONNECT_JOINED_RUN_ID $runId
    Set-RunSetting NEXACONNECT_JOINED_BFF_PORT $bffPort
    foreach($name in @('ADMIN_PASSWORD','MIGRATION_PASSWORD','RUNTIME_PASSWORD','KEYCLOAK_DB_PASSWORD','KEYCLOAK_ADMIN_PASSWORD','CLIENT_SECRET','READER_PASSWORD','RESOLVER_PASSWORD','RABBITMQ_PASSWORD')){Set-RunSetting "NEXACONNECT_JOINED_$name" ('Aa1!'+[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N'))}
    $existing=@(& $DockerExecutable @compose ps -aq);if($LASTEXITCODE-ne 0-or$existing.Count-ne 0){throw 'Generated project must be empty.'}
    $created=$true;& $DockerExecutable @compose up -d --wait --wait-timeout 180
    if($LASTEXITCODE-ne 0){throw 'Disposable infrastructure startup failed.'}
    $pgPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @compose port postgres 5432)
    $keycloakPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @compose port keycloak 8080)
    $brokerPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @compose port rabbitmq 5672)
    $proxyListener=$ports[5]

    $targets=@{PlatformDirectory=@('platform',3);Restaurant=@('restaurant',3);Authorization=@('authorization',9);Order=@('order',11);Payment=@('payment',10);Reporting=@('reporting',20)}
    foreach($e in $targets.GetEnumerator()){
        Set-RunSetting ('NEXACONNECT_'+$e.Key.ToUpperInvariant()+'_DB') (Connection $e.Value[0] nexaconnect_migration $env:NEXACONNECT_JOINED_MIGRATION_PASSWORD)
        & dotnet run --project (Join-Path $root 'src/Tools/NexaConnect.DataMigration') -- --service $e.Key --scripts-root (Join-Path $root 'src/Tools/NexaConnect.DataMigration/Scripts') --target $e.Value[1] --confirm --application-version 0.23.0
        if($LASTEXITCODE-ne 0){throw "Migration failed for $($e.Key)."}
    }
    Set-RunSetting NEXACONNECT_AUTHORIZATION_INTEGRATION_DB (Connection authorization nexaconnect_migration $env:NEXACONNECT_JOINED_MIGRATION_PASSWORD)
    Set-RunSetting NEXACONNECT_ENVIRONMENT Testing
    $authorizationTrx=Join-Path $run 'authorization.trx'
    & dotnet test (Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests') --filter FullyQualifiedName~AuthorizationAssignmentPersistenceTests --logger "trx;LogFileName=$authorizationTrx" --verbosity minimal
    if($LASTEXITCODE-ne 0){throw 'Authorization persistence regression failed.'}
    [xml]$authorizationResults=Get-Content -LiteralPath $authorizationTrx -Raw
    $counts=$authorizationResults.TestRun.ResultSummary.Counters
    if([int]$counts.total-ne 6-or[int]$counts.passed-ne 6-or[int]$counts.notExecuted-ne 0){throw 'All six authorization persistence cases must pass.'}
    $authorizationPassed=$true
    # Identity provisioning uses the same run-specific imported realm as the joined Review harness.
    & $DockerExecutable @compose exec -T keycloak sh -c '/opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user acceptance-admin --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null'
    if($LASTEXITCODE-ne 0){throw 'Identity administrator authentication failed.'}
    $realm="nexa-review-it-$runId";$subjects=@{};$users=@{}
    foreach($role in @('reader','resolver')){
        $users[$role]="cash-$role-$($runId.Substring(0,8))";$user=$users[$role];$passwordVariable='NEXACONNECT_'+$role.ToUpperInvariant()+'_PASSWORD'
        $subject=& $DockerExecutable @compose exec -T keycloak sh -c "/opt/keycloak/bin/kcadm.sh create users -r '$realm' -s username='$user' -s firstName='Cash' -s lastName='Acceptance' -s email='$user@nexa.invalid' -s emailVerified=true -s enabled=true -i && /opt/keycloak/bin/kcadm.sh set-password -r '$realm' --username '$user' --new-password `"`$$passwordVariable`" >/dev/null"
        if($LASTEXITCODE-ne 0){throw 'Identity creation failed.'};$subjects[$role]=$subject.Trim()
        & $DockerExecutable @compose exec -T keycloak /opt/keycloak/bin/kcadm.sh add-roles -r $realm --uusername $user --rolename customer-viewer
        if($LASTEXITCODE-ne 0){throw 'Customer read role provisioning failed.'}
    }
    # A public password-grant client exists only in this disposable acceptance realm.
    & $DockerExecutable @compose exec -T keycloak /opt/keycloak/bin/kcadm.sh create clients -r $realm -s 'clientId=refund-acceptance' -s 'publicClient=true' -s 'directAccessGrantsEnabled=true' -s 'standardFlowEnabled=false' -s 'defaultClientScopes=["basic","profile","email","nexaconnect-api"]' -s 'protocolMappers=[{"name":"subject","protocol":"openid-connect","protocolMapper":"oidc-sub-mapper","config":{"access.token.claim":"true","id.token.claim":"true"}},{"name":"api-audience","protocol":"openid-connect","protocolMapper":"oidc-audience-mapper","config":{"included.client.audience":"nexaconnect-api","access.token.claim":"true"}}]' | Out-Null
    if($LASTEXITCODE-ne 0){throw 'Disposable identity client provisioning failed.'}
    $fixtureProject=Join-Path $root 'src/Tools/NexaConnect.RefundAcceptance'
    $fixtureDll=Join-Path $fixtureProject 'bin/Debug/net10.0/NexaConnect.RefundAcceptance.dll'
    $runtime=@{platform='platform_directory_app';restaurant='nexaconnect_restaurant_app';authorization='nexaconnect_authorization_app';order='nexaconnect_order_app';payment='nexaconnect_payment_app';reporting='nexaconnect_reporting_app'}
    foreach($e in $runtime.GetEnumerator()){Set-RunSetting ('NEXACONNECT_REFUND_DB_'+$e.Key.ToUpperInvariant()) (Connection $e.Key $e.Value $env:NEXACONNECT_JOINED_RUNTIME_PASSWORD)}
    $authority="http://127.0.0.1:$keycloakPort/realms/$realm"
    $settings=@{ENABLED='1';CONFIRM_DISPOSABLE='1';RUN_ID=$runId;STATE_PATH=(Join-Path $run 'fixture.json');READER_SUBJECT=$subjects.reader;MANAGER_SUBJECT=$subjects.resolver;AUTHORITY=$authority;PAYMENT_URL="http://127.0.0.1:$($ports[5])/";REPORTING_URL="http://127.0.0.1:$($ports[3])/";CLIENT_SECRET=$env:NEXACONNECT_JOINED_CLIENT_SECRET;READER_USERNAME=$users.reader;READER_PASSWORD=$env:NEXACONNECT_JOINED_READER_PASSWORD;MANAGER_USERNAME=$users.resolver;MANAGER_PASSWORD=$env:NEXACONNECT_JOINED_RESOLVER_PASSWORD;ADAPTER=$(if($ConfirmOmiseTest){'Omise'}else{'GenericHttp'})}
    foreach($e in $settings.GetEnumerator()){Set-RunSetting "NEXACONNECT_REFUND_$($e.Key)" $e.Value}
    if(-not $NoBuild){& dotnet build $fixtureProject --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw 'Acceptance tool build failed.'}}
    if($ConfirmOmiseTest){
        Set-RunSetting NEXACONNECT_REFUND_CONFIRM_OMISE_TEST '1'
        Set-RunSetting NEXACONNECT_REFUND_PROVIDER_KEY $env:NEXACONNECT_REFUND_OMISE_SECRET
        foreach($index in 0..2){Set-RunSetting "NEXACONNECT_REFUND_TOKEN_$index" ([Environment]::GetEnvironmentVariable("NEXACONNECT_REFUND_OMISE_TOKEN_$index"))}
    }else{
        $cert=Join-Path $run 'simulator.pfx';$certPassword=[Guid]::NewGuid().ToString('N')
        $rsa=[Security.Cryptography.RSA]::Create(2048)
        $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Disposable refund simulator',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $san=[Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new();$san.AddIpAddress([Net.IPAddress]::Loopback);$request.CertificateExtensions.Add($san.Build())
        $certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddHours(2))
        try{[IO.File]::WriteAllBytes($cert,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$certPassword));$pin=$certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)}finally{$certificate.Dispose();$rsa.Dispose()}
        $providerKey=[Guid]::NewGuid().ToString('N');$providerUrl="https://127.0.0.1:$($ports[6])/"
        Set-RunSetting NEXACONNECT_REFUND_PROVIDER_KEY $providerKey;Set-RunSetting NEXACONNECT_REFUND_PROVIDER_URL $providerUrl;Set-RunSetting NEXACONNECT_REFUND_SIMULATOR_PIN $pin
        $simulator=Join-Path $root 'src/Tools/NexaConnect.PaymentProviderSimulator'
        & dotnet build $simulator --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw 'Simulator build failed.'}
        Start-App Simulator (Join-Path $simulator 'bin/Debug/net10.0/NexaConnect.PaymentProviderSimulator.dll') $simulator @{ASPNETCORE_ENVIRONMENT='Testing';DOTNET_ENVIRONMENT='Testing';ASPNETCORE_URLS=$providerUrl;Simulator__ApiKey=$providerKey;Kestrel__Certificates__Default__Path=$cert;Kestrel__Certificates__Default__Password=$certPassword} $ports[6]
    }
    $preServices=@('PlatformDirectory','Authorization','Restaurant','Reporting','Order')
    for($i=0;$i-lt 5;$i++){Set-RunSetting ('NEXACONNECT_REFUND_SERVICE_'+$preServices[$i].ToUpperInvariant()) "http://127.0.0.1:$($ports[$i])/"}
    $broker="amqp://acceptance:$([Uri]::EscapeDataString($env:NEXACONNECT_JOINED_RABBITMQ_PASSWORD))@127.0.0.1:$brokerPort/"
    Set-RunSetting NEXACONNECT_REFUND_BROKER $broker
    & dotnet $fixtureDll provision;if($LASTEXITCODE-ne 0){throw 'Provider-backed fixture provisioning failed.'}
    $serviceNames=@('PlatformDirectory','Authorization','Restaurant','Reporting','Order')
    $urls=@{};for($i=0;$i-lt 5;$i++){$urls[$serviceNames[$i]]="http://127.0.0.1:$($ports[$i])/"}
    $authority="http://127.0.0.1:$keycloakPort/realms/$realm"
    $common=@{ASPNETCORE_ENVIRONMENT='Development';Authentication__Authority=$authority;Authentication__Audience='nexaconnect-api';Authentication__RequireHttpsMetadata='false';Services__PlatformDirectory=$urls.PlatformDirectory;Services__Authorization=$urls.Authorization;Services__Restaurant=$urls.Restaurant;WorkloadIdentity__Authority=$authority;WorkloadIdentity__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET;WorkloadIdentity__ClientId='nexaconnect-pos-service'}
    foreach($name in @('PlatformDirectory','Authorization','Restaurant','Order')){Set-RunSetting ('NEXACONNECT_REFUND_SERVICE_'+$name.ToUpperInvariant()) $urls[$name]}
    $broker="amqp://acceptance:$([Uri]::EscapeDataString($env:NEXACONNECT_JOINED_RABBITMQ_PASSWORD))@127.0.0.1:$brokerPort/"
    for($i=0;$i-lt 5;$i++){
        $name=$serviceNames[$i];$dir=Join-Path $root "src/Services/NexaConnect.Services.$name"
        if(-not $NoBuild){& dotnet build $dir --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw "$name build failed."}}
        $suffix=if($name-eq'PlatformDirectory'){'platform'}else{$name.ToLowerInvariant()}
        $config=$common.Clone();$config["ConnectionStrings__$name"]=[Environment]::GetEnvironmentVariable('NEXACONNECT_REFUND_DB_'+$suffix.ToUpperInvariant())
        if($name-eq'PlatformDirectory'){$config.KeycloakAdmin__BaseUrl="http://127.0.0.1:$keycloakPort/";$config.KeycloakAdmin__Realm=$realm;$config.KeycloakAdmin__ClientId='platform-directory-admin';$config.KeycloakAdmin__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET}
        if($name-eq'Reporting'){$config.WorkloadIdentity__ClientId='nexaconnect-reporting-service';foreach($consumer in @('OrderSaleConsumer','PaymentRefundConsumer')){$config[$consumer+'__Enabled']='true';$config[$consumer+'__ConnectionString']=$broker};$config.CashCloseConsumer__Enabled='false';$config.ActivityConsumer__Enabled='false'}
        if($name-eq'Order'){$config.Persistence__Provider='PostgreSQL';$config.Outbox__Enabled='true';$config.Outbox__ConnectionString=$broker;$config.WorkflowRecovery__Enabled='false';$config.Workflow__UseHttpAdapters='true';foreach($unused in @('Catalog','Inventory','Kitchen','Payment')){$config['Services__'+$unused]=$env:NEXACONNECT_REFUND_PAYMENT_URL}}
        Start-App $name (Join-Path $dir "bin/Debug/net10.0/NexaConnect.Services.$name.dll") $dir $config $ports[$i]
    }
    Set-RunSetting NEXACONNECT_REFUND_BROKER $broker
    $bound=$false;$deadline=[DateTimeOffset]::UtcNow.AddSeconds(45)
    while([DateTimeOffset]::UtcNow-lt$deadline){
        $bindings=& $DockerExecutable @compose exec -T rabbitmq rabbitmqctl list_bindings source_name destination_name routing_key
        if($LASTEXITCODE-eq 0-and($bindings -match 'nexaconnect.events\s+nexaconnect.reporting.order-sales.v1\s+order.sale-completed.v1')-and($bindings -match 'nexaconnect.events\s+nexaconnect.reporting.payment-refunds.v1\s+payment.refunded.v1')){$bound=$true;break}
        Start-Sleep -Seconds 1
    }
    if(-not $bound){throw 'Both actual Reporting financial bindings must exist before delivery.'}
    $paymentStarted=$true; & dotnet $fixtureDll run;if($LASTEXITCODE-ne 0){throw 'Hosted refund matrix failed.'}
    $summary=Get-Content (Join-Path $run 'matrix.json') -Raw|ConvertFrom-Json
    $expected=if($ConfirmOmiseTest){15}else{17}
    if(-not $summary.verified-or$summary.passed-ne $expected-or$summary.total-ne $expected){throw 'Every hosted refund scenario must pass.'};$passed=$true
}
finally{
    $failed=$false
    try{
        if($paymentStarted){try{$marker=Get-Content -LiteralPath (Join-Path $run 'payment-cleanup.json') -Raw|ConvertFrom-Json;if(-not $marker.cleanupVerified-or$marker.runId-ne $runId){$failed=$true}}catch{$failed=$true}}
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
Write-Output "Hosted refund workflow acceptance passed; evidence: $run/verification.json"
