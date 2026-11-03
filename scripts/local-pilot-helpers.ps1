# Pure guards for the normal local pilot. Never edits recovery or accepts a lab scope.
function Assert-LocalPilotProfile($State,$Profile,$Reference,[bool]$IsLab) {
    if($IsLab -or ($Reference.Contains('recoveryLabAccessCompleted') -and $Reference.recoveryLabAccessCompleted)){throw 'Recovery lab selected. Use pos-recovery-lab.ps1; normal-pilot startup cannot adopt a lab.'}
    if($State.runId -cnotmatch '^[a-f0-9]{32}$' -or $State.project -cne "nexa-draft-$($State.runId)" -or
       $State.realm -cne "nexa-review-it-$($State.runId)" -or $State.status -notin @('Ready','Stopped') -or -not $State.provisionedBranch){throw 'A provisioned normal local pilot is required.'}
    $scope=Get-TerminalScope $Profile
    if($Profile.Identity.Authority -cne $State.authority){throw 'Installed identity differs from selected pilot.'}
    foreach($key in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){
        $referenceKey=$key.Substring(0,1).ToLowerInvariant()+$key.Substring(1)
        if($scope[$key] -cne ([guid]$Reference[$referenceKey]).ToString('D')){throw 'Installed scope differs from selected pilot.'}
    }
    foreach($pair in @(@('PosApi','POS'),@('OrderApi','Order'),@('CatalogApi','Catalog'),@('PaymentApi','Payment'),@('MediaApi','Media'))){
        $configured=[string]$Profile.Services[$pair[0]]
        if($configured -and (-not $State.urls.Contains($pair[1]) -or ([uri]$configured).AbsoluteUri -cne ([uri]$State.urls[$pair[1]]).AbsoluteUri)){throw 'Installed service differs from selected pilot.'}
    }
}
function Assert-LocalPilotPublished($State,$CurrentSource,[string]$Artifacts) {
    # Legacy QR stacks already carry both inventories. Non-QR stacks acquire them at Start.
    $source=if($State.Contains('publishedSource')){$State.publishedSource}elseif($State.Contains('qrPilotSource')){$State.qrPilotSource}else{$null}
    $hash=if($State.Contains('publishedArtifacts')){$State.publishedArtifacts}elseif($State.Contains('qrPilotArtifacts')){$State.qrPilotArtifacts}else{$null}
    if(-not $source -or ($source|ConvertTo-Json -Depth 8 -Compress) -cne ($CurrentSource|ConvertTo-Json -Depth 8 -Compress) -or
       $hash -cnotmatch '^[a-f0-9]{64}$' -or $hash -cne $Artifacts){throw 'Published source or artifacts changed. Close POS and use local-pilot.ps1 -Action Refresh; no service was restarted.'}
}
function Get-LocalPilotRecoveryPlan([object[]]$Checks,[bool]$PosRunning) {
    if(@($Checks|Where-Object {$_.status -eq 'changed-owner'}).Count){throw 'A service process or listener has another owner. No service was stopped or adopted.'}
    $missing=@($Checks|Where-Object {$_.status -ne 'responding'})
    if($missing.Count -and $PosRunning){throw 'Finish the current action and close POS before recovering services; original payment recovery is preserved.'}
    return $(if($missing.Count){'resume'}else{'open'})
}
function Get-LocalPilotIdentityBinding([string[]]$Mappings,[int]$ContainerCount,[int]$Port,[bool]$HasListener) {
    if($Port -lt 1024 -or $Port -gt 65535){throw 'Invalid owned identity port.'}
    if($ContainerCount -eq 0){return $(if($HasListener){'changed-owner'}else{'stopped'})}
    if($ContainerCount -ne 1 -or $Mappings.Count -ne 1 -or $Mappings[0] -cne "127.0.0.1:$Port"){return 'changed-owner'}
    return 'owned'
}
function Get-LocalPilotProcessSnapshot([string]$Run,$State) {
    $checks=@()
    foreach($name in $State.urls.Keys){
        $expectedAssembly=if($name -ceq 'Bff'){'NexaConnect.CustomerBff'}else{"NexaConnect.Services.$name"}
        $record=if($State.processes.Contains($name)){$State.processes[$name]}else{$null}
        $p=if($record){Get-Process -Id $record.id -ErrorAction SilentlyContinue}else{$null}
        $owned=$record -and (Assert-DraftProcess $record $p)
        $listeners=@(Get-NetTCPConnection -State Listen -LocalPort ([uri]$State.urls[$name]).Port -ErrorAction SilentlyContinue)
        $status='stopped'
        if($listeners.Count -and (-not $owned -or @($listeners|Where-Object OwningProcess -ne $record.id).Count)){$status='changed-owner'}
        elseif($owned){
            $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.id)"
            $expected=Join-Path $Run "apps/$name/$expectedAssembly.dll"
            if($record.assemblyName -cne $expectedAssembly -or $actual.ExecutablePath -cne (Get-Command dotnet).Source -or $actual.CommandLine -notlike "*`"$expected`"*"){$status='changed-owner'}
            elseif($listeners.Count){$status='running'}else{$status='unresponsive'}
        }
        $checks+=@{service=$name;status=$status}
    }
    return $checks
}
