#requires -Version 7.4
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
$root=Split-Path $PSScriptRoot -Parent
$passed=0
function Reject([scriptblock]$Action){$rejected=$false;try{& $Action|Out-Null}catch{$rejected=$true};if(-not $rejected){throw 'Media profile accepted unsafe configuration.'};$script:passed++}
$base=Get-DraftServiceCatalog $root
$media=Get-DraftServiceCatalog $root -IncludeMedia
if($base.Count -ne 10 -or $base.Contains('Media')){throw 'Default inventory changed.'};$passed++
if($media.Count -ne 11 -or $media.Media.version -ne 4){throw 'Media schema inventory missing.'};$passed++
$pos='{"Services":{"PosApi":"","OrderApi":"","PaymentApi":"","CatalogApi":""}}'|ConvertFrom-Json
$profile=@{urls=@{POS='http://127.0.0.1:24101/';Order='http://127.0.0.1:24102/';Payment='http://127.0.0.1:24103/';Catalog='http://127.0.0.1:24104/';Media='http://127.0.0.1:24105/'}}
Set-DraftPosServiceUrls $pos $profile
if($pos.Services.MediaApi -cne '' -or $pos.Services.PosApi -cne $profile.urls.POS){throw 'Default terminal Media must stay disabled.'};$passed++
$profile.mediaEnabled=$true;Set-DraftPosServiceUrls $pos $profile
if($pos.Services.MediaApi -cne $profile.urls.Media){throw 'Enabled terminal Media endpoint missing.'};$passed++
$profile.mediaEnabled=$false;Set-DraftPosServiceUrls $pos $profile
if($pos.Services.MediaApi -cne ''){throw 'Disabled terminal retained a stale Media endpoint.'};$passed++
$profile.mediaEnabled='true';Reject {Set-DraftPosServiceUrls $pos $profile}
$profile.mediaEnabled=$true;$profile.urls.Remove('Media');Reject {Set-DraftPosServiceUrls $pos $profile}
$profile.urls.Media='http://192.168.0.191:24105/';Reject {Set-DraftPosServiceUrls $pos $profile}
$id=[guid]::NewGuid().ToString('N');$run=Get-DraftRunDirectory $root $id
$foreign=@{runId=$id;project="nexa-draft-$id";realm="nexa-review-it-$id";infrastructure=@{keycloak=24001};authority='https://identity.example.invalid/realms/other'}
Reject {Ensure-DraftMediaAudience $foreign @{}}
$foreign.infrastructure.keycloak='24001@other.example.invalid'
Reject {Ensure-DraftMediaAudience $foreign @{}}
New-Item -ItemType Directory -Path $run|Out-Null
try{
 $argsList=Get-DraftComposeArguments $root $id
 if($argsList -contains (Join-Path $root 'docker/first-draft/media.compose.yaml')){throw 'Media dependencies enabled by default.'};$passed++
 @{mediaEnabled=$true}|ConvertTo-Json|Set-Content (Join-Path $run 'state.json')
 $argsList=Get-DraftComposeArguments $root $id
 if($argsList -notcontains (Join-Path $root 'docker/first-draft/media.compose.yaml')){throw 'Persisted Media overlay missing.'};$passed++
 @{mediaEnabled='true'}|ConvertTo-Json|Set-Content (Join-Path $run 'state.json')
 Reject {Get-DraftComposeArguments $root $id}
 @{runId=$id;project="nexa-draft-$id";status='Stopped';mediaEnabled=$true}|ConvertTo-Json|Set-Content (Join-Path $run 'state.json')
 Save-DraftSecrets (Join-Path $run 'secrets.clixml') @{fixture='synthetic'}
 $stateBefore=(Get-FileHash (Join-Path $run 'state.json')).Hash;$secretsBefore=(Get-FileHash (Join-Path $run 'secrets.clixml')).Hash
 Reject {& (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Status -RunId $id}
 if((Get-FileHash (Join-Path $run 'state.json')).Hash -cne $stateBefore -or (Get-FileHash (Join-Path $run 'secrets.clixml')).Hash -cne $secretsBefore){throw 'Read-only Status mutated incomplete Media credentials/state.'};$passed++
 @{mediaEnabled=$true}|ConvertTo-Json|Set-Content (Join-Path $run 'state.json')
 $ports=@{postgres=24001;keycloak=24002;rabbitmq=24003;redis=24004;minio=24005;clamav=24006}
 $old=Set-DraftComposeEnvironment @{} $id 24007 $ports
 try{if($env:NEXACONNECT_DRAFT_MINIO_PORT -ne '24005' -or $env:NEXACONNECT_DRAFT_CLAMAV_PORT -ne '24006' -or $env:NEXACONNECT_DRAFT_MEDIA_ORIGIN -cne 'https://127.0.0.1:24007'){throw 'Exact storage/CORS configuration missing.'};$passed++}finally{Restore-DraftEnvironment $old}
 $ports.clamav=24005;Reject {Set-DraftComposeEnvironment @{} $id 24007 $ports}
 $ports.Remove('clamav');Reject {Set-DraftComposeEnvironment @{} $id 24007 $ports}
 foreach($name in @($base.Keys)+@('Bff')){New-Item -ItemType Directory -Path (Join-Path $run "apps/$name") -Force|Out-Null;Set-Content (Join-Path $run "apps/$name/fixture.dll") 'synthetic'}
 Reject {Get-DraftQrPilotArtifactHash $run}
 New-Item -ItemType Directory -Path (Join-Path $run 'apps/Media')|Out-Null;Set-Content (Join-Path $run 'apps/Media/fixture.dll') 'first'
 $first=Get-DraftQrPilotArtifactHash $run;$passed++
 Set-Content (Join-Path $run 'apps/Media/fixture.dll') 'changed'
 if((Get-DraftQrPilotArtifactHash $run) -ceq $first){throw 'Media publication is outside QR integrity binding.'};$passed++
}finally{
 $resolved=[IO.Path]::GetFullPath($run);$expected=[IO.Path]::GetFullPath((Join-Path $root ".runstate/first-draft/$id"))
 if($resolved -cne $expected -or $id -cnotmatch '^[a-f0-9]{32}$'){throw 'Unsafe fixture cleanup path.'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output "Media profile guards: $passed passed"
