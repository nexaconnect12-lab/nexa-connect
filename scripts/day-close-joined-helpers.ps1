# Runner-owned control only; no arbitrary executable, PID or production endpoint.
function Invoke-DayCloseJoinedBrowser {
    $control=Join-Path $run 'pos-control';New-Item -ItemType Directory -Path $control -Force|Out-Null
    $requestPath=Join-Path $control 'request.json';$seen=[Collections.Generic.HashSet[string]]::new()
    $info=[Diagnostics.ProcessStartInfo]::new((Get-Command node).Source);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.WorkingDirectory=Join-Path $root 'src/Frontend';$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.ArgumentList.Add((Join-Path $info.WorkingDirectory 'node_modules/@playwright/test/cli.js'))
    $info.ArgumentList.Add('test');$info.ArgumentList.Add('--config');$info.ArgumentList.Add($(if($CashierDayClose){'playwright.cashier-day-close-live.config.mjs'}else{'playwright.day-close-live.config.mjs'}))
    $browser=[Diagnostics.Process]::Start($info)
    $browserState=@{Process=$browser;Out=$browser.StandardOutput.ReadToEndAsync();Error=$browser.StandardError.ReadToEndAsync();Name='Browser'}
    $script:processes+=$browserState;$limit=[DateTimeOffset]::UtcNow.AddMinutes(12);$restartNumber=0
    while(-not $browser.HasExited){
        if([DateTimeOffset]::UtcNow-gt$limit){throw 'Day-close browser deadline exceeded.'}
        if(Test-Path -LiteralPath $requestPath){
            if((Get-Item -LiteralPath $requestPath).Length-gt4096){throw 'Oversized runner control request.'}
            $request=Get-Content -LiteralPath $requestPath -Raw|ConvertFrom-Json
            if($request.runId-ne$runId-or$request.requestId-notmatch'^[a-f0-9]{32}$'-or$request.action-notin@('stop-pos','start-pos')-or @($request.PSObject.Properties.Name).Count-ne3){throw 'Invalid runner control request.'}
            if($seen.Add($request.requestId)){
                $active=@($processes|Where-Object{$_.Name-match'^POS(?:Restart[0-9]+)?$'-and-not$_.Process.HasExited})
                if($request.action-eq'stop-pos'){
                    if($active.Count-ne1){throw 'One retained POS process handle is required.'}
                    $active[0].Process.Kill($true);if(-not$active[0].Process.WaitForExit(10000)){throw 'Owned POS process did not stop.'}
                }else{
                    if($active.Count-ne0){throw 'POS is already running.'}
                    $restartNumber++;Start-App "POSRestart$restartNumber" $posAssembly $posDirectory $posConfiguration $ports[8]
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
