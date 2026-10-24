#requires -Version 7.4
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm generated temporary broker containers and cleanup.'}
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId=[Guid]::NewGuid().ToString('N');$run=Join-Path $root ".runstate/rabbit-health/$runId"
New-Item -ItemType Directory -Path $run | Out-Null
$names=@();$results=@();$cleanup=$false
try {
    foreach($mode in @('root','rabbitmq')) {
        $name="nexa-draft-rabbit-$mode-$runId";$names+=$name
        $health=if($mode -eq 'root'){'rabbitmq-diagnostics -q ping'}else{'gosu rabbitmq rabbitmq-diagnostics -q ping'}
        # Pause after directory ownership setup to deterministically expose health/server
        # cookie creation ordering. No ports, host mounts or existing volume are used.
        & docker create --name $name --label "nexa.draft.health-test=$runId" --entrypoint bash --health-cmd $health --health-interval 1s --health-timeout 5s --health-retries 30 rabbitmq:4-management -c 'chown rabbitmq:rabbitmq /var/lib/rabbitmq; sleep 8; exec gosu rabbitmq rabbitmq-server' | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Controlled broker container creation failed.'}
        & docker start $name | Out-Null
        $deadline=[DateTimeOffset]::UtcNow.AddSeconds(75);$healthStatus=''
        while([DateTimeOffset]::UtcNow -lt $deadline) {
            $state=(& docker inspect --format '{{json .State}}' $name | ConvertFrom-Json)
            if($LASTEXITCODE -ne 0){throw 'Controlled broker state unavailable.'}
            if(-not $state.Running){break}
            $healthStatus=$state.Health.Status
            if($healthStatus -eq 'healthy'){break}
            Start-Sleep -Seconds 1
        }
        # No cookie contents or raw broker logs enter the evidence.
        $log=(& docker logs $name 2>&1) -join "`n"
        $permissionFailure=$log -match '(?i)(permission denied|eacces)'
        $results+=@{mode=$mode;running=[bool]$state.Running;exitCode=[int]$state.ExitCode;healthy=($healthStatus -eq 'healthy');permissionFailure=$permissionFailure}
    }
    if(-not $results[0].permissionFailure -or $results[0].exitCode -ne 1 -or -not $results[1].healthy){throw 'Cookie ownership race/fix was not demonstrated; do not claim a proven cause.'}
} finally {
    $cleanup=$true
    foreach($name in $names) {
        $owner=& docker inspect --format '{{ index .Config.Labels "nexa.draft.health-test" }}' $name 2>$null
        if($LASTEXITCODE -eq 0 -and $owner -eq $runId){& docker rm -f -v $name | Out-Null;if($LASTEXITCODE -ne 0){$cleanup=$false}}else{$cleanup=$false}
    }
    @{runId=$runId;results=$results;cleanupVerified=$cleanup;productionVerified=$false}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $run 'verification.json')
    if(-not $cleanup){throw 'Controlled broker cleanup could not be verified.'}
}
Write-Output "Controlled cookie ownership failure and user-scoped health success verified: $run/verification.json"
