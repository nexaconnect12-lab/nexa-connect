#requires -Version 7.4
param([Parameter(Mandatory)][string]$RunId,[Parameter(Mandatory)][string]$PreviousPilotId,[Parameter(Mandatory)][string]$CandidateDirectory,[switch]$ConfirmTestResources)
$ErrorActionPreference='Stop'
if(-not $ConfirmTestResources){throw 'Confirm this same-scope local QR refresh.'}
if(Get-Process NexaConnect.POS -ErrorAction SilentlyContinue){throw 'Finish the current action and close POS before QR refresh.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
Assert-DraftRunId $RunId;Assert-DraftRunId $PreviousPilotId
$old=Assert-TerminalPath "$root/.runstate/qr7/$PreviousPilotId"
$run=Assert-TerminalPath (Get-DraftRunDirectory $root $RunId)
if(Test-Path "$root/.runstate/pos-recovery-lab/$RunId/lab.json"){throw 'Cannot refresh a normal QR edge for a recovery lab.'}
$previous=Get-Content "$old/plan.json" -Raw|ConvertFrom-Json
if($previous.stackRunId -cne $RunId){throw 'Previous QR pilot belongs to a different stack.'}
$candidate=Assert-TerminalPath $CandidateDirectory
$output=Assert-TerminalPath "$root/.runstate/local-pilot/$RunId"
New-Item -ItemType Directory $output -Force|Out-Null;Protect-DraftDirectory $output
$id=[guid]::NewGuid().ToString('N');$new=Assert-TerminalPath "$root/.runstate/qr7/$id"
& "$PSScriptRoot/qr-pilot.ps1" -Action Init -PilotId $id -CandidateDirectory $candidate
& node "$PSScriptRoot/release/local-pilot.mjs" $old $new "$run/state.json" "$run/reference.json"
if($LASTEXITCODE -ne 0){throw 'Same-scope QR plan rejected.'}
# Configure validates current source, owned listeners, actual LAN address, exact SAN/chain
# and lifetime before the old listener is stopped. Certificate trust is retained, never added.
& "$PSScriptRoot/qr-pilot.ps1" -Action Configure -PilotId $id
& "$PSScriptRoot/qr-pilot.ps1" -Action Stop -PilotId $PreviousPilotId
& "$PSScriptRoot/qr-pilot.ps1" -Action Start -PilotId $id
@{version=1;runId=$RunId;pilotId=$id;previousPilotId=$PreviousPilotId;kind='normal-pilot-qr-refresh';operatorAcceptance=$false;releaseReady=$false}|ConvertTo-Json|Set-Content "$output/qr-current.pending.json"
[IO.File]::Move("$output/qr-current.pending.json","$output/qr-current.json",$true)
Write-Output "Normal pilot QR refreshed: $id. Phone observations must be repeated; no passes were transferred."
