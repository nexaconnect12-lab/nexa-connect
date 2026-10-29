function Test-DraftBranchPromptPayEnabled($State){
    if($State.Contains('branchPromptPayQrEnabled') -and $State.branchPromptPayQrEnabled -isnot [bool]){throw 'Branch PromptPay selection must be Boolean.'}
    return $State.Contains('branchPromptPayQrEnabled') -and $State.branchPromptPayQrEnabled
}
function Enable-DraftBranchPromptPay($State,[string]$Action,[bool]$NoBuild){
    if($Action -cne 'Start' -or $NoBuild -or $State.processes.Count){throw 'Branch PromptPay activation requires a stopped, build-enabled Start.'}
    if(-not $State.Contains('mediaEnabled') -or $State.mediaEnabled -isnot [bool] -or -not $State.mediaEnabled){throw 'Enable the Media profile before branch PromptPay activation.'}
    $State.branchPromptPayQrEnabled=$true
}
