#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'payment-review-joined-helpers.ps1')
$checks=0
function Must-Reject([scriptblock]$Action){$rejected=$false;try{& $Action|Out-Null}catch{$rejected=$true};if(-not$rejected){throw 'Unsafe joined acceptance input was accepted.'};$script:checks++}
foreach($name in @('','nexa-connect','nexa-review-joined-123','../nexa-review-joined-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')){Must-Reject{Assert-ReviewJoinedProject $name}}
$project='nexa-review-joined-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';Assert-ReviewJoinedProject $project;$checks++
$arguments=@(Get-ReviewJoinedComposeArguments (Resolve-Path (Join-Path $PSScriptRoot '..')).Path $project)
if($arguments[-1]-ne$project-or$arguments-notcontains'--env-file'-or$arguments-notcontains'-f'){throw 'Joined Compose isolation flags are missing.'};$checks++
foreach($address in @('0.0.0.0:5432','localhost:5432','127.0.0.1:22','127.0.0.1:65536','127.0.0.1:5432/')){Must-Reject{ConvertFrom-ReviewJoinedPort $address}}
if((ConvertFrom-ReviewJoinedPort '127.0.0.1:55432')-ne 55432){throw 'Valid joined loopback port failed.'};$checks++
Must-Reject{Get-ReviewJoinedFreePorts 0};$checks++
$ports=@(Get-ReviewJoinedFreePorts 7);if($ports.Count-ne 7-or(@($ports|Select-Object -Unique).Count)-ne 7-or@($ports|Where-Object{$_-lt 1024-or$_-gt 65535}).Count){throw 'Joined application ports are not distinct and valid.'};$checks++
foreach($suffix in @('NexaConnect_Order','../order','reporting')){Must-Reject{New-ReviewJoinedConnection 55432 ('a'*32) $suffix test synthetic}}
$connection=New-Object System.Data.Common.DbConnectionStringBuilder;$connection.set_ConnectionString((New-ReviewJoinedConnection 55432 ('a'*32) order test 'synthetic;quoted=value'))
if($connection['Database']-ne'nexa_review_it_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_order'-or$connection['Password']-ne'synthetic;quoted=value'){throw 'Joined connection was not safely encoded.'};$checks++
Must-Reject{& (Join-Path $PSScriptRoot 'test-payment-review-joined-infrastructure.ps1')};$checks++

$global:ReviewJoinedFakeCalls=New-Object 'System.Collections.Generic.List[object]'
function Invoke-FakeJoinedDocker{$global:ReviewJoinedFakeCalls.Add(@($args));$global:LASTEXITCODE=0;if($args[0]-eq'context'){Write-Output 'npipe://local-joined-test';return};if($args-contains'up'){$global:LASTEXITCODE=1}}
$names=@('NEXACONNECT_JOINED_RUN_ID','NEXACONNECT_JOINED_ADMIN_PASSWORD','NEXACONNECT_REVIEW_FIXTURE_ENABLED','DOCKER_HOST');$saved=@{}
try{
    foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,'synthetic-prior-value')};$env:DOCKER_HOST='npipe://local-joined-test'
    Must-Reject{& (Join-Path $PSScriptRoot 'test-payment-review-joined-infrastructure.ps1') -ConfirmDisposableInfrastructure -DockerExecutable Invoke-FakeJoinedDocker}
    foreach($name in $names|Where-Object{$_-ne'DOCKER_HOST'}){if([Environment]::GetEnvironmentVariable($name)-ne'synthetic-prior-value'){throw 'Joined launcher did not restore its environment.'}};$checks++
    $down=@($global:ReviewJoinedFakeCalls|Where-Object{$_-contains'down'});if($down.Count-ne 1){throw 'Failed joined startup did not clean exactly once.'};$checks++
    foreach($call in $global:ReviewJoinedFakeCalls){if($call[0]-ne'compose'){continue};$index=[Array]::IndexOf($call,'-p');if($index-lt 0-or$call-notcontains'-f'-or$call-notcontains'--env-file'){throw 'Joined Docker call escaped Compose isolation.'};Assert-ReviewJoinedProject $call[$index+1]};$checks++
}finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
$launcher=Get-Content -Raw (Join-Path $PSScriptRoot 'test-payment-review-joined-infrastructure.ps1')
foreach($required in @('RunLiveBrowser','nexa-review-it-$runId-inventory','host.docker.internal','test:e2e:payment-review:live','browserSummary.verified-ne$true','Stop-JoinedApplications')){if(-not$launcher.Contains($required)){throw "Joined live fail-closed contract is missing: $required"};$checks++}
Write-Output "$checks joined acceptance safety checks passed."
