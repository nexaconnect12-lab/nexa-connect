#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [Uri] $PublicWebhookUrl,
    [ValidateRange(1024,65535)] [int] $ListenerPort = 5272,
    [decimal] $Amount = 50.00,
    [switch] $ConfirmDisposableInfrastructure,
    [switch] $ConfirmProcessTermination,
    [switch] $ConfirmSandboxTransaction,
    [switch] $ConfirmDashboardWebhookConfigured,
    [switch] $ValidateOnly,
    [string] $DockerExecutable = 'docker'
)

$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
if(-not $IsWindows){throw 'Live Omise webhook acceptance currently requires Windows for exact process-tree termination.'}
if($PublicWebhookUrl.Scheme -cne 'https' -or $PublicWebhookUrl.UserInfo -or $PublicWebhookUrl.Query -or $PublicWebhookUrl.Fragment -or
    $PublicWebhookUrl.AbsolutePath -cne '/api/payment/v1/webhooks/omise' -or
    $PublicWebhookUrl.Host -in @('localhost','127.0.0.1','::1') -or
    $PublicWebhookUrl.Host -match '(?i)(^|[.\-_])(prod|production)([.\-_]|$)'){
    throw 'Use the exact non-production, publicly trusted HTTPS Omise callback URL ending /api/payment/v1/webhooks/omise.'
}
if($Amount-le 0-or$Amount-gt 10000-or[decimal]::Truncate($Amount*100)-ne$Amount*100){throw 'Use an exact two-decimal THB amount between 0.01 and 10000.'}
if($env:NEXACONNECT_OMISE_TEST_SECRET_KEY-cnotmatch '\Askey_test_[a-z0-9]{10,64}\z'){throw 'Inject NEXACONNECT_OMISE_TEST_SECRET_KEY without printing it.'}
if($env:NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN-cnotmatch '\Atokn_test_[a-z0-9]{10,64}\z'){throw 'Inject one fresh token in NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN without printing it.'}
if([string]::IsNullOrWhiteSpace($env:NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET)){throw 'Inject NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET without printing it.'}
try{$webhookKey=[Convert]::FromBase64String($env:NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET)}catch{throw 'The Omise test webhook secret must be base64.'}
if($webhookKey.Length-lt 16-or$webhookKey.Length-gt 128){[Array]::Clear($webhookKey,0,$webhookKey.Length);throw 'The webhook secret must decode to 16-128 bytes.'}
if($ValidateOnly){
    [Array]::Clear($webhookKey,0,$webhookKey.Length)
    [ordered]@{configurationValidated=$true;externalEndpointChecked=$false;infrastructureStarted=$false;providerRequestsSent=0;financialCommandsSent=0;processesTerminated=0;acceptancePassed=$false}|ConvertTo-Json
    return
}
if(-not$ConfirmDisposableInfrastructure-or-not$ConfirmProcessTermination-or-not$ConfirmSandboxTransaction-or-not$ConfirmDashboardWebhookConfigured){
    [Array]::Clear($webhookKey,0,$webhookKey.Length)
    throw 'Pass all four confirmation switches after authorizing disposable infrastructure, exact Payment termination, one test authorization, and the Omise test-dashboard callback URL.'
}

