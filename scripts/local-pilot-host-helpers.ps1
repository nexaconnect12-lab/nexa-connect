function Read-LocalPilotHost([string]$Root,[string]$Id) {
    Assert-DraftRunId $Id
    $directory=Assert-TerminalPath "$Root/.runstate/local-pilot-host/$Id"
    $hostConfig=Get-Content "$directory/host.json" -Raw|ConvertFrom-Json -AsHashtable
    if($hostConfig.version -cne 1 -or $hostConfig.kind -cne 'normal-local-pilot-host' -or $hostConfig.id -cne $Id -or
       $hostConfig.userSid -cne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or
       $hostConfig.taskName -cne "NexaConnect-LocalPilot-$Id" -or $hostConfig.enableLogonStart -isnot [bool]){throw 'Not this Windows account''s owned normal-pilot host.'}
    Assert-DraftRunId $hostConfig.runId
    return $hostConfig
}
function Get-LocalPilotHostQrState([string]$Root,$Config) {
    if(-not $Config.qrPilotId){return 'not-configured'}
    Assert-DraftRunId $Config.qrPilotId
    $directory=Assert-TerminalPath "$Root/.runstate/qr7/$($Config.qrPilotId)"
    if((Get-FileHash "$directory/activation.json").Hash.ToLowerInvariant() -cne $Config.qrActivationSha256){return 'changed-owner'}
    $plan=Get-Content "$directory/plan.json" -Raw|ConvertFrom-Json
    if($plan.stackRunId -cne $Config.runId){return 'changed-owner'}
    $listeners=@(Get-NetTCPConnection -LocalAddress $plan.listenAddress -LocalPort ([uri]$plan.publicOrigin).Port -State Listen -ErrorAction SilentlyContinue)
    if(-not(Test-Path "$directory/edge-process.json")){return $(if($listeners.Count){'changed-owner'}else{'stopped'})}
    $record=Get-Content "$directory/edge-process.json" -Raw|ConvertFrom-Json -AsHashtable
    if(-not(Assert-DraftProcess $record (Get-Process -Id $record.id -ErrorAction SilentlyContinue))){return $(if($listeners.Count){'changed-owner'}else{'stopped'})}
    $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.id)"
    if($actual.ExecutablePath -cne (Get-Command dotnet).Source -or $actual.CommandLine -notlike "*`"$directory\edge\NexaConnect.QrPilotEdge.dll`"*" -or @($listeners|Where-Object OwningProcess -ne $record.id).Count){return 'changed-owner'}
    return $(if($listeners.Count){'running'}else{'unresponsive'})
}
function Get-LocalPilotHostArguments([string]$Root,[string]$Id) {
    Assert-DraftRunId $Id
    return "-NoProfile -NonInteractive -WindowStyle Hidden -File `"$Root\scripts\local-pilot-host-worker.ps1`" -HostId $Id"
}
function Get-LocalPilotHostPublicStatus($Status,[string]$Id) {
    $phases=@('starting','running','recovering','recovery-blocked','blocked-owner','blocked-budget','degraded-live-process','stop-waiting-pos','stop-blocked','stopped')
    $qr=@('not-configured','running','stopped','unresponsive','changed-owner','recovering','blocked-refresh-required')
    $time=[DateTimeOffset]::MinValue
    if($Status.version -cne 1 -or $Status.hostId -cne $Id -or $Status.phase -cnotin $phases -or $Status.qr -cnotin $qr -or
       $Status.attempts -lt 0 -or $Status.attempts -gt 3 -or $Status.qrAttempts -lt 0 -or $Status.qrAttempts -gt 3 -or
       $Status.processId -le 0 -or $Status.startTicks -le 0 -or -not [DateTimeOffset]::TryParse($Status.observedAtUtc,[ref]$time)){throw 'Invalid owned host status.'}
    return @{hostId=$Id;service='nexaconnect-local-pilot-host';phase=[string]$Status.phase;qr=[string]$Status.qr;attempts=[int]$Status.attempts;qrAttempts=[int]$Status.qrAttempts;processId=[int]$Status.processId;observedAtUtc=$time.ToUniversalTime().ToString('O');businessAccessVerified=$false;releaseReady=$false}
}
function Assert-LocalPilotTask($Task,$Config,[string]$Root) {
    $sid=[string]$Task.Principal.UserId
    if($sid -notlike 'S-1-*'){$sid=[Security.Principal.NTAccount]::new($sid).Translate([Security.Principal.SecurityIdentifier]).Value}
    if($Task.TaskName -cne $Config.taskName -or $Task.TaskPath -cne '\' -or $sid -cne $Config.userSid -or
       [string]$Task.Principal.RunLevel -notin @('Limited','0') -or [string]$Task.Principal.LogonType -notin @('Interactive','3') -or
       @($Task.Actions).Count -ne 1 -or $Task.Actions[0].Execute -cne $Config.pwsh -or
       $Task.Actions[0].Arguments -cne (Get-LocalPilotHostArguments $Root $Config.id) -or
       $Task.Actions[0].WorkingDirectory -cne $Root -or $Task.Settings.ExecutionTimeLimit -cne 'PT0S' -or
       [string]$Task.Settings.MultipleInstances -notin @('IgnoreNew','2') -or $Task.Settings.RestartCount -ne 3 -or
       $Task.Settings.RestartInterval -cne 'PT1M' -or
       $Task.Settings.DisallowStartIfOnBatteries -or $Task.Settings.StopIfGoingOnBatteries -or
       @($Task.Triggers|Where-Object {$null -ne $_}).Count -ne $(if($Config.enableLogonStart){1}else{0})){throw 'Scheduled task ownership or definition changed; no task was stopped, replaced or adopted.'}
    if($Config.enableLogonStart){
        $triggerSid=[string]$Task.Triggers[0].UserId
        if($triggerSid -notlike 'S-1-*'){$triggerSid=[Security.Principal.NTAccount]::new($triggerSid).Translate([Security.Principal.SecurityIdentifier]).Value}
        if($Task.Triggers[0].CimClass.CimClassName -cne 'MSFT_TaskLogonTrigger' -or $triggerSid -cne $Config.userSid){throw 'Logon trigger owner changed.'}
    }
}
function Get-LocalPilotHostDecision([object[]]$Checks,[bool]$StopRequested,[int]$Attempts) {
    if($Attempts -lt 0 -or $Attempts -gt 3){throw 'Invalid durable recovery budget.'}
    if($StopRequested){return 'stop'}
    if(@($Checks|Where-Object status -eq 'changed-owner').Count){return 'blocked-owner'}
    if(@($Checks|Where-Object status -eq 'unresponsive').Count){return 'degraded-live-process'}
    if(@($Checks|Where-Object status -eq 'stopped').Count){return $(if($Attempts -ge 3){'blocked-budget'}else{'resume'})}
    return 'running'
}
function Test-LocalPilotHostReady($Status) {
    return $null -ne $Status -and $Status.phase -ceq 'running' -and
        $Status.qr -cin @('not-configured','running','stopped','unresponsive','changed-owner','blocked-refresh-required')
}
function Assert-LocalPilotHostBinding([string]$Root,$Config) {
    if((Get-FileHash "$Root/scripts/local-pilot-host-worker.ps1").Hash.ToLowerInvariant() -cne $Config.workerSha256){throw 'Host worker source changed.'}
    if($Config.Contains('telemetryFiles')){
        $files=Get-TerminalFileHashes "$Root/.runstate/local-pilot-host/$($Config.id)/telemetry"
        if($files.Count -ne $Config.telemetryFiles.Count -or @($files.Keys|Where-Object {$files[$_] -cne $Config.telemetryFiles[$_]}).Count){throw 'Host telemetry artifact binding changed.'}
    }
    $install=Assert-TerminalPath $Config.installDirectory
    Assert-TerminalInstallOwnership $install
    $active=Get-Content "$install/active.json" -Raw|ConvertFrom-Json -AsHashtable
    if($active.runId -cne $Config.runId -or $active.releaseId -cne $Config.releaseId){throw 'Host installation target changed; stop and install a fresh host binding.'}
    $package=Assert-TerminalPath "$install/releases/$($Config.releaseId)"
    $manifest=Read-TerminalPackage $package
    if((Get-FileHash "$package/terminal-package.json").Hash.ToLowerInvariant() -cne $Config.packageSha256){throw 'Host package binding changed.'}
    $run=Assert-TerminalPath (Get-DraftRunDirectory $Root $Config.runId)
    $state=Get-Content "$run/state.json" -Raw|ConvertFrom-Json -AsHashtable
    $profile=Get-Content "$package/appsettings.json" -Raw|ConvertFrom-Json -AsHashtable
    $reference=Get-Content "$run/reference.json" -Raw|ConvertFrom-Json -AsHashtable
    Assert-LocalPilotProfile $state $profile $reference (Test-Path "$Root/.runstate/pos-recovery-lab/$($Config.runId)/lab.json")
    if((Get-FileHash "$run/reference.json").Hash.ToLowerInvariant() -cne $Config.referenceSha256 -or (Get-DraftQrPilotArtifactHash $run) -cne $Config.backendArtifacts){throw 'Host reference or server artifacts changed.'}
    $current=& node "$Root/scripts/release/source.mjs"
    if($LASTEXITCODE -ne 0){throw 'Host source inventory failed.'}
    Assert-LocalPilotPublished $state ($current|ConvertFrom-Json -AsHashtable) $Config.backendArtifacts
    Assert-TerminalCandidateUnchanged @{revision=$manifest.sourceRevision;dirty=$manifest.sourceDirty;inputHash=$manifest.sourceInputHash} (Get-DraftCandidate $Root)
}
