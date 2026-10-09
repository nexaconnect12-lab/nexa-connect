# Runner-owned control only; no arbitrary executable, PID or production endpoint.
function Test-DayCloseControlRequest($request,[string]$expectedRun,[bool]$cutoffMode) {
    if($null-eq$request-or$expectedRun-notmatch'^[a-f0-9]{32}$'){return $false}
    if(@($request.PSObject.Properties.Name).Count-ne3-or$request.runId-isnot[string]-or$request.requestId-isnot[string]-or$request.action-isnot[string]){return $false}
    $allowed=if($cutoffMode){@('stop-pos','start-pos','start-pos-held-delivery','stop-order','start-order-delivery')}else{@('stop-pos','start-pos')}
    return $request.runId-eq$expectedRun-and$request.requestId-match'^[a-f0-9]{32}$'-and$request.action-in$allowed
}
function Invoke-DayCloseJoinedBrowser {
    $control=Join-Path $run 'pos-control';New-Item -ItemType Directory -Path $control -Force|Out-Null
    $requestPath=Join-Path $control 'request.json';$seen=[Collections.Generic.HashSet[string]]::new()
    $info=[Diagnostics.ProcessStartInfo]::new((Get-Command node).Source);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.WorkingDirectory=Join-Path $root 'src/Frontend';$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.ArgumentList.Add((Join-Path $info.WorkingDirectory 'node_modules/@playwright/test/cli.js'))
    $info.ArgumentList.Add('test');$info.ArgumentList.Add('--config');$info.ArgumentList.Add($(if($CashierDayCutoff){'playwright.cashier-day-cutoff-live.config.mjs'}elseif($CashierDayClose){'playwright.cashier-day-close-live.config.mjs'}else{'playwright.day-close-live.config.mjs'}))
    $browser=[Diagnostics.Process]::Start($info)
    $browserState=@{Process=$browser;Out=$browser.StandardOutput.ReadToEndAsync();Error=$browser.StandardError.ReadToEndAsync();Name='Browser'}
    $script:processes+=$browserState;$limit=[DateTimeOffset]::UtcNow.AddMinutes(12);$restartNumber=0
    while(-not $browser.HasExited){
        if([DateTimeOffset]::UtcNow-gt$limit){throw 'Day-close browser deadline exceeded.'}
        if(Test-Path -LiteralPath $requestPath){
            if((Get-Item -LiteralPath $requestPath).Length-gt4096){throw 'Oversized runner control request.'}
            $request=Get-Content -LiteralPath $requestPath -Raw|ConvertFrom-Json
            if(-not(Test-DayCloseControlRequest $request $runId ([bool]$CashierDayCutoff))){throw 'Invalid runner control request.'}
            if($seen.Add($request.requestId)){
                if($request.action-in@('stop-order','start-order-delivery')){
                    $orderActive=@($processes|Where-Object{$_.Name-match'^Order(?:Restart[0-9]+)?$'-and-not$_.Process.HasExited})
                    if($request.action-eq'stop-order'){
                        if($orderActive.Count-ne1){throw 'One retained Order child is required.'}
                        $orderActive[0].Process.Kill($true);if(-not$orderActive[0].Process.WaitForExit(10000)){throw 'Owned Order did not stop.'}
                    }else{
                        if($orderActive.Count-ne0){throw 'Order is already running.'}
                        $restartNumber++;$orderConfiguration.Outbox__BatchSize='100'
                        Start-App "OrderRestart$restartNumber" $orderAssembly $orderDirectory $orderConfiguration $ports[6]
                    }
                }else{
                $active=@($processes|Where-Object{$_.Name-match'^POS(?:Restart[0-9]+)?$'-and-not$_.Process.HasExited})
                if($request.action-eq'stop-pos'){
                    if($active.Count-ne1){throw 'One retained POS process handle is required.'}
                    $active[0].Process.Kill($true);if(-not$active[0].Process.WaitForExit(10000)){throw 'Owned POS process did not stop.'}
                }else{
                    if($active.Count-ne0){throw 'POS is already running.'}
                    $restartNumber++;$restartConfiguration=$posConfiguration.Clone()
                    if($request.action-eq'start-pos-held-delivery'){$restartConfiguration.OrderSettlementConsumer__Enabled='false'}
                    Start-App "POSRestart$restartNumber" $posAssembly $posDirectory $restartConfiguration $ports[8]
                }
                }
                $ack=@{requestId=$request.requestId;status='completed'}|ConvertTo-Json -Compress
                $temp=Join-Path $control 'ack.tmp';Set-Content -LiteralPath $temp -Value $ack
                Move-Item -LiteralPath $temp -Destination (Join-Path $control 'ack.json') -Force
            }
        }
        Start-Sleep -Milliseconds 100
    }
    Write-Output $browserState.Out.GetAwaiter().GetResult()
    if($browser.ExitCode-ne0){throw 'Joined day-close browser acceptance failed; sensitive diagnostics remain local.'}
}
