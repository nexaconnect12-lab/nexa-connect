#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('Validate','Start','Status','Stop','Reset','Restart')][string]$Action='Validate',
    [string]$RunId,
    [ValidateSet('cash_manual','promptpay_manual')][string]$PaymentMethod='cash_manual',
    [string]$QrImagePath,
    [ValidateSet('PlatformDirectory','Authorization','Restaurant','Catalog','Inventory','Kitchen','Payment','POS','Reporting','Order','Bff')][string]$Service='Kitchen',
    [switch]$ConfirmTestResources,
    [switch]$NoBuild
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'payment-review-joined-helpers.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$catalog=Get-DraftServiceCatalog $root
$compatibility=($catalog.Values | Sort-Object { [version]$_.minimumApplicationVersion })[-1].minimumApplicationVersion
if ($Action -eq 'Validate') {
    Assert-DraftPayment $PaymentMethod $QrImagePath
    [pscustomobject]@{paymentMethod=$PaymentMethod;currency='THB';timeZone='Asia/Singapore';applicationCompatibility=$compatibility;schemaTargets=$catalog;providerCheckoutEnabled=$false}
    return
}
if (-not $IsWindows) { throw 'Use the local Windows PC for this draft lifecycle.' }
if (-not $RunId) {
    if ($Action -ne 'Start') { throw 'Specify the RunId printed by Start.' }
    $RunId=[Guid]::NewGuid().ToString('N')
}
$run=Get-DraftRunDirectory $root $RunId
$statePath=Join-Path $run 'state.json'
$secretPath=Join-Path $run 'secrets.clixml'
$compose=Get-DraftComposeArguments $root $RunId
$project="nexa-draft-$RunId"
function Save-State { $script:state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath }
function Assert-LocalDocker {
    if ($env:DOCKER_HOST) { throw 'Remove DOCKER_HOST; this lifecycle requires the recognized local Docker context.' }
    $endpoint=& docker context inspect --format '{{.Endpoints.docker.Host}}'
    if ($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^npipe://') { throw 'A local Docker Desktop named pipe is required.' }
}
function Invoke-OwnedCompose([string[]]$Arguments) {
    & docker @compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Owned draft Compose operation failed.' }
}
function Assert-ContainerOwnership {
    $ids=@(& docker @compose ps -aq)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect owned draft resources.' }
    foreach ($id in $ids) {
        if ($id -notmatch '^[a-f0-9]{12,64}$') { throw 'Invalid draft container identity.' }
        $owner=& docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' $id
        if ($LASTEXITCODE -ne 0 -or $owner -ne $project) { throw 'Draft container owner mismatch.' }
    }
    foreach($name in @('application-data','identity-data','broker-data','session-data')) {
        $volume="${project}_$name"
        $existing=@(& docker volume ls -q --filter "name=^$([regex]::Escape($volume))$")
        if($LASTEXITCODE -ne 0){throw 'Draft volume lookup failed.'}
        if($existing.Count) {
            $owner=& docker volume inspect --format '{{ index .Labels "com.docker.compose.project" }}' $volume
            if($LASTEXITCODE -ne 0 -or $owner -ne $project){throw 'Draft volume owner mismatch.'}
        }
    }
}
function Stop-DraftApp([string]$Name) {
    if (-not $script:state.processes.Contains($Name)) { return }
    $record=$script:state.processes[$Name]
    $process=Get-Process -Id $record.id -ErrorAction SilentlyContinue
    if (Assert-DraftProcess $record $process) {
        $actual=(Get-CimInstance Win32_Process -Filter "ProcessId = $($record.id)")
        $assembly=Join-Path $run "apps/$Name/$($record.assemblyName).dll"
        if (-not $actual -or $actual.ExecutablePath -ne (Get-Command dotnet).Source -or
            $actual.CommandLine -notlike "*`"$assembly`"*") { throw 'Draft executable identity mismatch; no process was stopped.' }
        $process.Kill()
        if (-not $process.WaitForExit(10000)) { throw 'Draft application did not stop.' }
    }
    $script:state.processes.Remove($Name)
    Save-State
}
function Start-DraftApp([string]$Name) {
    $settings=$script:configurations[$Name]
    $directory=Join-Path $run "apps/$Name"
    $assemblyName=if($Name -eq 'Bff'){'NexaConnect.CustomerBff'}else{"NexaConnect.Services.$Name"}
    $assembly=Join-Path $directory "$assemblyName.dll"
    if (-not (Test-Path -LiteralPath $assembly)) { throw "Missing draft artifact for $Name; start with build enabled." }
    $listenerPort=([uri]$script:state.urls[$Name]).Port
    $occupied=@(Get-NetTCPConnection -LocalPort $listenerPort -State Listen -ErrorAction SilentlyContinue)
    if($occupied.Count){throw "Draft $Name port is occupied; no existing listener will be adopted."}
    $old=@{}
    try {
        # The child receives only explicit configuration, not existing development secrets.
        foreach ($key in @([Environment]::GetEnvironmentVariables('Process').Keys)) {
            if ($key -match '__|^NEXACONNECT_|^NEXA_|^MINIO_|^ASPNETCORE_|^DOTNET_ENVIRONMENT$|^OTEL_') {
                $old[$key]=[Environment]::GetEnvironmentVariable($key,'Process')
                [Environment]::SetEnvironmentVariable($key,$null,'Process')
            }
        }
        foreach ($entry in $settings.GetEnumerator()) {
            if (-not $old.ContainsKey($entry.Key)) { $old[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key,'Process') }
            [Environment]::SetEnvironmentVariable($entry.Key,[string]$entry.Value,'Process')
        }
        $url=$script:state.urls[$Name]
        $process=Start-Process (Get-Command dotnet).Source -ArgumentList @("`"$assembly`"",'--urls',$url) -WorkingDirectory $directory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run "$Name.log") -RedirectStandardError (Join-Path $run "$Name.error.log")
        $script:state.processes[$Name]=@{id=$process.Id;startTicks=$process.StartTime.ToUniversalTime().Ticks;assemblyName=$assemblyName}
        Save-State
    } finally { Restore-DraftEnvironment $old }
    $path=if($Name -in @('Bff','Reporting','POS','Order')){'health/live'}elseif($Name -eq 'Payment'){'health/ready'}elseif($Name -eq 'PlatformDirectory'){'health'}else{'openapi/v1.json'}
    $probeHeaders=@{}
    if($path -eq 'openapi/v1.json') {
        $probeToken=Invoke-RestMethod ($script:state.authority+'/protocol/openid-connect/token') -Method Post -Body @{grant_type='client_credentials';client_id='nexaconnect-pos-service';client_secret=$script:secrets.CLIENT_SECRET}
        $probeHeaders.Authorization='Bearer '+$probeToken.access_token
    }
    Wait-DraftHttp ($url+$path) 90 $probeHeaders
    $listeners=@(Get-NetTCPConnection -LocalPort $listenerPort -State Listen -ErrorAction Stop | Where-Object OwningProcess -eq $process.Id)
    if($listeners.Count -eq 0){throw 'The ready listener does not belong to the recorded draft process.'}
    Write-Output "$Name HTTP ready."
}
function New-Connection([string]$Name,[bool]$Migration=$false) {
    $suffix=if($Name -eq 'PlatformDirectory'){'platform'}else{$Name.ToLowerInvariant()}
    $user=if($Migration){'nexaconnect_migration'}elseif($Name -eq 'PlatformDirectory'){'platform_directory_app'}else{'nexaconnect_'+$suffix+'_app'}
    $password=if($Migration){$script:secrets.MIGRATION_PASSWORD}else{$script:secrets.RUNTIME_PASSWORD}
    $connection=[System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host']='127.0.0.1';$connection['Port']=$script:state.infrastructure.postgres
    $connection['Database']="nexa_review_it_${RunId}_$suffix";$connection['Username']=$user;$connection['Password']=$password
    return $connection.ConnectionString
}
function Set-Configurations {
    $authority=$script:state.authority
    $broker="amqp://acceptance:$([Uri]::EscapeDataString($script:secrets.RABBITMQ_PASSWORD))@127.0.0.1:$($script:state.infrastructure.rabbitmq)/"
    $script:configurations=@{}
    foreach ($name in @($catalog.Keys)+@('Bff')) {
        $config=@{ASPNETCORE_ENVIRONMENT='Development';Authentication__Authority=$authority;Authentication__Audience='nexaconnect-api';Authentication__RequireHttpsMetadata='false';Persistence__Provider='PostgreSQL';Observability__OtlpEnabled='false';CardCheckout__EnableOmiseTestCheckout='false';PaymentProvider__Adapter='Disabled';OmiseWebhooks__Enabled='false';Outbox__Enabled='false';Outbox__BatchSize='0'}
        foreach ($dependency in $catalog.Keys) { $config['Services__'+$dependency]=$script:state.urls[$dependency] }
        $config.Services__Media='https://media.example.invalid/'
        if($name -ne 'Bff'){$config['ConnectionStrings__'+$name]=New-Connection $name}
        $config.WorkloadIdentity__Authority=$authority
        $config.WorkloadIdentity__ClientId='nexaconnect-'+$name.ToLowerInvariant()+'-service'
        $config.WorkloadIdentity__ClientSecret=$script:secrets.CLIENT_SECRET
        $config.Outbox__ConnectionString=$broker
        if($name -eq 'PlatformDirectory') {
            $config.KeycloakAdmin__BaseUrl="http://127.0.0.1:$($script:state.infrastructure.keycloak)/"
            $config.KeycloakAdmin__Realm=$script:state.realm
            $config.KeycloakAdmin__ClientId='platform-directory-admin';$config.KeycloakAdmin__ClientSecret=$script:secrets.CLIENT_SECRET
        }
        if($name -eq 'Order') {
            $config.Workflow__UseHttpAdapters='true';$config.WorkflowRecovery__Enabled='true'
            $config.Authentication__TokenEndpoint="$authority/protocol/openid-connect/token"
            $config.Authentication__ClientId='nexaconnect-order-service';$config.Authentication__ClientSecret=$script:secrets.CLIENT_SECRET
            $config.PaymentReconciliationConsumer__Enabled='true';$config.PaymentReconciliationConsumer__ConnectionString=$broker
        }
        if($name -eq 'POS') {
            $config.OrderSettlementConsumer__Enabled='true';$config.OrderSettlementConsumer__ConnectionString=$broker
            $config.CashClosePublication__Enabled='true';$config.SettlementRecovery__Enabled='true'
        }
        if($name -eq 'Reporting') {
            foreach ($consumer in @('OrderSaleConsumer','PaymentRefundConsumer','CashCloseConsumer','CashCorrectionConsumer','SettlementConsumer','ActivityConsumer')) {
                $config[$consumer+'__Enabled']='true';$config[$consumer+'__ConnectionString']=$broker
            }
        }
        if($name -eq 'Payment'){$config.PaymentProvider__RecoveryEnabled='false';$config.PaymentProvider__RefundRecoveryEnabled='false'}
        if($name -eq 'Bff') {
            $config.Bff__Authority=$authority;$config.Bff__RequireHttpsMetadata='false';$config.Bff__ClientId='nexaconnect-web-bff';$config.Bff__ClientSecret=$script:secrets.CLIENT_SECRET
            $config.Kestrel__Certificates__Default__Path=Join-Path $run 'local.pfx';$config.Kestrel__Certificates__Default__Password=$script:secrets.CERT_PASSWORD
            $config.ConnectionStrings__BffSessionCache="127.0.0.1:$($script:state.infrastructure.redis),password=$($script:secrets.REDIS_PASSWORD),abortConnect=false"
            $config.BffSessionCache__InstanceName="nexa:draft:$RunId:customer:"
        }
        $script:configurations[$name]=$config
    }
}
function Wait-Bindings {
    $required=@{
        'nexaconnect.reporting.order-sales.v1'='order.sale-completed.v1'
        'nexaconnect.reporting.payment-refunds.v1'='payment.refunded.v1'
        'nexaconnect.reporting.cash-close.v1'='pos.cash-close.snapshot.v1'
        'nexaconnect.reporting.cash-corrections.v1'='pos.late-cash-correction-posted.v1'
        'nexaconnect.reporting.day-settlements.v1'='pos.branch-day-settled.v2'
        'nexaconnect.pos.order-manual-tender.v1'='order.manual-tender-settled.v1'
        'nexaconnect.order.payment-reconciled.v1'='payment.capture-reconciled.v1'
    }
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(60)
    while([DateTimeOffset]::UtcNow -lt $deadline) {
        $bindings=(& docker @compose exec -T rabbitmq gosu rabbitmq rabbitmqctl list_bindings source_name destination_name routing_key 2>$null) -join "`n"
        $bindingsExit=$LASTEXITCODE
        $queues=(& docker @compose exec -T rabbitmq gosu rabbitmq rabbitmqctl list_queues name consumers 2>$null) -join "`n"
        $missing=@($required.Keys | Where-Object {
            $bindings -notmatch ('nexaconnect\.events\s+'+[regex]::Escape($_)+'\s+'+[regex]::Escape($required[$_])+'(?:\s|$)') -or
            $queues -notmatch ('(?m)^'+[regex]::Escape($_)+'\s+[1-9][0-9]*\s*$')
        })
        if($bindingsExit -eq 0 -and $LASTEXITCODE -eq 0 -and $missing.Count -eq 0){return}
        Start-Sleep -Seconds 1
    }
    throw 'Required owned financial queue bindings are not ready; producers remain disabled.'
}
if(-not (Test-Path -LiteralPath $run)) {
    if($Action -ne 'Start' -or -not $ConfirmTestResources){throw 'A new draft requires Start -ConfirmTestResources.'}
    Assert-DraftPayment $PaymentMethod $QrImagePath
    New-Item -ItemType Directory -Path $run | Out-Null
    Protect-DraftDirectory $run
}
$operationLock=Open-DraftLifecycleLock $run
try {
$newRun=-not (Test-Path -LiteralPath $statePath)
if ($newRun -and $Action -ne 'Start') { throw 'Owned draft state does not exist.' }
if($newRun) {
    if(-not $ConfirmTestResources){throw 'Start requires -ConfirmTestResources for isolated generated resources and migrations.'}
    if(Test-Path -LiteralPath $secretPath){throw 'Draft state is missing but protected credentials exist; preserve recovery material rather than reinitialize this run.'}
    Assert-DraftPayment $PaymentMethod $QrImagePath
    Protect-DraftDirectory $run
    Set-Content -LiteralPath (Join-Path $run 'compose.env') -Value '# No repository environment import.'
    $secrets=@{}
    foreach($name in @('ADMIN_PASSWORD','MIGRATION_PASSWORD','RUNTIME_PASSWORD','KEYCLOAK_DB_PASSWORD','KEYCLOAK_ADMIN_PASSWORD','CLIENT_SECRET','READER_PASSWORD','RESOLVER_PASSWORD','SECONDMANAGER_PASSWORD','ACCOUNTANT_PASSWORD','RABBITMQ_PASSWORD','REDIS_PASSWORD','CERT_PASSWORD')){$secrets[$name]='Aa1!'+[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N')}
    Save-DraftSecrets $secretPath $secrets
    $free=@(Get-ReviewJoinedFreePorts 11);$urls=[ordered]@{};$index=0
    foreach($name in $catalog.Keys){$urls[$name]="http://127.0.0.1:$($free[$index])/";$index++}
    $urls.Bff="https://127.0.0.1:$($free[10])/"
    $state=@{runId=$RunId;project=$project;status='Starting';paymentMethod=$PaymentMethod;realm="nexa-review-it-$RunId";urls=$urls;processes=@{};infrastructure=@{};authority='';applicationCompatibility=$compatibility;schemaTargets=$catalog;provisionedBranch=$false}
    Save-State
} else {
    $state=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -AsHashtable
    if($state.runId -ne $RunId -or $state.project -ne $project -or $state.status -eq 'Reset'){throw 'Invalid or reset draft state.'}
    $secrets=Read-DraftSecrets $secretPath
}
Assert-DraftUrls $state.urls
$previous=Set-DraftComposeEnvironment $secrets $RunId ([uri]$state.urls.Bff).Port
try {
    Assert-LocalDocker
    Assert-ContainerOwnership
    if($newRun) {
        $existing=@(& docker ps -aq --filter "label=com.docker.compose.project=$project")
        $existingVolumes=@(& docker volume ls -q --filter "label=com.docker.compose.project=$project")
        if($LASTEXITCODE -ne 0 -or $existing.Count -or $existingVolumes.Count){throw 'New draft project must be empty; existing resources cannot be adopted.'}
    }
    if($Action -eq 'Status') {
        $health=[ordered]@{}
        foreach($name in $state.processes.Keys){$record=$state.processes[$name];$actual=Get-Process -Id $record.id -ErrorAction SilentlyContinue;$health[$name]=Assert-DraftProcess $record $actual}
        [pscustomobject]@{runId=$RunId;status=$state.status;applicationProcesses=$health;portal=$state.urls.Bff;branchProvisioned=$state.provisionedBranch}
        & docker @compose ps --format '{{.Service}} {{.State}} {{.Health}}'
        return
    }
    if($Action -in @('Stop','Reset')) {
        if($Action -eq 'Reset' -and -not $ConfirmTestResources){throw 'Reset requires -ConfirmTestResources; only this draft project data is removed.'}
        foreach($name in @($state.processes.Keys)){Stop-DraftApp $name}
        if($Action -eq 'Reset') {
            Invoke-OwnedCompose @('down','--volumes','--remove-orphans')
            $remaining=@(& docker ps -aq --filter "label=com.docker.compose.project=$project")
            $volumes=@(& docker volume ls -q --filter "label=com.docker.compose.project=$project")
            if($LASTEXITCODE -ne 0 -or $remaining.Count -or $volumes.Count){throw 'Draft resource cleanup could not be verified.'}
            $state.status='Reset';Save-State
            Remove-Item -LiteralPath $secretPath
            if(Test-Path -LiteralPath (Join-Path $run 'local.pfx')){Remove-Item -LiteralPath (Join-Path $run 'local.pfx')}
        } else {Invoke-OwnedCompose @('stop');$state.status='Stopped';Save-State}
        Write-Output "Draft $Action verified for $RunId."
        return
    }
    if($Action -eq 'Restart') {
        if($state.status -ne 'Ready'){throw 'Restart requires a Ready draft.'}
        Set-Configurations
        foreach($name in $configurations.Keys){if($name -notin @('Authorization','Reporting','Bff')){$configurations[$name].Outbox__Enabled='true';$configurations[$name].Outbox__BatchSize='100'}}
        Stop-DraftApp $Service;Start-DraftApp $Service
        Wait-Bindings
        return
    }
    if($state.processes.Count){throw 'Draft has recorded application processes; Stop before Start.'}
    if($NoBuild -and -not (Test-Path -LiteralPath (Join-Path $run 'pos/appsettings.json'))){throw 'NoBuild requires a previously completed draft publication including POS.'}
    $state.status='Starting';Save-State
    if(-not $NoBuild) {
        foreach($name in $catalog.Keys) {
            & dotnet publish (Join-Path $root "src/Services/NexaConnect.Services.$name") -c Release --output (Join-Path $run "apps/$name") --verbosity quiet -m:1
            if($LASTEXITCODE -ne 0){throw "Build failed for $name."}
        }
        & dotnet publish (Join-Path $root 'src/Gateway/NexaConnect.CustomerBff') -c Release --output (Join-Path $run 'apps/Bff') --verbosity quiet -m:1
        if($LASTEXITCODE -ne 0){throw 'Customer BFF/portal publication failed.'}
        & dotnet publish (Join-Path $root 'src/Tools/NexaConnect.DataMigration') -c Release --output (Join-Path $run 'migration') --verbosity quiet -m:1
        if($LASTEXITCODE -ne 0){throw 'Migration tool publication failed.'}
    }
    if(-not (Test-Path -LiteralPath (Join-Path $run 'local.pfx'))) {
        & dotnet dev-certs https --export-path (Join-Path $run 'local.pfx') --password $secrets.CERT_PASSWORD | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Local draft certificate export failed.'}
    }
    Invoke-OwnedCompose @('up','-d','--wait','--wait-timeout','180')
    foreach($entry in @{postgres=5432;keycloak=8080;rabbitmq=5672;redis=6379}.GetEnumerator()){
        $published=& docker @compose port $entry.Key $entry.Value
        if($LASTEXITCODE -ne 0){throw 'Infrastructure port lookup failed.'}
        $state.infrastructure[$entry.Key]=ConvertFrom-ReviewJoinedPort $published
    }
    $state.authority="http://127.0.0.1:$($state.infrastructure.keycloak)/realms/$($state.realm)";Save-State
    Wait-DraftHttp ($state.authority+'/.well-known/openid-configuration')
    foreach($name in @('Catalog','Inventory','Kitchen','Order','POS','Payment','Reporting')) {
        $token=Invoke-RestMethod ($state.authority+'/protocol/openid-connect/token') -Method Post -Body @{grant_type='client_credentials';client_id=('nexaconnect-'+$name.ToLowerInvariant()+'-service');client_secret=$secrets.CLIENT_SECRET}
        $payload=$token.access_token.Split('.')[1].Replace('-','+').Replace('_','/')
        $payload=$payload.PadRight($payload.Length+(4-$payload.Length%4)%4,'=')
        $claims=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
        if(@($claims.aud) -notcontains 'nexaconnect-api'){throw 'Draft workload audience validation failed.'}
    }
    foreach($name in $catalog.Keys) {
        $key='NEXACONNECT_'+$name.ToUpperInvariant()+'_DB';$old=[Environment]::GetEnvironmentVariable($key,'Process')
        try {
            [Environment]::SetEnvironmentVariable($key,(New-Connection $name $true),'Process')
            $argsList=@((Join-Path $run 'migration/NexaConnect.DataMigration.dll'),'--service',$name,'--scripts-root',(Join-Path $root 'src/Tools/NexaConnect.DataMigration/Scripts'),'--target',[string]$catalog[$name].version,'--application-version',$compatibility)
            & dotnet @argsList --plan | Out-Null;if($LASTEXITCODE -ne 0){throw "Migration preview failed for $name."}
            & dotnet @argsList --confirm | Out-Null;if($LASTEXITCODE -ne 0){throw "Migration apply failed for $name."}
        } finally {[Environment]::SetEnvironmentVariable($key,$old,'Process')}
        # Runtime roles must never retain history writes granted by init defaults.
        $suffix=if($name -eq 'PlatformDirectory'){'platform'}else{$name.ToLowerInvariant()}
        $role=if($name -eq 'PlatformDirectory'){'platform_directory_app'}else{'nexaconnect_'+$suffix+'_app'}
        $sql="REVOKE ALL ON nexaconnect_schema_migrations FROM $role; GRANT SELECT(version) ON nexaconnect_schema_migrations TO $role;"
        $sql | & docker @compose exec -T postgres psql -U postgres -d "nexa_review_it_${RunId}_$suffix" -v ON_ERROR_STOP=1 | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Runtime migration-history grant restriction failed.'}
    }
    Set-Configurations
    # Bind all consumers while producers are disabled; Order also owns a consumer.
    foreach($name in @('PlatformDirectory','Authorization','Restaurant','Catalog','Inventory','Kitchen','Payment','Reporting','POS','Order')){Start-DraftApp $name}
    Wait-Bindings
    foreach($name in @('PlatformDirectory','Restaurant','Catalog','Inventory','Kitchen','Payment','POS','Order')) {
        Stop-DraftApp $name;$configurations[$name].Outbox__Enabled='true';$configurations[$name].Outbox__BatchSize='100';Start-DraftApp $name
    }
    Start-DraftApp 'Bff'
    # Identity-only login smoke subject; product membership/roles are slice 2.
    $admin=Invoke-RestMethod "http://127.0.0.1:$($state.infrastructure.keycloak)/realms/master/protocol/openid-connect/token" -Method Post -Body @{grant_type='password';client_id='admin-cli';username='acceptance-admin';password=$secrets.KEYCLOAK_ADMIN_PASSWORD}
    $headers=@{Authorization='Bearer '+$admin.access_token};$adminBase="http://127.0.0.1:$($state.infrastructure.keycloak)/admin/realms/$($state.realm)"
    $users=@(Invoke-RestMethod "$adminBase/users?username=draft-smoke&exact=true" -Headers $headers | ForEach-Object { $_ })
    if($users.Count -eq 0) {
        $user=@{username='draft-smoke';enabled=$true;emailVerified=$true;firstName='Draft';lastName='Tester';email='draft-smoke@nexa.invalid';credentials=@(@{type='password';value=$secrets.READER_PASSWORD;temporary=$false})}
        Invoke-RestMethod "$adminBase/users" -Method Post -Headers $headers -ContentType 'application/json' -Body ($user|ConvertTo-Json -Depth 5) | Out-Null
        $users=@(Invoke-RestMethod "$adminBase/users?username=draft-smoke&exact=true" -Headers $headers | ForEach-Object { $_ })
    }
    if($users.Count -ne 1){throw 'Owned smoke user identity could not be verified.'}
    # Idempotently repair a partial identity creation; no product role is granted.
    $role=Invoke-RestMethod "$adminBase/roles/customer-viewer" -Headers $headers
    Invoke-RestMethod "$adminBase/users/$($users[0].id)/role-mappings/realm" -Method Post -Headers $headers -ContentType 'application/json' -Body (ConvertTo-Json -InputObject @($role)) | Out-Null
    $state.smokeSubject=$users[0].id
    # Separate generated POS artifacts never overwrite a current terminal's settings/state.
    if(-not $NoBuild) {
        $savedPosPath=Join-Path $run 'pos-configuration.json'
        $existingPos=if(Test-Path -LiteralPath $savedPosPath){Get-Content -LiteralPath $savedPosPath -Raw | ConvertFrom-Json}else{$null}
        & dotnet publish (Join-Path $root 'src/Clients/NexaConnect.POS') -c Release --output (Join-Path $run 'pos') --verbosity quiet -m:1
        if($LASTEXITCODE -ne 0){throw 'POS publication failed.'}
        $pos=if($existingPos){$existingPos}else{Get-Content (Join-Path $run 'pos/appsettings.json') -Raw | ConvertFrom-Json}
        $pos.Identity.Authority=$state.authority;$pos.Pos.PaymentMethod=$state.paymentMethod;$pos.Pos.EnableOmiseTestCheckout=$false;$pos.Pos.Currency='THB'
        if(-not $existingPos){foreach($name in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){$pos.Pos.$name=[Guid]::NewGuid().ToString()};$pos.Pos.PromptPayQrImagePath=''}
        if($state.paymentMethod -eq 'promptpay_manual' -and -not $existingPos) {
            Assert-DraftPayment $state.paymentMethod $QrImagePath
            $asset=Join-Path $run ('pos/promptpay-test'+[IO.Path]::GetExtension($QrImagePath));Copy-Item -LiteralPath $QrImagePath -Destination $asset
            $pos.Pos.PromptPayQrImagePath=$asset
        }
        $pos.Services.PosApi=$state.urls.POS;$pos.Services.OrderApi=$state.urls.Order;$pos.Services.PaymentApi=$state.urls.Payment;$pos.Services.CatalogApi=$state.urls.Catalog
        $pos | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'pos/appsettings.json')
        $pos | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $savedPosPath
    }
    $state.status='Ready';Save-State
    Write-Output "Draft Ready. RunId=$RunId Portal=$($state.urls.Bff)"
    Write-Output 'Branch/menu/roles are not provisioned. Complete slice 2 before launching POS. Configuration/credentials/logs remain in the protected run directory.'
} catch {
    if($Action -in @('Start','Restart')) {
        $state.status='Failed';Save-State
        # Preserve evidence/data for safe diagnosis; only owned application processes stop.
        foreach($name in @($state.processes.Keys)){try{Stop-DraftApp $name}catch{Write-Warning 'Owned process cleanup requires review.'}}
    }
    throw
} finally {Restore-DraftEnvironment $previous}
} finally {$operationLock.Dispose()}
