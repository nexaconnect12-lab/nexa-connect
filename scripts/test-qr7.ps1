#requires -Version 7.4
param([switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $IsWindows -or -not $ConfirmDisposableInfrastructure){throw 'QR7 verification requires Windows and explicit disposable local-resource confirmation.'}
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
$id=[guid]::NewGuid().ToString('N');$run=Join-Path $root ".runstate/qr7-verification/$id";New-Item -ItemType Directory -Path $run|Out-Null;Protect-DraftDirectory $run
$checks=[ordered]@{};$passed=$false;$pilotId=$null;$candidate=$null
Push-Location $root
try{
 $before=& node scripts/release/source.mjs;if($LASTEXITCODE -ne 0){throw 'QR7 source inventory failed.'};$source=$before|ConvertFrom-Json
 & node --test --test-reporter=tap scripts/release/qr7.test.mjs *> (Join-Path $run 'plan.private.log');if($LASTEXITCODE -ne 0){throw 'QR7 plan guards failed.'}
 $count=& node scripts/release/results.mjs tap (Join-Path $run 'plan.private.log') 25;if($LASTEXITCODE -ne 0){throw 'QR7 plan guards incomplete.'};$checks.plan=@{passed=$true;count=[int]$count}
 & (Join-Path $PSScriptRoot 'test-qr-pilot-guards.ps1') *> (Join-Path $run 'tooling.private.log');if($LASTEXITCODE -ne 0){throw 'QR7 tooling guards failed.'};$checks.tooling=@{passed=$true;count=17}
 & dotnet test tests/Integration/NexaConnect.IntegrationTests --no-restore --filter FullyQualifiedName~QrPilotEdgeTests --verbosity quiet --logger 'trx;LogFileName=edge.trx' --results-directory $run *> (Join-Path $run 'edge.private.log')
 if($LASTEXITCODE -ne 0){throw 'QR7 edge/TLS boundaries failed.'};$count=Read-DraftTestCounts (Join-Path $run 'edge.trx') 18;$checks.edge=@{passed=$true;count=$count}
 & (Join-Path $PSScriptRoot 'test-first-draft-stack.ps1') -ConfirmDisposableInfrastructure -QrPilotOrigin 'https://qr7.example.test:8443' *> (Join-Path $run 'stack.private.log')
 if($LASTEXITCODE -ne 0){throw 'Disposable QR profile lifecycle failed; inspect private logs.'};$checks.stack=@{passed=$true;cleanupVerified=$true}
 $output=@(& (Join-Path $PSScriptRoot 'test-qr6.ps1') -ConfirmDisposableInfrastructure 2>&1|Tee-Object -FilePath (Join-Path $run 'qr6.private.log'))
 $paths=@($output|ForEach-Object {if($_ -is [string] -and $_ -match '^QR6 local test candidate prepared: (.+)/candidate\.json$'){$Matches[1]}})
 if($paths.Count -ne 1){throw 'QR7 requires one fresh successful QR6 candidate.'};$candidate=[IO.Path]::GetFullPath($paths[0])
 $pilotId=[guid]::NewGuid().ToString('N')
 & (Join-Path $PSScriptRoot 'qr-pilot.ps1') -Action Init -PilotId $pilotId -CandidateDirectory $candidate *> (Join-Path $run 'prepared.private.log')
 if($LASTEXITCODE -ne 0){throw 'QR7 prerequisite template preparation failed.'}
 $report=& node scripts/release/qr7.mjs report (Join-Path $root ".runstate/qr7/$pilotId");if($LASTEXITCODE -ne 0){throw 'QR7 prepared report invalid.'}
 $view=$report|ConvertFrom-Json
 if($view.configured -or $view.releaseReady -or $view.productionVerified -or @($view.prerequisites).Count -lt 7){throw 'Unconfirmed QR7 pilot must stay unconfigured with prerequisites pending.'}
 foreach($device in $view.cases.PSObject.Properties){if(@($device.Value.PSObject.Properties|Where-Object {$_.Value.status -ceq 'pending'}).Count -ne 12){throw 'Actual-phone observations must stay pending.'}}
 $checks.preparation=@{passed=$true;pendingDeviceCases=24}
 $after=& node scripts/release/source.mjs;if($LASTEXITCODE -ne 0 -or ($after -join "`n") -cne ($before -join "`n")){throw 'QR7 source changed during verification.'}
 $passed=$true
}finally{
 Pop-Location
 @{version=1;runId=$id;source=$source;passed=$passed;checks=$checks;pilotId=$pilotId;candidateDirectory=$candidate;lanActivation='pending-prerequisites';actualDeviceAcceptance='pending';productionVerified=$false}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $run 'verification.json')
}
Write-Output "QR7 setup verification passed: $run/verification.json"
Write-Output "Prepared pilot $pilotId; LAN/phone/cashier/PDF acceptance remains pending."
