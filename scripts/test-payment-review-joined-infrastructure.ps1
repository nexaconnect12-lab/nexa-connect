#requires -Version 7.0
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure,[string]$DockerExecutable='docker',[switch]$NoBuild,[switch]$RunLiveBrowser,[int]$StartupTimeoutSeconds=120)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm creation, fixture writes, and deletion of the generated joined environment with -ConfirmDisposableInfrastructure.'}
. (Join-Path $PSScriptRoot 'payment-review-joined-helpers.ps1')
$repositoryRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runId=[Guid]::NewGuid().ToString('N');$projectName="nexa-review-joined-$runId"
$composeArguments=@(Get-ReviewJoinedComposeArguments $repositoryRoot $projectName)
$runRoot=Join-Path $repositoryRoot ".runstate/payment-review-joined/$runId"
$liveKeys=@('ENABLED','CONFIRM_DISPOSABLE','RUN_ID','BASE_URL','OIDC_ISSUER','FAULT_CONTROL_URL','FAULT_PROXY_NAME','PROCESS_CONTROL_URL','PROCESS_CONTROL_TOKEN','RESOLVER_USERNAME','RESOLVER_PASSWORD','READER_USERNAME','READER_PASSWORD','ORGANIZATION_ID','OTHER_ORGANIZATION_ID','BRANCH_ID','CONCURRENCY_ORDER_ID','RESUME_ORDER_ID','VOID_ORDER_ID','OUTAGE_ORDER_ID','LOST_RESPONSE_ORDER_ID','INVENTORY_PROCESS_ORDER_ID','KITCHEN_PROCESS_ORDER_ID','COMBINED_PROCESS_ORDER_ID')
$names=@('NEXACONNECT_JOINED_ADMIN_PASSWORD','NEXACONNECT_JOINED_RUN_ID','NEXACONNECT_JOINED_MIGRATION_PASSWORD','NEXACONNECT_JOINED_RUNTIME_PASSWORD','NEXACONNECT_JOINED_KEYCLOAK_DB_PASSWORD','NEXACONNECT_JOINED_KEYCLOAK_ADMIN_PASSWORD','NEXACONNECT_JOINED_CLIENT_SECRET','NEXACONNECT_JOINED_READER_PASSWORD','NEXACONNECT_JOINED_RESOLVER_PASSWORD','NEXACONNECT_JOINED_RABBITMQ_PASSWORD','NEXACONNECT_JOINED_BFF_PORT','NEXACONNECT_REVIEW_PROCESS_CONTROL_TOKEN','NEXACONNECT_REVIEW_PROCESS_CONTROL_CONFIG','NEXACONNECT_REVIEW_FIXTURE_ENABLED','NEXACONNECT_REVIEW_FIXTURE_RUN_ID','NEXACONNECT_REVIEW_FIXTURE_READER_SUBJECT_ID','NEXACONNECT_REVIEW_FIXTURE_RESOLVER_SUBJECT_ID','NEXACONNECT_REVIEW_FIXTURE_PLATFORM_DIRECTORY_DB','NEXACONNECT_REVIEW_FIXTURE_RESTAURANT_DB','NEXACONNECT_REVIEW_FIXTURE_AUTHORIZATION_DB','NEXACONNECT_REVIEW_FIXTURE_ORDER_DB','NEXACONNECT_PLATFORMDIRECTORY_DB','NEXACONNECT_RESTAURANT_DB','NEXACONNECT_AUTHORIZATION_DB','NEXACONNECT_ORDER_DB')+@($liveKeys|ForEach-Object{"NEXACONNECT_REVIEW_LIVE_$_"})
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
$created=$false;$migrationsPassed=$false;$identityPassed=$false;$fixturePassed=$false;$applicationsPassed=$false;$proxyPassed=$false;$liveBrowserVerified=$false;$processCleanupPassed=-not $RunLiveBrowser;$cleanupPassed=$false;$fixture=$null;$processes=@()