$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runId=[Guid]::NewGuid().ToString('N');$project='nexa-omise-webhook-live-it-'+$runId
$run=Join-Path $root ('.runstate/payment-omise-webhook-live/'+$runId);New-Item -ItemType Directory -Path $run|Out-Null
$composeDir=Join-Path $root 'docker/order-provider-recovery-acceptance'
$compose=@('compose','--env-file',(Join-Path $composeDir '.env.example'),'-f',(Join-Path $composeDir 'compose.yaml'),'-p',$project)
$paymentProject=Join-Path $root 'src/Services/NexaConnect.Services.Payment/NexaConnect.Services.Payment.csproj'
$testProject=Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$paymentOutput=Join-Path $run 'payment-host/';$testOutput=Join-Path $run 'integration-bin/'
$marker=Join-Path $run 'financial-boundary.json';$control=Join-Path $run 'control.json';$evidence=Join-Path $run 'evidence.json'
$host=$null;$created=$false;$processesTerminated=0;$externalDelivery=$false;$duplicateReplay=$false;$acceptancePassed=$false;$cleanupPassed=$false
$password=[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N')
$rabbitListener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$rabbitListener.Start();$rabbitPort=$rabbitListener.LocalEndpoint.Port;$rabbitListener.Stop()
$saved=@{};$environmentNames=@('NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_PASSWORD','NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_RABBIT_PORT','NEXACONNECT_ENVIRONMENT','NEXACONNECT_OMISE_WEBHOOK_LIVE_ACCEPTANCE','NEXACONNECT_OMISE_WEBHOOK_LIVE_STAGE','NEXACONNECT_OMISE_WEBHOOK_LIVE_DB','NEXACONNECT_OMISE_WEBHOOK_LIVE_AMOUNT','NEXACONNECT_OMISE_WEBHOOK_LIVE_EVIDENCE','NEXACONNECT_OMISE_WEBHOOK_LIVE_EXPECTED_INBOX_COUNT')
foreach($name in $environmentNames){$saved[$name]=[Environment]::GetEnvironmentVariable($name)}

function Local-Port([string]$value){if($value-cnotmatch '^127\.0\.0\.1:(\d{1,5})$'){throw 'Disposable ports must bind only to IPv4 loopback.'};return[int]$Matches[1]}
function Db-Connection([int]$port){$b=[Data.Common.DbConnectionStringBuilder]::new();$b['Host']='127.0.0.1';$b['Port']=$port;$b['Database']='payment_provider_recovery';$b['Username']='postgres';$b['Password']=$password;return$b.ConnectionString}
function Wait-Port([Diagnostics.Process]$process){for($i=0;$i-lt 120;$i++){if($process.HasExited){throw 'Payment exited; its restricted logs will be removed during cleanup.'};$c=[Net.Sockets.TcpClient]::new();try{if($c.ConnectAsync('127.0.0.1',$ListenerPort).Wait(500)-and$c.Connected){return}}catch{}finally{$c.Dispose()};Start-Sleep -Milliseconds 500};throw 'Payment did not open its loopback listener.'}
function Start-Payment([bool]$pause){
    $out=Join-Path $run 'payment.out.log';$err=Join-Path $run 'payment.err.log'
    $old=@{};$settings=[ordered]@{
      'ASPNETCORE_ENVIRONMENT'='Testing';'DOTNET_ENVIRONMENT'='Testing';'ASPNETCORE_URLS'="http://127.0.0.1:$ListenerPort";
      'Persistence__Provider'='PostgreSQL';'ConnectionStrings__Payment'=$env:NEXACONNECT_OMISE_WEBHOOK_LIVE_DB;
      'PaymentProvider__Adapter'='Omise';'PaymentProvider__OmiseSecretKey'=$env:NEXACONNECT_OMISE_TEST_SECRET_KEY;
      'PaymentProvider__RequestTimeout'='00:00:05';'PaymentProvider__LeaseDuration'='00:00:30';'PaymentProvider__AuthorizationRecoveryEnabled'='false';'PaymentProvider__CaptureRecoveryEnabled'='false';'PaymentProvider__VoidRecoveryEnabled'='false';
      'OmiseWebhooks__Enabled'='true';'OmiseWebhooks__Secret'=$env:NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET;'OmiseWebhooks__PollInterval'='00:00:01';'OmiseWebhooks__RetryDelay'='00:00:05';'OmiseWebhooks__LeaseDuration'='00:01:00';'OmiseWebhooks__MaximumAttempts'='20';
      'OmiseWebhookAcceptance__Enabled'=$(if($pause){'true'}else{'false'});'OmiseWebhookAcceptance__MarkerPath'=$marker;'OmiseWebhookAcceptance__ControlPath'=$control;
      'Outbox__Enabled'='true';'Outbox__ConnectionString'=$env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_RABBITMQ;'Outbox__PollInterval'='00:00:00.250';
      'Authentication__Authority'='http://127.0.0.1:9/';'Authentication__Audience'='nexaconnect-api';'Authentication__RequireHttpsMetadata'='false';
      'Services__PlatformDirectory'='http://127.0.0.1:9/';'Services__Restaurant'='http://127.0.0.1:9/';'Services__Order'='http://127.0.0.1:9/';'Services__Authorization'='http://127.0.0.1:9/'
    }
    $rawNames=@('NEXACONNECT_OMISE_TEST_SECRET_KEY','NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET','NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN')
    try{foreach($name in $settings.Keys){$old[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$settings[$name])};foreach($name in $rawNames){$old[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$null)};$p=Start-Process dotnet -ArgumentList @(('"'+(Join-Path $paymentOutput 'NexaConnect.Services.Payment.dll')+'"')) -WorkingDirectory $paymentOutput -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err;Wait-Port $p;return$p}finally{foreach($name in @($settings.Keys)+$rawNames){[Environment]::SetEnvironmentVariable($name,$old[$name])}}
}
function Build-Acceptance{
    $rawNames=@('NEXACONNECT_OMISE_TEST_SECRET_KEY','NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET','NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN');$old=@{}
    try{foreach($name in $rawNames){$old[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$null)};&dotnet build $paymentProject --configuration Release --no-restore --verbosity minimal "-p:OutputPath=$paymentOutput";if($LASTEXITCODE-ne 0){throw 'Payment build failed.'};&dotnet build $testProject --configuration Release --no-restore --verbosity minimal "-p:OutputPath=$testOutput" -m:1;if($LASTEXITCODE-ne 0){throw 'Webhook acceptance build failed.'}}finally{foreach($name in $rawNames){[Environment]::SetEnvironmentVariable($name,$old[$name])}}
}
function Run-Stage([string]$stage,[string]$method){
    $rawNames=@('NEXACONNECT_OMISE_TEST_SECRET_KEY','NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET','NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN');$old=@{};$env:NEXACONNECT_OMISE_WEBHOOK_LIVE_STAGE=$stage
    try{foreach($name in $rawNames){$old[$name]=[Environment]::GetEnvironmentVariable($name)};[Environment]::SetEnvironmentVariable('NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET',$null);if($stage-ne'authorize'){[Environment]::SetEnvironmentVariable('NEXACONNECT_OMISE_TEST_SECRET_KEY',$null);[Environment]::SetEnvironmentVariable('NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN',$null)};&dotnet test $testProject --configuration Release --no-build --no-restore --verbosity quiet "-p:OutputPath=$testOutput" --filter "FullyQualifiedName~$method";if($LASTEXITCODE-ne 0){throw "Omise webhook live stage '$stage' failed. Do not retry the token if authorization may have reached Omise; inspect test-account charges first."}}finally{foreach($name in $rawNames){[Environment]::SetEnvironmentVariable($name,$old[$name])}}
}
function Query([string]$sql){$value=&$DockerExecutable @compose exec -T postgres psql -X -q -A -t -v ON_ERROR_STOP=1 -U postgres -d payment_provider_recovery -c $sql 2>$null;if($LASTEXITCODE-ne 0){throw 'Disposable Payment database query failed.'};return([string]($value-join'')).Trim()}

try{
    $endpoint=(& $DockerExecutable context inspect --format '{{.Endpoints.docker.Host}}').Trim();if($LASTEXITCODE-ne 0-or($endpoint-notmatch'^npipe:////\./pipe/[A-Za-z0-9._-]+$'-and$endpoint-notmatch'^unix:///')){throw 'Acceptance requires a local Docker socket.'}
    $env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_PASSWORD=$password;$env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_ACCEPTANCE_RABBIT_PORT=[string]$rabbitPort
    $existing=@(&$DockerExecutable @compose ps -aq);if($LASTEXITCODE-ne 0){throw 'Docker was unavailable while checking the generated acceptance identity.'};if($existing.Count-ne 0){throw 'Generated disposable Compose identity was unexpectedly in use.'}
    $created=$true;&$DockerExecutable @compose up -d --wait --wait-timeout 120;if($LASTEXITCODE-ne 0){throw 'Disposable webhook infrastructure did not become healthy.'}
    $postgresPort=Local-Port (&$DockerExecutable @compose port postgres 5432);$actualRabbit=Local-Port (&$DockerExecutable @compose port rabbitmq 5672)
    $env:NEXACONNECT_OMISE_WEBHOOK_LIVE_DB=Db-Connection $postgresPort;$env:NEXACONNECT_ORDER_PROVIDER_RECOVERY_RABBITMQ="amqp://acceptance:$password@127.0.0.1:$actualRabbit/"
    $env:NEXACONNECT_ENVIRONMENT='Testing';$env:NEXACONNECT_OMISE_WEBHOOK_LIVE_ACCEPTANCE='1';$env:NEXACONNECT_OMISE_WEBHOOK_LIVE_AMOUNT=$Amount.ToString([Globalization.CultureInfo]::InvariantCulture);$env:NEXACONNECT_OMISE_WEBHOOK_LIVE_EVIDENCE=$evidence
    Build-Acceptance
    Run-Stage 'initialize' 'Initialize_disposable_webhook_recovery_fixture'
    $host=Start-Payment $true
    try{$probe=Invoke-WebRequest -Uri $PublicWebhookUrl -Method Get -TimeoutSec 15 -SkipHttpErrorCheck;if([int]$probe.StatusCode-ne 405){throw 'unexpected status'}}catch{throw 'The public webhook URL did not reach the exact Payment webhook route. Verify the trusted HTTPS tunnel and dashboard URL before consuming the token.'}
    Run-Stage 'authorize' 'Create_one_test_authorization_for_external_webhook_delivery'
    $deadline=[DateTimeOffset]::UtcNow.AddMinutes(3);while(-not(Test-Path -LiteralPath $marker)){if($host.HasExited){throw 'Payment exited before the webhook recovery boundary.'};if([DateTimeOffset]::UtcNow-ge$deadline){throw 'Timed out waiting for a real signed Omise webhook. Do not create another authorization; inspect the dashboard and callback delivery.'};Start-Sleep -Milliseconds 500}
    $boundary=Get-Content -LiteralPath $marker -Raw|ConvertFrom-Json;if($boundary.phase-ne'financial_committed_before_inbox_ack'-or$boundary.outcome-ne'completed'-or[int]$boundary.processId-ne$host.Id){throw 'Payment reported an invalid interruption boundary.'}
    $externalDelivery=$true;&taskkill.exe /PID $host.Id /T /F|Out-Null;if($LASTEXITCODE-ne 0-or-not$host.WaitForExit(10000)){throw 'Could not terminate the exact Payment process tree.'};$processesTerminated++;$host.Dispose();$host=$null
    $host=Start-Payment $false
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(100);do{$state=Query "SELECT count(*) FILTER(WHERE status='completed')||':'||count(*) FILTER(WHERE status IN('pending','processing')) FROM omise_webhook_inbox";if($state-match'^(\d+):0$'-and[int]$Matches[1]-ge 1){break};Start-Sleep -Seconds 1}while([DateTimeOffset]::UtcNow-lt$deadline)
    if($state-notmatch'^(\d+):0$'-or[int]$Matches[1]-lt 1){throw 'Webhook inbox did not recover its expired lease after Payment restart.'}
    $baseline=[int](Query 'SELECT count(*) FROM omise_webhook_inbox');$eventId=Query "SELECT event_id FROM omise_webhook_inbox WHERE status='completed' ORDER BY received_at_utc LIMIT 1"
    if($eventId-cnotmatch'\Aevnt_test_[a-z0-9]{10,64}\z'){throw 'Completed webhook identity was invalid.'}
    $body='{"id":"'+$eventId+'"}';$timestamp=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString();$signature=[Convert]::ToHexStringLower([Security.Cryptography.HMACSHA256]::HashData($webhookKey,[Text.Encoding]::UTF8.GetBytes($timestamp+'.'+$body)))
    Invoke-WebRequest -Uri "http://127.0.0.1:$ListenerPort/api/payment/v1/webhooks/omise" -Method Post -ContentType 'application/json' -Body $body -Headers @{'Omise-Signature-Timestamp'=$timestamp;'Omise-Signature'=$signature}|Out-Null
    $eventId=$null;$body=$null;$signature=$null;Start-Sleep -Seconds 2
    if([int](Query 'SELECT count(*) FROM omise_webhook_inbox')-ne$baseline){throw 'Signed duplicate delivery created another inbox identity.'};$duplicateReplay=$true
    $env:NEXACONNECT_OMISE_WEBHOOK_LIVE_EXPECTED_INBOX_COUNT=[string]$baseline;Run-Stage 'verify' 'Verify_restart_and_signed_duplicate_replay_without_duplicate_financial_transition'
    $acceptancePassed=$true
    Write-Output "Omise webhook live process-interruption acceptance passed. Sanitized evidence retained at '$run'."
}
finally{
    [Array]::Clear($webhookKey,0,$webhookKey.Length);$password=$null
    if($null-ne$host){if(-not$host.HasExited){&taskkill.exe /PID $host.Id /T /F 2>$null|Out-Null;$processesTerminated++};$host.Dispose()}
    if($created){&$DockerExecutable @compose down -v --remove-orphans 2>$null|Out-Null;$remaining=@(&$DockerExecutable @compose ps -aq 2>$null);$cleanupPassed=$LASTEXITCODE-eq 0-and$remaining.Count-eq 0}
    foreach($name in $environmentNames){[Environment]::SetEnvironmentVariable($name,$saved[$name])}
    Get-ChildItem -LiteralPath $run -Filter '*.log' -ErrorAction SilentlyContinue|Remove-Item -Force -ErrorAction SilentlyContinue
    foreach($path in @($marker,$control)){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}
    foreach($directory in @($paymentOutput,$testOutput)){if((Test-Path -LiteralPath $directory)-and[IO.Path]::GetFullPath($directory).StartsWith([IO.Path]::GetFullPath($run),[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $directory -Recurse -Force}}
    if($acceptancePassed){[ordered]@{completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');externalSignedDeliveryVerified=$externalDelivery;paymentProcessTerminationVerified=$true;signedDuplicateReplayVerified=$duplicateReplay;processesTerminated=1;providerAuthorizationCommands=1;acceptancePassed=$true;cleanupPassed=$cleanupPassed;secretsRetained=$false;rawServiceLogsRetained=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'summary.json') -Encoding utf8;if(-not$cleanupPassed){throw 'Acceptance passed, but disposable cleanup could not be verified.'}}
}
