function Read-PosRecoveryLab([string]$Root,[string]$Id){
    Assert-UatId $Id
    $directory=Assert-TerminalPath "$Root/.runstate/pos-recovery-lab/$Id"
    $lab=ConvertFrom-UatJson (Get-Content -LiteralPath (Assert-TerminalPath "$directory/lab.json") -Raw)
    if($lab.version -cne 1 -or $lab.kind -cne 'nexaconnect-pos-recovery-lab' -or $lab.id -cne $Id -or $lab.runId -cne $Id){throw 'Not an owned recovery lab.'}
    return $lab
}
function Assert-PosRecoveryLabProfile($Profile,$State,$Reference){
    $scope=Get-TerminalScope $Profile
    if($Profile.Pos.UseIsolatedLocalPilotStorage -isnot [bool] -or -not $Profile.Pos.UseIsolatedLocalPilotStorage -or
        $Profile.Pos.PaymentMethod -cne 'cash_manual' -or $Profile.Pos.EnableOmiseTestCheckout -cne $false -or
        $Profile.Identity.Authority -cne $State.authority -or $State.realm -cne "nexa-review-it-$($State.runId)"){throw 'Invalid isolated recovery profile.'}
    foreach($key in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){
        $referenceKey=$key.Substring(0,1).ToLowerInvariant()+$key.Substring(1)
        if($scope[$key] -cne ([guid]$Reference[$referenceKey]).ToString('D')){throw 'Recovery lab scope differs from its owned reference.'}
    }
    foreach($pair in @(@('PosApi','POS'),@('OrderApi','Order'),@('CatalogApi','Catalog'),@('PaymentApi','Payment'))){
        if(([uri]$Profile.Services[$pair[0]]).AbsoluteUri -cne ([uri]$State.urls[$pair[1]]).AbsoluteUri){throw 'Recovery lab service endpoint differs.'}
    }
}
function Get-PosRecoveryLabBinding([string]$Root,[string]$Id){
    $lab=Read-PosRecoveryLab $Root $Id
    $run=Assert-TerminalPath (Get-DraftRunDirectory $Root $Id)
    $lock=Open-DraftLifecycleLock $run
    try{
        $state=ConvertFrom-UatJson (Get-Content -LiteralPath "$run/state.json" -Raw)
        if($state.runId -cne $Id -or $state.project -cne "nexa-draft-$Id" -or $state.status -cne 'Ready' -or -not $state.provisionedBranch){throw 'Recovery lab is not Ready.'}
        Assert-DraftUrls $state.urls
        $target=ConvertFrom-UatJson (Get-Content -LiteralPath (Assert-TerminalPath "$Root/.runstate/pos-recovery-lab/$Id/target.json") -Raw)
        Assert-UatId $target.releaseId
        $packagePath=Assert-TerminalPath "$run/terminal-packages/$($target.releaseId)"
        $package=Read-TerminalPackage $packagePath
        if($package.runId -cne $Id -or $package.releaseId -cne $target.releaseId){throw 'Recovery lab package owner differs.'}
        $profile=ConvertFrom-UatJson (Get-Content -LiteralPath "$packagePath/appsettings.json" -Raw)
        $reference=ConvertFrom-UatJson (Get-Content -LiteralPath "$run/reference.json" -Raw)
        Assert-PosRecoveryLabProfile $profile $state $reference
        foreach($name in $state.urls.Keys){
            if(-not $state.processes.Contains($name)){throw 'Recovery lab dependency is stopped.'}
            $record=$state.processes[$name]
            if(-not(Assert-DraftProcess $record (Get-Process -Id $record.id -ErrorAction SilentlyContinue))){throw 'Recovery lab dependency identity changed.'}
            $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.id)"
            $expected=Join-Path $run "apps/$name/$($record.assemblyName).dll"
            if($actual.ExecutablePath -cne (Get-Command dotnet).Source -or $actual.CommandLine -notlike "*`"$expected`"*" -or
                -not @(Get-NetTCPConnection -State Listen -LocalPort ([uri]$state.urls[$name]).Port -ErrorAction SilentlyContinue|Where-Object OwningProcess -eq $record.id).Count){throw 'Recovery lab executable/listener changed.'}
        }
        $snapshot=@{runId=$Id;labId=$Id;releaseId=$target.releaseId;
            labManifestHash=(Get-FileHash "$Root/.runstate/pos-recovery-lab/$Id/target.json").Hash.ToLowerInvariant();
            packageManifestHash=(Get-FileHash "$packagePath/terminal-package.json").Hash.ToLowerInvariant();
            configurationHash=(Get-FileHash "$packagePath/appsettings.json").Hash.ToLowerInvariant();
            referenceHash=(Get-FileHash "$run/reference.json").Hash.ToLowerInvariant();backendArtifacts=Get-DraftQrPilotArtifactHash $run}
        foreach($key in @('configurationHash','referenceHash','backendArtifacts')){if($snapshot[$key] -cne $target[$key]){throw 'Recovery lab target files changed.'}}
        return @{snapshot=$snapshot;qrCandidateFresh=$false}
    }finally{$lock.Dispose()}
}
function Get-PosRecoveryProtocolRestore($Saved,[string]$Current,[string]$OwnedCommand){
    if($Current -cne $OwnedCommand){throw 'Callback registration changed; preserve its current owner and review restoration.'}
    foreach($key in @('root','url','command')){
        if($Saved[$key].exists -isnot [bool] -or ($Saved[$key].exists -and ($Saved[$key].value -isnot [string] -or $Saved[$key].value.Length -gt 4096))){throw 'Invalid callback backup.'}
    }
    return $Saved
}