function Start-JoinedApplication($definition){
    $saved=@{};try{
        foreach($entry in $definition.Environment.GetEnumerator()){$saved[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key);[Environment]::SetEnvironmentVariable($entry.Key,[string]$entry.Value)}
        $executable=if($definition.Executable){$definition.Executable}else{'dotnet'};$arguments=if($definition.Arguments){@($definition.Arguments)}else{@($definition.Assembly,'--urls',$definition.Url)}
        $log=Join-Path $runRoot "$($definition.Name).log";$process=Start-Process $executable -WindowStyle Hidden -ArgumentList $arguments -WorkingDirectory $definition.WorkingDirectory -RedirectStandardOutput $log -RedirectStandardError "$log.error" -PassThru
        return [pscustomobject]@{Name=$definition.Name;Id=$process.Id;StartTimeUtc=$process.StartTime.ToUniversalTime();Log=$log}
    }finally{foreach($entry in $definition.Environment.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$saved[$entry.Key])}}
}
function Wait-JoinedApplication($definition,$state){
    if($null-eq$state){throw "Joined process state is missing for $($definition.Name)."}
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while([DateTimeOffset]::UtcNow-lt$deadline){
        $running=Get-Process -Id $state.Id -ErrorAction SilentlyContinue;if($null-eq$running){throw "$($definition.Name) exited during startup. Inspect $($state.Log).error."}
        $client=[Net.Sockets.TcpClient]::new();try{$task=$client.ConnectAsync('127.0.0.1',$definition.Port);$connected=$task.Wait(1000);if($connected-and$client.Connected){return}}catch{}finally{$client.Dispose()};Start-Sleep -Milliseconds 500
    }
    throw "$($definition.Name) was not ready within $StartupTimeoutSeconds seconds."
}
function Stop-JoinedApplications{
    $failed=$false
    if($script:processControlUrl-and$script:processControlToken){try{Invoke-RestMethod -Method Post -Uri "$script:processControlUrl/shutdown" -Headers @{Authorization="Bearer $script:processControlToken"} -TimeoutSec 15|Out-Null}catch{$failed=$true}}
    foreach($state in @($processes)|Sort-Object Id -Descending){$process=Get-Process -Id $state.Id -ErrorAction SilentlyContinue;if($null-eq$process){continue};if($process.StartTime.ToUniversalTime()-ne$state.StartTimeUtc){$failed=$true;continue};try{Stop-Process -Id $state.Id -Force -ErrorAction Stop;$null=$process.WaitForExit(10000)}catch{$failed=$true}}
    foreach($state in $processes){if(Get-Process -Id $state.Id -ErrorAction SilentlyContinue){$failed=$true}}
    foreach($port in @($script:controlledPorts)){$client=[Net.Sockets.TcpClient]::new();try{$task=$client.ConnectAsync('127.0.0.1',$port);if($task.Wait(1000)-and$client.Connected){$failed=$true}}catch{}finally{$client.Dispose()}}
    if($failed){throw 'One or more joined application processes could not be verified as stopped.'}
}
try{
    $endpoint=$env:DOCKER_HOST
    if([string]::IsNullOrWhiteSpace($endpoint)){$endpoint=& $DockerExecutable context inspect --format '{{.Endpoints.docker.Host}}';if($LASTEXITCODE -ne 0){throw 'Could not inspect Docker context.'}}
    if($endpoint -notmatch '^(npipe|unix)://'){throw 'Joined acceptance requires a local Docker socket.'}
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    $applicationPorts=@(Get-ReviewJoinedFreePorts 8);$env:NEXACONNECT_JOINED_RUN_ID=$runId;$env:NEXACONNECT_JOINED_BFF_PORT=$applicationPorts[6].ToString()
    foreach($name in $names|Where-Object{$_ -match 'PASSWORD$|SECRET$'}){[Environment]::SetEnvironmentVariable($name,('Aa1!'+[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N')))}
    $existing=@(& $DockerExecutable @composeArguments ps -aq);if($LASTEXITCODE -ne 0 -or $existing.Count -ne 0){throw 'Generated joined project is not empty.'}
    $created=$true;& $DockerExecutable @composeArguments up -d --wait --wait-timeout 180
    if($LASTEXITCODE -ne 0){throw 'Joined infrastructure did not become healthy.'}
    $postgresPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @composeArguments port postgres 5432)
    $keycloakPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @composeArguments port keycloak 8080)
    $toxiproxyPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @composeArguments port toxiproxy 8474)
    $proxyListenerPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @composeArguments port toxiproxy 8666)
    $rabbitmqPort=ConvertFrom-ReviewJoinedPort (& $DockerExecutable @composeArguments port rabbitmq 5672)
    $scriptsRoot=Join-Path $repositoryRoot 'src/Tools/NexaConnect.DataMigration/Scripts';$migrationProject=Join-Path $repositoryRoot 'src/Tools/NexaConnect.DataMigration'
    $targets=@{PlatformDirectory=3;Restaurant=3;Authorization=5;Order=4}
    foreach($entry in $targets.GetEnumerator()){
        $suffix=if($entry.Key-eq'PlatformDirectory'){'platform'}else{$entry.Key.ToLowerInvariant()}
        $variable='NEXACONNECT_'+$entry.Key.ToUpperInvariant()+'_DB';[Environment]::SetEnvironmentVariable($variable,(New-ReviewJoinedConnection $postgresPort $runId $suffix 'nexaconnect_migration' $env:NEXACONNECT_JOINED_MIGRATION_PASSWORD))
        & dotnet run --no-restore --project $migrationProject -- --service $entry.Key --scripts-root $scriptsRoot --target $entry.Value --confirm --application-version 0.12.0
        if($LASTEXITCODE -ne 0){throw "Joined migration failed for $($entry.Key)."}
    }
    $migrationsPassed=$true
    & $DockerExecutable @composeArguments exec -T keycloak sh -c '/opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user acceptance-admin --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null'
    if($LASTEXITCODE -ne 0){throw 'Could not authenticate the disposable identity administrator.'}
    $shortRunId=$runId.Substring(0,8);$readerUser="review-reader-$shortRunId";$resolverUser="review-resolver-$shortRunId";$realm="nexa-review-it-$runId"
    $readerSubject=& $DockerExecutable @composeArguments exec -T keycloak sh -c "/opt/keycloak/bin/kcadm.sh create users -r '$realm' -s username='$readerUser' -s firstName='Review' -s lastName='Reader' -s email='$readerUser@nexa.invalid' -s emailVerified=true -s enabled=true -i && /opt/keycloak/bin/kcadm.sh set-password -r '$realm' --username '$readerUser' --new-password `"`$NEXACONNECT_READER_PASSWORD`" >/dev/null"
    if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($readerSubject)){throw 'Could not create the disposable reader identity.'}
    $resolverSubject=& $DockerExecutable @composeArguments exec -T keycloak sh -c "/opt/keycloak/bin/kcadm.sh create users -r '$realm' -s username='$resolverUser' -s firstName='Review' -s lastName='Resolver' -s email='$resolverUser@nexa.invalid' -s emailVerified=true -s enabled=true -i && /opt/keycloak/bin/kcadm.sh set-password -r '$realm' --username '$resolverUser' --new-password `"`$NEXACONNECT_RESOLVER_PASSWORD`" >/dev/null"
    if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolverSubject)){throw 'Could not create the disposable resolver identity.'}
    $identityPassed=$true
    $runtime=@{PLATFORM_DIRECTORY_DB=@('platform','platform_directory_app');RESTAURANT_DB=@('restaurant','nexaconnect_restaurant_app');AUTHORIZATION_DB=@('authorization','nexaconnect_authorization_app');ORDER_DB=@('order','nexaconnect_order_app')}
    $env:NEXACONNECT_REVIEW_FIXTURE_ENABLED='1';$env:NEXACONNECT_REVIEW_FIXTURE_RUN_ID=$runId;$env:NEXACONNECT_REVIEW_FIXTURE_READER_SUBJECT_ID=$readerSubject.Trim();$env:NEXACONNECT_REVIEW_FIXTURE_RESOLVER_SUBJECT_ID=$resolverSubject.Trim()
    foreach($entry in $runtime.GetEnumerator()){[Environment]::SetEnvironmentVariable('NEXACONNECT_REVIEW_FIXTURE_'+$entry.Key,(New-ReviewJoinedConnection $postgresPort $runId $entry.Value[0] $entry.Value[1] $env:NEXACONNECT_JOINED_RUNTIME_PASSWORD))}
    if(-not $NoBuild){& dotnet build (Join-Path $repositoryRoot 'src/Tools/NexaConnect.PaymentReviewAcceptance/NexaConnect.PaymentReviewAcceptance.csproj') --no-restore --verbosity minimal;if($LASTEXITCODE -ne 0){throw 'Fixture tool build failed.'}}
    $fixtureJson=& dotnet run --no-build --no-restore --project (Join-Path $repositoryRoot 'src/Tools/NexaConnect.PaymentReviewAcceptance')
    if($LASTEXITCODE -ne 0){throw 'Fixture tool failed.'};$fixture=$fixtureJson|ConvertFrom-Json;$fixturePassed=$true
    [ordered]@{runId=$runId;project=$projectName;postgresPort=$postgresPort;keycloakPort=$keycloakPort;toxiproxyPort=$toxiproxyPort;proxyListenerPort=$proxyListenerPort;rabbitmqPort=$rabbitmqPort;fixture=$fixture} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $runRoot 'fixture-identifiers.json')
    if($RunLiveBrowser){
        $ports=@{Platform=$applicationPorts[0];Authorization=$applicationPorts[1];Restaurant=$applicationPorts[2];Inventory=$applicationPorts[3];Kitchen=$applicationPorts[4];Order=$applicationPorts[5];Bff=$applicationPorts[6]}
        $projects=@{
            Platform='src/Services/NexaConnect.Services.PlatformDirectory';Authorization='src/Services/NexaConnect.Services.Authorization';Restaurant='src/Services/NexaConnect.Services.Restaurant';Inventory='src/Services/NexaConnect.Services.Inventory';Kitchen='src/Services/NexaConnect.Services.Kitchen';Order='src/Services/NexaConnect.Services.Order';Bff='src/Gateway/NexaConnect.CustomerBff'
        }
        $assemblies=@{Platform='NexaConnect.Services.PlatformDirectory.dll';Authorization='NexaConnect.Services.Authorization.dll';Restaurant='NexaConnect.Services.Restaurant.dll';Inventory='NexaConnect.Services.Inventory.dll';Kitchen='NexaConnect.Services.Kitchen.dll';Order='NexaConnect.Services.Order.dll'}
        $artifact=Join-Path $repositoryRoot '.runartifacts/payment-review-joined/customer-bff'
        if(-not $NoBuild){
            foreach($key in @('Platform','Authorization','Restaurant','Inventory','Kitchen','Order')){& dotnet build (Join-Path $repositoryRoot $projects[$key]) --no-restore --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw "Joined application build failed for $key."}}
            & dotnet publish (Join-Path $repositoryRoot $projects.Bff) --no-restore --output $artifact --verbosity minimal -m:1;if($LASTEXITCODE-ne 0){throw 'Joined Customer BFF publish failed.'}
        }
        $realm="nexa-review-it-$runId";$authority="http://127.0.0.1:$keycloakPort/realms/$realm";$common=@{ASPNETCORE_ENVIRONMENT='Development';Authentication__Authority=$authority;Authentication__Audience='nexaconnect-api';Authentication__RequireHttpsMetadata='false'}
        $runtimeConnections=@{Platform=New-ReviewJoinedConnection $postgresPort $runId platform platform_directory_app $env:NEXACONNECT_JOINED_RUNTIME_PASSWORD;Authorization=New-ReviewJoinedConnection $postgresPort $runId authorization nexaconnect_authorization_app $env:NEXACONNECT_JOINED_RUNTIME_PASSWORD;Restaurant=New-ReviewJoinedConnection $postgresPort $runId restaurant nexaconnect_restaurant_app $env:NEXACONNECT_JOINED_RUNTIME_PASSWORD;Order=New-ReviewJoinedConnection $postgresPort $runId order nexaconnect_order_app $env:NEXACONNECT_JOINED_RUNTIME_PASSWORD}
        $url=@{};foreach($key in $ports.Keys){$scheme=if($key-eq'Bff'){'https'}else{'http'};$url[$key]="${scheme}://127.0.0.1:$($ports[$key])"}
        $serviceCommon=@{Services__PlatformDirectory=$url.Platform+'/';Services__Authorization=$url.Authorization+'/';Services__Restaurant=$url.Restaurant+'/';WorkloadIdentity__Authority=$authority;WorkloadIdentity__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET}
        $definitions=@(
            @{Name='platform-directory';Key='Platform';Environment=$common+@{ConnectionStrings__PlatformDirectory=$runtimeConnections.Platform;KeycloakAdmin__BaseUrl="http://127.0.0.1:$keycloakPort/";KeycloakAdmin__Realm=$realm;KeycloakAdmin__ClientId='platform-directory-admin';KeycloakAdmin__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET}},
            @{Name='authorization';Key='Authorization';Environment=$common+@{ConnectionStrings__Authorization=$runtimeConnections.Authorization}},
            @{Name='restaurant';Key='Restaurant';Environment=$common+$serviceCommon+@{ConnectionStrings__Restaurant=$runtimeConnections.Restaurant}},
            @{Name='inventory';Key='Inventory';Environment=$common+$serviceCommon+@{Persistence__Provider='InMemory';WorkloadIdentity__ClientId='nexaconnect-inventory-service'}},
            @{Name='kitchen';Key='Kitchen';Environment=$common+$serviceCommon+@{Persistence__Provider='InMemory';WorkloadIdentity__ClientId='nexaconnect-kitchen-service'}},
            @{Name='order';Key='Order';Environment=$common+$serviceCommon+@{Persistence__Provider='PostgreSQL';ConnectionStrings__Order=$runtimeConnections.Order;Outbox__ConnectionString="amqp://acceptance:$([Uri]::EscapeDataString($env:NEXACONNECT_JOINED_RABBITMQ_PASSWORD))@127.0.0.1:$rabbitmqPort/";Workflow__UseHttpAdapters='true';Services__Inventory="http://127.0.0.1:$proxyListenerPort/";Services__Kitchen=$url.Kitchen+'/';Services__Catalog='http://127.0.0.1:9/';Services__Payment='http://127.0.0.1:9/';Authentication__TokenEndpoint="$authority/protocol/openid-connect/token";Authentication__ClientId='nexaconnect-order-service';Authentication__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET;WorkloadIdentity__ClientId='nexaconnect-order-service';PaymentReconciliationConsumer__Enabled='false'}},
            @{Name='customer-bff';Key='Bff';Environment=$common+@{Bff__Authority=$authority;Bff__RequireHttpsMetadata='false';Bff__ClientId='nexaconnect-web-bff';Bff__ClientSecret=$env:NEXACONNECT_JOINED_CLIENT_SECRET;Services__PlatformDirectory=$url.Platform+'/';Services__Order=$url.Order+'/';Services__Restaurant=$url.Restaurant+'/';Services__Authorization=$url.Authorization+'/';Services__Inventory=$url.Inventory+'/';Services__Catalog='http://127.0.0.1:9/';Services__Reporting='http://127.0.0.1:9/';Services__Media='http://127.0.0.1:9/'}}
        )
        foreach($definition in $definitions){
            $definition.Port=$ports[$definition.Key];$definition.Url=$url[$definition.Key]
            if($definition.Key-eq'Bff'){$definition.WorkingDirectory=$artifact;$definition.Assembly=Join-Path $artifact 'NexaConnect.CustomerBff.dll'}else{$definition.WorkingDirectory=Join-Path $repositoryRoot $projects[$definition.Key];$definition.Assembly=Join-Path $definition.WorkingDirectory "bin/Debug/net10.0/$($assemblies[$definition.Key])"}
            if(-not(Test-Path -LiteralPath $definition.Assembly)){throw "Joined application artifact is missing for $($definition.Name)."}
        }
        $controlled=@($definitions|Where-Object{$_.Key-in @('Inventory','Kitchen')});$script:controlledPorts=@($controlled.Port)
        $processControlToken=([Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N'));$processControlUrl="http://127.0.0.1:$($applicationPorts[7])";$script:processControlToken=$processControlToken;$script:processControlUrl=$processControlUrl
        $controlConfig=[ordered]@{controlPort=$applicationPorts[7];services=@($controlled|ForEach-Object{[ordered]@{name=$_.Name;assembly=$_.Assembly;workingDirectory=$_.WorkingDirectory;url=$_.Url;port=$_.Port;log=(Join-Path $runRoot "$($_.Name).log");environment=$_.Environment}})}|ConvertTo-Json -Depth 8 -Compress
        $controlDefinition=@{Name='process-control';Executable='node';Arguments=@((Join-Path $repositoryRoot 'src/Frontend/e2e/payment-review-live/process-control-server.mjs'));WorkingDirectory=$repositoryRoot;Environment=@{NEXACONNECT_REVIEW_PROCESS_CONTROL_TOKEN=$processControlToken;NEXACONNECT_REVIEW_PROCESS_CONTROL_CONFIG=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($controlConfig))};Port=$applicationPorts[7]}
        $processes+=Start-JoinedApplication $controlDefinition;Wait-JoinedApplication $controlDefinition $processes[-1]
        foreach($definition in @($definitions|Where-Object{$_.Key-notin @('Inventory','Kitchen')})){$processes+=Start-JoinedApplication $definition}
        foreach($definition in @($definitions|Where-Object{$_.Key-notin @('Inventory','Kitchen')})){$state=@($processes|Where-Object{$_.Name-eq$definition.Name})[0];Wait-JoinedApplication $definition $state}
        foreach($definition in $controlled){Wait-JoinedApplication $definition $processes[0]};$applicationsPassed=$true
        $proxyName="nexa-review-it-$runId-inventory";$control="http://127.0.0.1:$toxiproxyPort"
        $proxyBody=@{name=$proxyName;listen='0.0.0.0:8666';upstream="host.docker.internal:$($ports.Inventory)";enabled=$true}|ConvertTo-Json -Compress
        Invoke-RestMethod -Method Post -Uri "$control/proxies" -UserAgent 'toxiproxy-cli' -ContentType 'application/json' -Body $proxyBody|Out-Null;$proxyPassed=$true
        $live=@{ENABLED='1';CONFIRM_DISPOSABLE='1';RUN_ID=$runId;BASE_URL=$url.Bff+'/';OIDC_ISSUER=$authority;FAULT_CONTROL_URL=$control+'/';FAULT_PROXY_NAME=$proxyName;PROCESS_CONTROL_URL=$processControlUrl+'/';PROCESS_CONTROL_TOKEN=$processControlToken;RESOLVER_USERNAME=$resolverUser;RESOLVER_PASSWORD=$env:NEXACONNECT_JOINED_RESOLVER_PASSWORD;READER_USERNAME=$readerUser;READER_PASSWORD=$env:NEXACONNECT_JOINED_READER_PASSWORD;ORGANIZATION_ID=$fixture.organizationId;OTHER_ORGANIZATION_ID=$fixture.otherOrganizationId;BRANCH_ID=$fixture.branchId;CONCURRENCY_ORDER_ID=$fixture.concurrencyOrderId;RESUME_ORDER_ID=$fixture.resumeOrderId;VOID_ORDER_ID=$fixture.voidOrderId;OUTAGE_ORDER_ID=$fixture.outageOrderId;LOST_RESPONSE_ORDER_ID=$fixture.lostResponseOrderId;INVENTORY_PROCESS_ORDER_ID=$fixture.inventoryProcessOrderId;KITCHEN_PROCESS_ORDER_ID=$fixture.kitchenProcessOrderId;COMBINED_PROCESS_ORDER_ID=$fixture.combinedProcessOrderId}
        foreach($entry in $live.GetEnumerator()){[Environment]::SetEnvironmentVariable("NEXACONNECT_REVIEW_LIVE_$($entry.Key)",[string]$entry.Value)}
        Push-Location (Join-Path $repositoryRoot 'src/Frontend');try{& npm.cmd run test:e2e:payment-review:live;if($LASTEXITCODE-ne 0){throw 'Joined Payment Review browser verification failed.'}}finally{Pop-Location}
        $browserSummary=Get-Content -Raw (Join-Path $repositoryRoot "src/Frontend/test-results/payment-review-live/$runId/summary.json")|ConvertFrom-Json
        if($browserSummary.verified-ne$true-or$browserSummary.passed-ne 10-or$browserSummary.total-ne 10){throw 'Joined browser evidence is incomplete.'};$liveBrowserVerified=$true
    }
}
catch{
    if($created){
        & $DockerExecutable @composeArguments logs --no-color --tail 250 2>&1 |
            Where-Object{$_ -notmatch '(?i)(password|secret|token)\s*[=:]'} |
            Set-Content -LiteralPath (Join-Path $runRoot 'startup-diagnostics.log')
    }
    throw
}
finally{
    try{$cleanupErrors=@();if($RunLiveBrowser){try{Stop-JoinedApplications;$processCleanupPassed=$true}catch{$cleanupErrors+=$_.Exception.Message}};if($created){try{Assert-ReviewJoinedProject $projectName;& $DockerExecutable @composeArguments down --volumes --remove-orphans;if($LASTEXITCODE -ne 0){throw "Joined cleanup failed for $projectName."};$remaining=@(& $DockerExecutable @composeArguments ps -aq);if($LASTEXITCODE -ne 0 -or $remaining.Count -ne 0){throw 'Joined cleanup could not be verified.'};$cleanupPassed=$true}catch{$cleanupErrors+=$_.Exception.Message}};if($cleanupErrors.Count){throw ($cleanupErrors-join ' ')}}
    finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])};if(Test-Path -LiteralPath $runRoot){[ordered]@{runId=$runId;project=$projectName;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');migrationsPassed=$migrationsPassed;identityPassed=$identityPassed;fixturePassed=$fixturePassed;applicationsPassed=$applicationsPassed;proxyPassed=$proxyPassed;processCleanupPassed=$processCleanupPassed;cleanupPassed=$cleanupPassed;liveBrowserVerified=$liveBrowserVerified}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $runRoot 'summary.json');Write-Output "Sanitized joined infrastructure summary: $runRoot"}}
}
