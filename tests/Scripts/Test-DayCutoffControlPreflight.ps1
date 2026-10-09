#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../scripts/day-close-joined-helpers.ps1')
$run='a'*32
foreach($action in @('stop-pos','start-pos','start-pos-held-delivery','stop-order','start-order-delivery')){
    $request=[pscustomobject]@{runId=$run;requestId=('b'*32);action=$action}
    if(-not(Test-DayCloseControlRequest $request $run $true)){throw 'Valid owned action rejected.'}
    if(($action-like'*order*'-or$action-eq'start-pos-held-delivery')-and(Test-DayCloseControlRequest $request $run $false)){throw 'Older mode accepted cutoff-only interruption.'}
}
$cases=@(
    [pscustomobject]@{runId=('c'*32);requestId=('b'*32);action='stop-pos'},
    [pscustomobject]@{runId=$run;requestId='invalid';action='stop-pos'},
    [pscustomobject]@{runId=$run;requestId=('b'*32);action='kill-process'},
    [pscustomobject]@{runId=$run;requestId=('b'*32);action='stop-pos';pid=1},
    [pscustomobject]@{runId=$run;requestId=('b'*32);action='stop-pos';url='https://example.org'},
    [pscustomobject]@{runId=$run;requestId=('b'*32);action=@('stop-pos','stop-order')}
)
foreach($request in $cases){if(Test-DayCloseControlRequest $request $run $true){throw 'Invalid owned control request accepted.'}}
Write-Output 'Passed five owned actions, three old-mode denials and six malformed control denials without infrastructure.'
