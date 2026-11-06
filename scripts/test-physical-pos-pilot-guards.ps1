#requires -Version 7.4
param([switch]$Compose)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-terminal-helpers.ps1')
. (Join-Path $PSScriptRoot 'physical-pos-pilot-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
foreach($name in @('physical-pos-pilot.ps1','physical-pos-pilot-helpers.ps1','first-draft-stack.ps1','first-draft-helpers.ps1','../packaging/windows-pos/Test-DeviceOnboarding.ps1')){
    $tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors)
    if($errors){throw "PowerShell syntax failed: $name"}
}
function Reject([scriptblock]$Action){$rejected=$false;try{&$Action}catch{$rejected=$true};if(-not $rejected){throw 'Expected rejection.'}}
if(-not(Test-PhysicalPosSameSubnet '192.168.0.191' '192.168.0.54' 24) -or (Test-PhysicalPosSameSubnet '192.168.0.191' '192.168.87.54' 24) -or -not(Test-PhysicalPosSameSubnet '172.28.0.1' '172.28.15.254' 20) -or (Test-PhysicalPosSameSubnet '172.28.0.1' '172.28.16.1' 20)){throw 'Subnet preflight incorrect.'}
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('nexa-physical-plan-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temporary|Out-Null
try{
    $certificate=Join-Path $temporary 'fixture.pem';$key=Join-Path $temporary 'fixture.key';Set-Content $certificate 'fixture';Set-Content $key 'fixture'
    $plan=@{version=1;runId=('a'*32);hostAddress='192.168.1.10';deviceAddress='192.168.1.20';origin='https://192.168.1.10:9443';certificatePath=$certificate;privateKeyPath=$key;confirmFreshTestData=$true;hostTrustConfirmed=$true;deviceTrustConfirmed=$true;deviceWindowsAccount='test-cashier'}
    $file=Join-Path $temporary 'plan.json';$plan|ConvertTo-Json|Set-Content $file
    $null=Read-PhysicalPosPlan $file
    foreach($name in @('confirmFreshTestData','hostTrustConfirmed','deviceTrustConfirmed')){$changed=$plan.Clone();$changed[$name]=$false;$changed|ConvertTo-Json|Set-Content $file;Reject {Read-PhysicalPosPlan $file}}
    foreach($origin in @('http://192.168.1.10:9443','https://0.0.0.0:9443','https://192.168.1.10:9443/path','https://192.168.1.10:9443?token=secret')){$changed=$plan.Clone();$changed.origin=$origin;$changed|ConvertTo-Json|Set-Content $file;Reject {Read-PhysicalPosPlan $file}}
    foreach($address in @('127.0.0.1','8.8.8.8','::1','0.0.0.0')){$changed=$plan.Clone();$changed.deviceAddress=$address;$changed|ConvertTo-Json|Set-Content $file;Reject {Read-PhysicalPosPlan $file}}
    foreach($origin in @('http://192.168.1.10:9443','https://8.8.8.8:9443','https://staff.example.test:9443')){Reject {Set-DraftComposeEnvironment @{} ('a'*32) 24010 @{} -StaffPilotOrigin $origin}}
    $changed=$plan.Clone();$changed.deviceWindowsAccount='';$changed|ConvertTo-Json|Set-Content $file;Reject {Read-PhysicalPosPlan $file}
    $state=@{runId=$plan.runId;physicalPosPilot=@{origin=$plan.origin};processes=@{};urls=@{Bff='https://127.0.0.1:24010/'};
        infrastructure=@{keycloak=24011};authority='https://192.168.1.10:9443/realms/test';provisionedBranch=$true}
    foreach($port in @(443,9443,24010,24011)){Reject {Set-PhysicalGuestQrProfile $state $port $false}}
    Reject {Set-PhysicalGuestQrProfile $state 8444 $true}
    $state.processes.Bff=@{id=42};Reject {Set-PhysicalGuestQrProfile $state 8444 $false};$state.processes.Clear()
    $state.qrPilotOrigin='https://192.168.1.10:8443';Reject {Set-PhysicalGuestQrProfile $state 8444 $false};$state.Remove('qrPilotOrigin')
    Set-PhysicalGuestQrProfile $state 8444 $false
    if($state.physicalGuestQrOrigin -cne 'https://192.168.1.10:8444' -or
        (Get-DraftQrPilotSettings Bff $state.physicalGuestQrOrigin).QrGuest__Enabled -cne 'true' -or
        $state.authority -cne 'https://192.168.1.10:9443/realms/test' -or -not $state.provisionedBranch){
        throw 'Guest QR changed the staff issuer or did not enable the intended guest origin.'
    }
    Reject {Set-PhysicalGuestQrProfile $state 8445 $false}
    Set-PhysicalGuestQrProfile $state 0 $false
    if($state.Contains('physicalGuestQrOrigin')){throw 'Staff-only recovery retained a guest binding.'}
    Reject {Set-PhysicalGuestQrProfile $state 8445 $false}
    Set-PhysicalGuestQrProfile $state 8444 $false
    Write-Output 'Physical POS plan/parser guards passed; no network, trust, listener or installation changes.'
}finally{Remove-Item -LiteralPath $temporary -Recurse -Force}
if($Compose){
    $id=[Guid]::NewGuid().ToString('N');$run=Get-DraftRunDirectory $root $id
    New-Item -ItemType Directory $run|Out-Null
    $old=@{}
    try{
        Set-Content (Join-Path $run 'compose.env') '# Synthetic Compose-only fixture'
        @{mediaEnabled=$true;physicalPosPilot=@{origin='https://192.168.1.10:9443'}}|ConvertTo-Json|Set-Content (Join-Path $run 'state.json')
        $secrets=@{};foreach($key in @('ADMIN_PASSWORD','MIGRATION_PASSWORD','RUNTIME_PASSWORD','KEYCLOAK_DB_PASSWORD','KEYCLOAK_ADMIN_PASSWORD','CLIENT_SECRET','READER_PASSWORD','RESOLVER_PASSWORD','SECONDMANAGER_PASSWORD','ACCOUNTANT_PASSWORD','RABBITMQ_PASSWORD','REDIS_PASSWORD','MEDIA_ROOT_USER','MEDIA_ROOT_PASSWORD','MEDIA_ACCESS_KEY','MEDIA_SECRET_KEY')){$secrets[$key]='synthetic-compose-only-not-a-credential'}
        $old=Set-DraftComposeEnvironment $secrets $id 24010 @{postgres=24001;keycloak=24002;rabbitmq=24003;redis=24004;minio=24005;clamav=24006} -StaffPilotOrigin 'https://192.168.1.10:9443'
        if($env:NEXACONNECT_POS_PILOT_ORIGIN -cne 'https://192.168.1.10:9443'){throw 'Provisioning Compose context lost the canonical staff origin.'}
        $composeArgs=Get-DraftComposeArguments $root $id
        $config=& docker @composeArgs config --format json
        if($LASTEXITCODE -ne 0){throw 'Compose configuration validation failed.'}
        $config=$config|ConvertFrom-Json
        if($config.services.keycloak.environment.KC_HOSTNAME -cne $env:NEXACONNECT_POS_PILOT_ORIGIN -or $config.services.keycloak.environment.KC_PROXY_HEADERS -cne 'xforwarded'){throw 'Canonical staff identity configuration missing.'}
        foreach($entry in $config.services.PSObject.Properties){if($entry.Value.PSObject.Properties['ports']){foreach($port in @($entry.Value.ports)){if($port -and $port.host_ip -cne '127.0.0.1'){throw 'Infrastructure port escaped loopback.'}}}}
        @{mediaEnabled=$true}|ConvertTo-Json|Set-Content (Join-Path $run 'state.json')
        $normalArgs=Get-DraftComposeArguments $root $id
        if($normalArgs -contains (Join-Path $root 'docker/first-draft/pos-lan.compose.yaml')){throw 'Normal pilot selected staff override.'}
        Write-Output 'Compose staff issuer and loopback infrastructure isolation passed; no containers started.'
    }finally{
        Restore-DraftEnvironment $old
        $expected=[IO.Path]::GetFullPath((Join-Path $root ".runstate/first-draft/$id"));if([IO.Path]::GetFullPath($run) -cne $expected -or $id -cnotmatch '^[a-f0-9]{32}$'){throw 'Unsafe fixture cleanup.'}
        Remove-Item -LiteralPath $run -Recurse -Force
    }
}
