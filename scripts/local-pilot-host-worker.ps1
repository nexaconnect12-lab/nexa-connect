#requires -Version 7.4
param([Parameter(Mandatory)][string]$HostId)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-release-helpers.ps1"
. "$PSScriptRoot/local-pilot-helpers.ps1"
. "$PSScriptRoot/local-pilot-host-helpers.ps1"
$config=Read-LocalPilotHost $root $HostId
$directory=Assert-TerminalPath "$root/.runstate/local-pilot-host/$HostId"
New-Item -ItemType Directory "$directory/worker" -Force|Out-Null
$workerLock=Open-DraftLifecycleLock "$directory/worker"
$attempts=0;$qrAttempts=0;$stableSince=$null
if(Test-Path "$directory/status.json"){
    $previous=Get-Content "$directory/status.json" -Raw|ConvertFrom-Json
    if($previous.hostId -cne $HostId -or $previous.attempts -lt 0 -or $previous.attempts -gt 3){throw 'Invalid durable host state.'}
    $attempts=[int]$previous.attempts;$qrAttempts=[int]$previous.qrAttempts
    if($qrAttempts -lt 0 -or $qrAttempts -gt 3){throw 'Invalid durable QR budget.'}
}
$lastEvent='';$telemetryValidated=$false
function Save-HostStatus([string]$Phase,[string]$Qr='not-configured') {
    @{version=1;hostId=$HostId;service='nexaconnect-local-pilot-host';phase=$Phase;qr=$Qr;attempts=$script:attempts;qrAttempts=$script:qrAttempts;processId=$PID;startTicks=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks;observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');businessAccessVerified=$false;releaseReady=$false}|ConvertTo-Json|Set-Content "$directory/status.pending.json"
    [IO.File]::Move("$directory/status.pending.json","$directory/status.json",$true)
    $event="$Phase/$Qr/$script:attempts"
    if($script:telemetryValidated -and $event -cne $script:lastEvent){
        $currentFiles=Get-TerminalFileHashes "$directory/telemetry"
        if($currentFiles.Count -ne $config.telemetryFiles.Count -or @($currentFiles.Keys|Where-Object {$currentFiles[$_] -cne $config.telemetryFiles[$_]}).Count){$script:telemetryValidated=$false;return}
        $script:lastEvent=$event
        $log="$directory/events.private.jsonl"
        if((Test-Path $log) -and (Get-Item $log).Length -gt 65536){[IO.File]::Move($log,"$log.previous",$true)}
        $arguments=@("$directory/telemetry/NexaConnect.LocalPilotTelemetry.dll",$Phase,[string]$script:attempts,$Qr,[guid]::NewGuid().ToString('D'))
        if($config.otlpEndpoint){$arguments+=@($config.otlpEndpoint)}
        & dotnet @arguments >> $log 2>> "$directory/telemetry.error.private.log"
    }
}
function Invoke-HostLifecycle([string]$Name,[string]$Script,[string[]]$Arguments) {
    $op=[guid]::NewGuid().ToString('N');$env:NEXACONNECT_LOCAL_PILOT_HOST_ID=$HostId
    $child=Start-Process $config.pwsh -ArgumentList (@('-NoProfile','-NonInteractive','-File',"`"$PSScriptRoot/$Script`"")+$Arguments) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$directory/$Name-$op.private.log" -RedirectStandardError "$directory/$Name-$op.error.private.log"
    [void]$child.Handle;$child.WaitForExit()
    if($child.ExitCode -ne 0){throw 'Owned host lifecycle operation failed.'}
}
try{
    Assert-LocalPilotTask (Get-ScheduledTask -TaskName $config.taskName) $config $root
    Assert-LocalPilotHostBinding $root $config
    $telemetryValidated=$true
    Save-HostStatus 'starting'
    while($true){
      try{
        if(Test-Path "$directory/retry.request"){
            Assert-LocalPilotHostBinding $root $config
            Remove-Item -LiteralPath "$directory/retry.request"
            $attempts=0;$qrAttempts=0;$stableSince=$null
        }
        $run=Assert-TerminalPath (Get-DraftRunDirectory $root $config.runId)
        $state=Get-Content "$run/state.json" -Raw|ConvertFrom-Json -AsHashtable
        $checks=@(Get-LocalPilotProcessSnapshot $run $state)
        $decision=Get-LocalPilotHostDecision $checks (Test-Path "$directory/stop.request") $attempts
        if($decision -eq 'stop'){
            if(Get-Process NexaConnect.POS -ErrorAction SilentlyContinue){Save-HostStatus 'stop-waiting-pos';Start-Sleep -Seconds 3;continue}
            $maintenance=$null
            try{
                $maintenance=Open-DraftLifecycleLock (Assert-TerminalPath "$root/.runstate/local-pilot/$($config.runId)")
                if($config.qrPilotId){Invoke-HostLifecycle 'qr-stop' 'qr-pilot.ps1' @('-Action','Stop','-PilotId',$config.qrPilotId)}
                Invoke-HostLifecycle 'stop' 'first-draft-stack.ps1' @('-Action','Stop','-RunId',$config.runId)
                Save-HostStatus 'stopped';break
            }catch{Save-HostStatus 'stop-blocked'}finally{if($maintenance){$maintenance.Dispose()}}
        }
        elseif($decision -eq 'resume'){
            $stableSince=$null;$maintenance=$null
            try{
                $maintenance=Open-DraftLifecycleLock (Assert-TerminalPath "$root/.runstate/local-pilot/$($config.runId)")
                Assert-LocalPilotTask (Get-ScheduledTask -TaskName $config.taskName) $config $root
                Assert-LocalPilotHostBinding $root $config
                $attempts++;Save-HostStatus 'recovering'
                Invoke-HostLifecycle 'resume' 'first-draft-stack.ps1' @('-Action','Resume','-RunId',$config.runId)
            }catch{Save-HostStatus 'recovery-blocked'}finally{if($maintenance){$maintenance.Dispose()}}
            Start-Sleep -Seconds ([Math]::Min(60,10*[Math]::Max(1,$attempts)))
        }
        elseif($decision -eq 'running'){
            if($null -eq $stableSince){$stableSince=[DateTimeOffset]::UtcNow}
            if(([DateTimeOffset]::UtcNow-$stableSince).TotalSeconds -ge 60){$attempts=0}
            $qr=Get-LocalPilotHostQrState $root $config
            if($qr -eq 'stopped' -and $qrAttempts -lt 3){
                try{
                    $maintenance=Open-DraftLifecycleLock (Assert-TerminalPath "$root/.runstate/local-pilot/$($config.runId)")
                    try{
                        Assert-LocalPilotHostBinding $root $config
                        $qrAttempts++;Save-HostStatus 'running' 'recovering'
                        Invoke-HostLifecycle 'qr-stop' 'qr-pilot.ps1' @('-Action','Stop','-PilotId',$config.qrPilotId)
                        Invoke-HostLifecycle 'qr-start' 'qr-pilot.ps1' @('-Action','Start','-PilotId',$config.qrPilotId)
                        $qr=Get-LocalPilotHostQrState $root $config
                    }finally{$maintenance.Dispose()}
                }catch{$qr='blocked-refresh-required'}
            }
            Save-HostStatus 'running' $qr
        }
        else{Save-HostStatus $decision}
      }catch{Save-HostStatus 'recovery-blocked'}
        Start-Sleep -Seconds 5
    }
}catch{Save-HostStatus 'recovery-blocked';throw 'Background host initialization failed; inspect its owned status.'}finally{$workerLock.Dispose()}
