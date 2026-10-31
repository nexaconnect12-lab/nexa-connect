#requires -Version 7.4
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm generated local database/broker creation, fixture-only migration rollback, controlled fixture Order-process interruptions and project-scoped cleanup.'}
if(-not $IsWindows){throw 'Windows DPAPI and WPF are required.'}
if($env:DOCKER_HOST){throw 'DOCKER_HOST must be unset; use local Docker Desktop.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-release-helpers.ps1"
. "$PSScriptRoot/first-draft-uat-helpers.ps1"
. "$PSScriptRoot/pos-recovery-access-helpers.ps1"
$docker=Get-Command docker -ErrorAction SilentlyContinue
$dockerPath=if($docker){$docker.Source}else{Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'}
if(-not(Test-Path -LiteralPath $dockerPath -PathType Leaf)){throw 'Local Docker CLI is required.'}
$context=(& $dockerPath context show 2>$null)
if($LASTEXITCODE -or ([string]$context).Trim() -cnotin @('default','desktop-linux')){throw 'Use a recognized local Docker context.'}
$endpoint=(& $dockerPath context inspect ([string]$context).Trim() --format '{{.Endpoints.docker.Host}}' 2>$null)
if($LASTEXITCODE -or ([string]$endpoint).Trim() -cnotin @('npipe:////./pipe/dockerDesktopLinuxEngine','npipe:////./pipe/docker_engine','unix:///var/run/docker.sock')){throw 'Local Docker socket verification failed.'}
# Prevent a download or implicit remote prerequisite change during verification.
foreach($image in @('postgres:17-alpine','rabbitmq:4-management')){
    & $dockerPath image inspect $image *> $null
    if($LASTEXITCODE){throw 'Required disposable fixture images must already be available locally.'}
}
$id=[guid]::NewGuid().ToString('N')
$run=Assert-TerminalPath "$root/.runstate/pos-recovery-access/$id"
New-Item -ItemType Directory -Path $run|Out-Null;Protect-DraftDirectory $run
$previous=@{};$checks=[ordered]@{};$source=$null;$passed=$false
function Invoke-Producer([string]$Name,[string]$Folder,[string]$ReceiptName,[string]$Script,[string[]]$Arguments){
    $parent=Assert-TerminalPath "$root/.runstate/$Folder"
    $before=@(if(Test-Path -LiteralPath $parent){Get-ChildItem -LiteralPath $parent -Directory|ForEach-Object Name})
    & pwsh -NoProfile -File "$PSScriptRoot/$Script" @Arguments *> "$run/$Name.private.log"
    if($LASTEXITCODE){throw "UX20 producer failed: $Name; private diagnostics retained. No automatic retry."}
    $created=@(Get-ChildItem -LiteralPath $parent -Directory|Where-Object {$_.Name -cnotin $before})
    if($created.Count -ne 1 -or $created[0].Name -cnotmatch '^[a-f0-9]{32}$'){throw 'Expected exactly one newly generated producer run; do not run competing producers.'}
    $path=Assert-TerminalPath (Join-Path $created[0].FullName $ReceiptName)
    $receipt=ConvertFrom-UatJson (Get-Content -LiteralPath $path -Raw)
    Assert-PosRecoveryProducer $Name $receipt
    if($Name -ceq 'regression'){Assert-TerminalCandidateUnchanged $script:source $receipt.source}
    $script:checks[$Name]=@{passed=$true;runId=$created[0].Name;receiptHash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}
}
Push-Location $root
try{
    $source=Get-DraftCandidate $root
    # These are child-process settings only. Never inherit retained-stack database/provider settings.
    foreach($item in @(Get-ChildItem Env:|Where-Object {$_.Name -like 'NEXACONNECT_*' -or $_.Name -in @('DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT','DOTNET_PROCESSOR_COUNT','MSBUILDDISABLENODEREUSE','DOTNET_CLI_USE_MSBUILD_SERVER','UseSharedCompilation')})){
        $previous[$item.Name]=$item.Value;[Environment]::SetEnvironmentVariable($item.Name,$null)
    }
    foreach($pair in @(@('DOTNET_PROCESSOR_COUNT','4'),@('MSBUILDDISABLENODEREUSE','1'),@('DOTNET_CLI_USE_MSBUILD_SERVER','0'),@('UseSharedCompilation','false'))){
        if(-not $previous.ContainsKey($pair[0])){$previous[$pair[0]]=$null}
        [Environment]::SetEnvironmentVariable($pair[0],$pair[1])
    }
    & pwsh -NoProfile -File "$PSScriptRoot/test-pos-recovery-access-guards.ps1" *> "$run/guards.private.log"
    if($LASTEXITCODE){throw 'UX20 producer guards failed.'}
    $checks.guards=@{passed=$true}
    Invoke-Producer cashier pos-cashier-sale verification.json test-pos-cashier-sale.ps1 @('-ConfirmDisposableInfrastructure','-ConfirmDestructiveRollback','-DockerExecutable',$dockerPath)
    Invoke-Producer workflow order-workflow-recovery-live summary.json test-order-workflow-recovery-live.ps1 @('-ConfirmDisposableInfrastructure','-DockerExecutable',$dockerPath)
    Invoke-Producer regression pos-regression verification.json test-pos-acceptance.ps1 @()
    Assert-TerminalCandidateUnchanged $source (Get-DraftCandidate $root)
    $passed=$true
}finally{
    Restore-DraftEnvironment $previous;Pop-Location
    @{version=1;kind='automated-pos-recovery-access';runId=$id;passed=$passed;
        source=if($source){@{revision=$source.revision;dirty=$source.dirty;inputHash=$source.inputHash}}else{$null};
        checks=$checks;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');
        operatorAcceptance=$false;retainedPilotMutated=$false;liveOidcVerified=$false;providerRequests=$false;
        releaseReady=$false;productionVerified=$false}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$run/verification.json"
}
Write-Output "POS recovery/access automation passed: $run/verification.json"
