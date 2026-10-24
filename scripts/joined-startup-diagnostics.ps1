# Only bounded categories are returned; never expose raw container logs or configuration.
function ConvertTo-JoinedBrokerStartupSummary($state,[string]$log) {
    if($null -eq $state -or $state.Running -isnot [bool] -or $state.OOMKilled -isnot [bool] -or ($state.ExitCode -isnot [int] -and $state.ExitCode -isnot [long]) -or $state.ExitCode -lt 0 -or $state.ExitCode -gt 255) {
        return [pscustomobject]@{service='rabbitmq';reason='diagnostic_unavailable'}
    }
    $reason=if($state.OOMKilled){'oom_killed'}
        elseif($log -match 'Failed to create .*thread|Cannot allocate memory'){'runtime_resource_exhaustion'}
        elseif($log -match 'permission denied|eacces'){'filesystem_permission'}
        elseif($log -match 'address already in use|eaddrinuse'){'listener_conflict'}
        elseif($log -match 'BOOT FAILED|Error during startup'){'broker_boot_failure'}
        elseif(-not $state.Running -and $state.ExitCode -ne 0){'process_exit'}
        elseif($state.Running){'running'}else{'not_ready'}
    return [pscustomobject]@{service='rabbitmq';running=$state.Running;exitCode=$state.ExitCode;oomKilled=$state.OOMKilled;reason=$reason}
}
function Get-JoinedBrokerStartupDiagnostic([string]$DockerExecutable,[string[]]$ComposeArguments,[string]$ProjectName) {
    try {
        if($ProjectName -notmatch '^nexa-(financial-portal|end-of-day-portal|day-close-portal|cashier-day-close|cashier-day-cutoff)-[a-f0-9]{32}$'){throw 'Invalid owned project.'}
        $projectIndex=[Array]::IndexOf($ComposeArguments,'-p')
        if($projectIndex -lt 0 -or $projectIndex+1 -ge $ComposeArguments.Length -or $ComposeArguments[$projectIndex+1] -ne $ProjectName){throw 'Project mismatch.'}
        $ids=@(& $DockerExecutable @ComposeArguments ps -aq rabbitmq 2>$null)
        if($LASTEXITCODE -ne 0 -or $ids.Count -ne 1 -or $ids[0] -notmatch '^[a-f0-9]{12,64}$'){throw 'Owned broker absent.'}
        $id=$ids[0]
        $owner=& $DockerExecutable inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' $id 2>$null
        if($LASTEXITCODE -ne 0 -or $owner -ne $ProjectName){throw 'Broker owner mismatch.'}
        $stateJson=& $DockerExecutable inspect --format '{{json .State}}' $id 2>$null
        if($LASTEXITCODE -ne 0){throw 'State unavailable.'}
        $state=$stateJson | ConvertFrom-Json
        $log=(@(& $DockerExecutable logs --tail 120 $id 2>&1) | ForEach-Object { [string]$_ }) -join "`n"
        return ConvertTo-JoinedBrokerStartupSummary $state $log
    } catch { return [pscustomobject]@{service='rabbitmq';reason='diagnostic_unavailable'} }
}
