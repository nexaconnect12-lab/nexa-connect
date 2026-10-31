#requires -Version 7.4
[CmdletBinding()]
param([switch]$SessionExpiryOnly,[switch]$ShutdownOnly)
$ErrorActionPreference='Stop'
if($SessionExpiryOnly -and $ShutdownOnly){throw 'Choose one isolated POS test mode.'}
if(-not $IsWindows){throw 'Windows DPAPI and WPF are required for POS regression checks.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-release-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
$id=[guid]::NewGuid().ToString('N');$run=Join-Path $root ".runstate/pos-regression/$id"
New-Item -ItemType Directory -Path $run|Out-Null;Protect-DraftDirectory $run
$previous=@{};$checks=[ordered]@{};$passed=$false;$source=$null
function Native([string]$Name,[string]$Command,[string[]]$Arguments){
    & $Command @Arguments *> (Join-Path $run "$Name.private.log")
    if($LASTEXITCODE){throw "POS regression failed: $Name; private diagnostics retained."}
    $script:checks[$Name]=@{passed=$true}
}
Push-Location $root
try{
    $source=Get-DraftCandidate $root
    foreach($pair in @(@('NEXACONNECT_ENVIRONMENT','Testing'),@('NEXACONNECT_POS_DPAPI_ACCEPTANCE','1'),@('MSBUILDDISABLENODEREUSE','1'),@('DOTNET_CLI_USE_MSBUILD_SERVER','0'),@('UseSharedCompilation','false'),@('DOTNET_PROCESSOR_COUNT','4'))){$previous[$pair[0]]=[Environment]::GetEnvironmentVariable($pair[0]);[Environment]::SetEnvironmentVariable($pair[0],$pair[1])}
    if($ShutdownOnly){
        Native windows-shutdown dotnet @('run','--project','tests/Windows/NexaConnect.PosTouchTests','--verbosity','quiet','--','--shutdown-only')
    }elseif($SessionExpiryOnly){
        Native windows-session dotnet @('run','--project','tests/Windows/NexaConnect.PosTouchTests','--verbosity','quiet','--','--session-expiry-only')
    }else{
        Native guards pwsh @('-NoProfile','-File',"$PSScriptRoot/test-pos-acceptance-guards.ps1")
        Native unit dotnet @('test','tests/Unit/NexaConnect.UnitTests','--no-restore','-m:1','--logger','trx;LogFileName=unit.trx','--results-directory',$run,'--verbosity','quiet')
        $checks.unit.count=Read-DraftTestCounts (Join-Path $run 'unit.trx') 1332
        Native http dotnet @('test','tests/Integration/NexaConnect.IntegrationTests','--no-restore','-m:1','--filter','FullyQualifiedName~DrawerIntentBoundaryTests|FullyQualifiedName~PromptPayQrBoundaryTests|FullyQualifiedName~CustomerPromptPayQrBoundaryTests|FullyQualifiedName~BarcodeBoundaryTests|FullyQualifiedName~OrderListBoundaryTests','--logger','trx;LogFileName=http.trx','--results-directory',$run,'--verbosity','quiet')
        $checks.http.count=Read-DraftTestCounts (Join-Path $run 'http.trx') 49
        Native windows-input dotnet @('run','--project','tests/Windows/NexaConnect.PosTouchTests','--verbosity','quiet')
        Native windows-print dotnet @('run','--project','tests/Windows/NexaConnect.PosPrintingTests','--verbosity','quiet')
    }
    Assert-TerminalCandidateUnchanged $source (Get-DraftCandidate $root)
    $passed=$true
}finally{
    Restore-DraftEnvironment $previous;Pop-Location
    @{version=1;kind=$(if($ShutdownOnly){'automated-pos-shutdown'}elseif($SessionExpiryOnly){'automated-pos-session-expiry'}else{'automated-pos-regression'});runId=$id;passed=$passed;source=if($source){@{revision=$source.revision;dirty=$source.dirty;inputHash=$source.inputHash}}else{$null};checks=$checks;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');retainedPilotMutated=$false;operatorAcceptance=$false;providerRequests=$false;releaseReady=$false;productionVerified=$false}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $run 'verification.json')
}
$label=if($ShutdownOnly){'POS shutdown automation'}elseif($SessionExpiryOnly){'POS session-expiry automation'}else{'POS automated regression'}
Write-Output "$label passed: $run/verification.json"
