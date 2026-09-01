#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'payment-review-acceptance-helpers.ps1')
$checks = 0
function Must-Reject([scriptblock] $Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unsafe acceptance input was accepted.' }
    $script:checks++
}
foreach ($name in @('', 'nexa-connect', 'production', 'nexa-review-it-123', '../nexa-review-it-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')) {
    Must-Reject { Assert-ReviewAcceptanceProject $name }
}
$name = 'nexa-review-it-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
Assert-ReviewAcceptanceProject $name
$arguments = @(Get-ReviewAcceptanceComposeArguments (Resolve-Path (Join-Path $PSScriptRoot '..')).Path $name)
if ($arguments[-1] -ne $name -or $arguments -notcontains '--env-file' -or $arguments -notcontains '-f') { throw 'Compose isolation flags missing.' }
$checks++
foreach ($address in @('0.0.0.0:5432','localhost:5432','127.0.0.1:22','127.0.0.1:65536','127.0.0.1:5432/','127.0.0.1:5432 extra')) {
    Must-Reject { ConvertFrom-ReviewAcceptancePort $address }
}
if ((ConvertFrom-ReviewAcceptancePort '127.0.0.1:55432') -ne 55432) { throw 'Valid loopback port failed.' }
$checks++
Must-Reject { New-ReviewAcceptanceConnection 55432 'NexaConnect_Order' 'synthetic' }
Must-Reject { New-ReviewAcceptanceConnection 55432 'NexaConnect_Reporting' 'synthetic' }
Must-Reject { & (Join-Path $PSScriptRoot 'test-payment-review-isolated.ps1') }
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection.set_ConnectionString((New-ReviewAcceptanceConnection 55432 'review_order' 'synthetic;quoted=value'))
if ($connection['Password'] -ne 'synthetic;quoted=value' -or $connection['Host'] -ne '127.0.0.1') { throw 'Connection values were not safely encoded.' }
$checks++

# Simulate startup failure without Docker. Cleanup must stay project-scoped and
# injected settings must be restored even when no test process can start.
$global:ReviewAcceptanceFakeCalls = New-Object 'System.Collections.Generic.List[object]'
function Invoke-FakeReviewDocker {
    $global:ReviewAcceptanceFakeCalls.Add(@($args))
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'context') { Write-Output 'npipe://local-acceptance-test'; return }
    if ($args -contains 'up') { $global:LASTEXITCODE = 1 }
}
$names = @('NEXACONNECT_REVIEW_ACCEPTANCE_PASSWORD','NEXACONNECT_ORDER_INTEGRATION_DB','NEXACONNECT_REPORTING_INTEGRATION_DB','NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB','NEXACONNECT_RABBITMQ_INTEGRATION_URI','DOCKER_HOST')
$saved = @{}
try {
    foreach ($item in $names) { $saved[$item]=[Environment]::GetEnvironmentVariable($item); [Environment]::SetEnvironmentVariable($item,'synthetic-prior-value') }
    $env:DOCKER_HOST='npipe://local-acceptance-test'
    Must-Reject { & (Join-Path $PSScriptRoot 'test-payment-review-isolated.ps1') -ConfirmDisposableInfrastructure -DockerExecutable 'Invoke-FakeReviewDocker' }
    foreach ($item in $names | Where-Object { $_ -ne 'DOCKER_HOST' }) { if ([Environment]::GetEnvironmentVariable($item) -ne 'synthetic-prior-value') { throw 'Acceptance environment was not restored.' } }
    $checks++
    $down = @($global:ReviewAcceptanceFakeCalls | Where-Object { $_ -contains 'down' })
    if ($down.Count -ne 1) { throw 'Failed startup did not clean its project exactly once.' }
    foreach ($call in $global:ReviewAcceptanceFakeCalls) {
        if ($call[0] -ne 'compose') { continue }
        $index = [Array]::IndexOf($call,'-p')
        if ($index -lt 0 -or $call -notcontains '-f' -or $call -notcontains '--env-file') { throw 'A cleanup/startup command escaped Compose isolation.' }
        Assert-ReviewAcceptanceProject $call[$index+1]
    }
    $checks++
} finally {
    foreach ($item in $names) { [Environment]::SetEnvironmentVariable($item,$saved[$item]) }
}
Write-Output "$checks acceptance safety checks passed."
