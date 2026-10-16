#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../scripts/joined-startup-diagnostics.ps1')
$state='{"Running":false,"OOMKilled":false,"ExitCode":1,"Error":"private-secret-marker"}' | ConvertFrom-Json
$cases=@(
    @('Failed to create scheduler thread 12','runtime_resource_exhaustion'),
    @('permission denied','filesystem_permission'),
    @('address already in use','listener_conflict'),
    @('BOOT FAILED','broker_boot_failure'),
    @('private-secret-marker','process_exit')
)
foreach($case in $cases){
    $result=ConvertTo-JoinedBrokerStartupSummary $state ($case[0]+' private-secret-marker')
    if($result.reason -ne $case[1] -or ($result|ConvertTo-Json) -match 'private-secret-marker'){throw 'Unsafe or incorrect startup diagnostic.'}
}
$state.OOMKilled=$true
if((ConvertTo-JoinedBrokerStartupSummary $state '').reason -ne 'oom_killed'){throw 'OOM attribution missing.'}
if((ConvertTo-JoinedBrokerStartupSummary $null 'private-secret-marker').reason -ne 'diagnostic_unavailable'){throw 'Missing state was not bounded.'}
# Invalid project is rejected before invoking any executable.
if((Get-JoinedBrokerStartupDiagnostic 'must-not-execute' @() 'unrelated-project').reason -ne 'diagnostic_unavailable'){throw 'Invalid project accepted.'}
Write-Output 'Passed eight bounded startup diagnostic cases without exposing raw logs or configuration.'
$global:LASTEXITCODE=0
