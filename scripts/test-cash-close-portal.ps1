#requires -Version 7.0
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure,[switch]$NoBuild,[string]$DockerExecutable='docker')
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Pass -ConfirmDisposableInfrastructure to authorize generated local infrastructure, fixture writes and cleanup.'}
if(-not(Get-Command $DockerExecutable -ErrorAction SilentlyContinue)){$DockerExecutable=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/DockerDesktop/resources/bin/docker.exe'}
. (Join-Path $PSScriptRoot 'payment-review-joined-helpers.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId=[Guid]::NewGuid().ToString('N');$projectName="nexa-cash-portal-$runId"
$run=Join-Path $root ".runstate/cash-close-portal/$runId"
$compose=@('compose','-f',(Join-Path $root 'docker/cash-close-portal/compose.yaml'),'-p',$projectName)
$previous=@{};$processes=@();$created=$false;$passed=$false;$cleanup=$false
function Set-RunSetting([string]$key,[string]$value){if(-not $previous.ContainsKey($key)){$previous[$key]=[Environment]::GetEnvironmentVariable($key)};[Environment]::SetEnvironmentVariable($key,$value)}
function Connection([string]$suffix,[string]$user,[string]$password){$b=[System.Data.Common.DbConnectionStringBuilder]::new();$b['Host']='127.0.0.1';$b['Port']=$pgPort;$b['Database']="nexa_review_it_${runId}_$suffix";$b['Username']=$user;$b['Password']=$password;return $b.ConnectionString}
function Start-App($name,$assembly,$working,$settings,$port){
    $info=[Diagnostics.ProcessStartInfo]::new('dotnet');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WorkingDirectory=$working
    $info.ArgumentList.Add($assembly);$info.ArgumentList.Add('--urls');$info.ArgumentList.Add("$(if($name-eq'Bff'){'https'}else{'http'})://127.0.0.1:$port")
    foreach($key in @($info.Environment.Keys)){if($key -match '^NEXACONNECT_(JOINED_|CASH_PORTAL_)'){$info.Environment.Remove($key)|Out-Null}}
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
    $ports=@(Get-ReviewJoinedFreePorts 7);$bffPort=$ports[5]
    Set-RunSetting NEXACONNECT_JOINED_RUN_ID $runId
    Set-RunSetting NEXACONNECT_JOINED_BFF_PORT $bffPort
    foreach($name in @('ADMIN_PASSWORD','MIGRATION_PASSWORD','RUNTIME_PASSWORD','KEYCLOAK_DB_PASSWORD','KEYCLOAK_ADMIN_PASSWORD','CLIENT_SECRET','READER_PASSWORD','RESOLVER_PASSWORD','RABBITMQ_PASSWORD')){Set-RunSetting "NEXACONNECT_JOINED_$name" ('Aa1!'+[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N'))}
    $existing=@(& $DockerExecutable @compose ps -aq);if($LASTEXITCODE-ne 0-or$existing.Count-ne 0){throw 'Generated project must be empty.'}
    $created=$true;& $DockerExecutable @compose up -d --wait --wait-timeout 180
    if($LASTEXITCODE-ne 0){throw 'Disposable infrastructure startup failed.'}
    $pgPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @compose port postgres 5432)
    $keycloakPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @compose port keycloak 8080)
    $brokerPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @compose port rabbitmq 5672)
    $proxyListener=$ports[6]

    $targets=@{PlatformDirectory=@('platform',3);Restaurant=@('restaurant',3);Authorization=@('authorization',7);POS=@('pos',7);Reporting=@('reporting',15)}
    foreach($e in $targets.GetEnumerator()){
        Set-RunSetting ('NEXACONNECT_'+$e.Key.ToUpperInvariant()+'_DB') (Connection $e.Value[0] nexaconnect_migration $env:NEXACONNECT_JOINED_MIGRATION_PASSWORD)
        & dotnet run --project (Join-Path $root 'src/Tools/NexaConnect.DataMigration') -- --service $e.Key --scripts-root (Join-Path $root 'src/Tools/NexaConnect.DataMigration/Scripts') --target $e.Value[1] --confirm --application-version 0.16.0
        if($LASTEXITCODE-ne 0){throw "Migration failed for $($e.Key)."}
    }
    # Identity provisioning uses the same run-specific imported realm as the joined Review harness.
    & $DockerExecutable @compose exec -T keycloak sh -c '/opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user acceptance-admin --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null'
    if($LASTEXITCODE-ne 0){throw 'Identity administrator authentication failed.'}
    $realm="nexa-review-it-$runId";$subjects=@{};$users=@{}
    foreach($role in @('reader','resolver')){
        $users[$role]="cash-$role-$($runId.Substring(0,8))";$user=$users[$role];$passwordVariable='NEXACONNECT_'+$role.ToUpperInvariant()+'_PASSWORD'
        $subject=& $DockerExecutable @compose exec -T keycloak sh -c "/opt/keycloak/bin/kcadm.sh create users -r '$realm' -s username='$user' -s firstName='Cash' -s lastName='Acceptance' -s email='$user@nexa.invalid' -s emailVerified=true -s enabled=true -i && /opt/keycloak/bin/kcadm.sh set-password -r '$realm' --username '$user' --new-password `"`$$passwordVariable`" >/dev/null"
        if($LASTEXITCODE-ne 0){throw 'Identity creation failed.'};$subjects[$role]=$subject.Trim()
    }
    $fixtureProject=Join-Path $root 'src/Tools/NexaConnect.CashClosePortalAcceptance'
    $fixtureDll=Join-Path $fixtureProject 'bin/Debug/net10.0/NexaConnect.CashClosePortalAcceptance.dll'
    $runtime=@{platform='platform_directory_app';restaurant='nexaconnect_restaurant_app';authorization='nexaconnect_authorization_app';pos='nexaconnect_pos_app';reporting='nexaconnect_reporting_app'}
    foreach($e in $runtime.GetEnumerator()){Set-RunSetting ('NEXACONNECT_CASH_PORTAL_DB_'+$e.Key.ToUpperInvariant()) (Connection $e.Key $e.Value $env:NEXACONNECT_JOINED_RUNTIME_PASSWORD)}
    $settings=@{ENABLED='1';CONFIRM_DISPOSABLE='1';RUN_ID=$runId;STATE_PATH=(Join-Path $run 'fixture.json');READER_SUBJECT=$subjects.reader;RESOLVER_SUBJECT=$subjects.resolver;FIXTURE_DLL=$fixtureDll}
    foreach($e in $settings.GetEnumerator()){Set-RunSetting "NEXACONNECT_CASH_PORTAL_$($e.Key)" $e.Value}
    if(-not $NoBuild){& dotnet build $fixtureProject --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw 'Fixture build failed.'}}
    & dotnet $fixtureDll provision | Out-Null;if($LASTEXITCODE-ne 0){throw 'Fixture provisioning failed.'}
    $fixture=Get-Content (Join-Path $run 'fixture.json') -Raw|ConvertFrom-Json
    $serviceNames=@('PlatformDirectory','Authorization','Restaurant','Reporting','POS')
    $urls=@{};for($i=0;$i-lt 5;$i++){$urls[$serviceNames[$i]]="http://127.0.0.1:$($ports[$i])/"}
    $authority="http://127.0.0.1:$keycloakPort/realms/$realm"
    $common=@{ASPNETCORE_ENVIRONMENT='Development';Authentication__Authority=$authority;Authentication__Audience='nexaconnect-api';Authentication__RequireHttpsMetadata='false';Services__PlatformDirectory=$urls.PlatformDirectory;Services__Authorization=$urls.Authorization;Services__Restaurant=$urls.Restaurant;WorkloadIdentity__Authority=$authority;WorkloadIdentity__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET;WorkloadIdentity__ClientId='nexaconnect-pos-service'}
    $broker="amqp://acceptance:$([Uri]::EscapeDataString($env:NEXACONNECT_JOINED_RABBITMQ_PASSWORD))@127.0.0.1:$brokerPort/"
    for($i=0;$i-lt 5;$i++){
        $name=$serviceNames[$i];$dir=Join-Path $root "src/Services/NexaConnect.Services.$name"
        if($name-eq'POS'){
            $bound=$false;$deadline=[DateTimeOffset]::UtcNow.AddSeconds(45)
            while([DateTimeOffset]::UtcNow-lt$deadline){
                $bindings=& $DockerExecutable @compose exec -T rabbitmq rabbitmqctl list_bindings source_name destination_name routing_key
                if($LASTEXITCODE-eq 0-and($bindings -match 'nexaconnect.events\s+nexaconnect.reporting.cash-close.v1\s+pos.cash-close.snapshot.v1')){$bound=$true;break}
                Start-Sleep -Seconds 1
            }
            if(-not $bound){throw 'Reporting binding must exist before POS publication starts.'}
        }
        if(-not $NoBuild){& dotnet build $dir --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw "$name build failed."}}
        $suffix=if($name-eq'PlatformDirectory'){'platform'}else{$name.ToLowerInvariant()}
        $config=$common.Clone();$config["ConnectionStrings__$name"]=[Environment]::GetEnvironmentVariable('NEXACONNECT_CASH_PORTAL_DB_'+$suffix.ToUpperInvariant())
        if($name-eq'PlatformDirectory'){$config.KeycloakAdmin__BaseUrl="http://127.0.0.1:$keycloakPort/";$config.KeycloakAdmin__Realm=$realm;$config.KeycloakAdmin__ClientId='platform-directory-admin';$config.KeycloakAdmin__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET}
        if($name-eq'POS'){$config.CashClosePublication__Enabled='true';$config.Outbox__Enabled='true';$config.Outbox__ConnectionString=$broker;$config.OrderSettlementConsumer__Enabled='false'}
        if($name-eq'Reporting'){$config.Services__POS="http://127.0.0.1:$proxyListener/";$config.CashCloseConsumer__Enabled='true';$config.CashCloseConsumer__ConnectionString=$broker;$config.ActivityConsumer__Enabled='false'}
        Start-App $name (Join-Path $dir "bin/Debug/net10.0/NexaConnect.Services.$name.dll") $dir $config $ports[$i]
    }
    $bffDir=Join-Path $run 'bff'
    & dotnet publish (Join-Path $root 'src/Gateway/NexaConnect.CustomerBff') --output $bffDir --verbosity minimal -m:1
    if($LASTEXITCODE-ne 0){throw 'BFF publish failed.'}
    $cert=Join-Path $run 'acceptance.pfx';$certPassword=[Guid]::NewGuid().ToString('N')
    & dotnet dev-certs https --export-path $cert --password $certPassword|Out-Null;if($LASTEXITCODE-ne 0){throw 'Acceptance certificate creation failed.'}
    $bff=$common+@{Bff__Authority=$authority;Bff__RequireHttpsMetadata='false';Bff__ClientId='nexaconnect-web-bff';Bff__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET;Services__Reporting=$urls.Reporting;Kestrel__Certificates__Default__Path=$cert;Kestrel__Certificates__Default__Password=$certPassword}
    Start-App Bff (Join-Path $bffDir 'NexaConnect.CustomerBff.dll') $bffDir $bff $bffPort
    $live=@{BASE_URL="https://127.0.0.1:$bffPort";OIDC_ISSUER=$authority;PROXY_PORT=$proxyListener;POS_PORT=$ports[4];READER_USERNAME=$users.reader;READER_PASSWORD=$env:NEXACONNECT_JOINED_READER_PASSWORD;RESOLVER_USERNAME=$users.resolver;RESOLVER_PASSWORD=$env:NEXACONNECT_JOINED_RESOLVER_PASSWORD}
    foreach($e in $live.GetEnumerator()){Set-RunSetting "NEXACONNECT_CASH_PORTAL_$($e.Key)" $e.Value}
    Push-Location (Join-Path $root 'src/Frontend')
    try{& npx playwright test --config playwright.cash-close-live.config.mjs;if($LASTEXITCODE-ne 0){throw 'Joined cash-close browser acceptance failed.'}}finally{Pop-Location}
    $summary=Get-Content (Join-Path $root "src/Frontend/test-results/cash-close-live/$runId/summary.json") -Raw|ConvertFrom-Json
    if(-not $summary.verified-or$summary.passed-ne 5-or$summary.total-ne 5){throw 'All five scenarios must pass without skips.'};$passed=$true
}
finally{
    $failed=$false
    foreach($state in $processes){try{if(-not $state.Process.HasExited){$state.Process.Kill($true);if(-not $state.Process.WaitForExit(10000)){throw 'Process cleanup timed out.'}};$state.Out.GetAwaiter().GetResult()|Set-Content (Join-Path $run "$($state.Name).log");$state.Error.GetAwaiter().GetResult()|Set-Content (Join-Path $run "$($state.Name).error.log")}catch{$failed=$true}}
    if($created){& $DockerExecutable @compose down --volumes --remove-orphans|Out-Null;if($LASTEXITCODE-ne 0){$failed=$true};$remaining=@(& $DockerExecutable @compose ps -aq);if($LASTEXITCODE-ne 0-or$remaining.Count-ne 0){$failed=$true}}
    $cleanup=-not $failed
    if(Test-Path $run){@{runId=$runId;passed=$passed;cleanupVerified=$cleanup;productionVerified=$false;sourceRevision=(& git -C $root rev-parse HEAD);sourceDirty=[bool](& git -C $root status --porcelain);completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $run 'verification.json')}
    if($cert-and(Test-Path -LiteralPath $cert)){Remove-Item -LiteralPath $cert}
    foreach($key in $previous.Keys){[Environment]::SetEnvironmentVariable($key,$previous[$key])}
    if($failed){throw 'Disposable process or infrastructure cleanup could not be verified.'}
}
Write-Output "Joined cash-close portal acceptance passed; evidence: $run/verification.json"
