#requires -Version 7.0
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runner=Join-Path $PSScriptRoot 'test-refund-hosted.ps1'
foreach($arguments in @(@(),@('-ConfirmProcessTermination'),@('-ConfirmProcessTermination','-ConfirmDisposableInfrastructure','-ConfirmOmiseTest'))){
    $rejected=$false
    # Do not inherit real external test credentials into a negative guard test.
    $secret=[Environment]::GetEnvironmentVariable('NEXACONNECT_REFUND_OMISE_SECRET')
    try{[Environment]::SetEnvironmentVariable('NEXACONNECT_REFUND_OMISE_SECRET',$null);try{& $runner @arguments | Out-Null}catch{$rejected=$true}}
    finally{[Environment]::SetEnvironmentVariable('NEXACONNECT_REFUND_OMISE_SECRET',$secret)}
    if(-not $rejected){throw 'Launcher guard failed.'}
}
$assembly=Join-Path $root 'src/Tools/NexaConnect.RefundAcceptance/bin/Debug/net10.0/NexaConnect.RefundAcceptance.dll'
if(-not(Test-Path -LiteralPath $assembly)){throw 'Build the acceptance tool before running guard tests.'}
$run='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$defaults=@{ENABLED='1';CONFIRM_DISPOSABLE='1';RUN_ID=$run;READER_SUBJECT='11111111-1111-1111-1111-111111111111';MANAGER_SUBJECT='22222222-2222-2222-2222-222222222222';STATE_PATH=(Join-Path $root ".runstate/refund-hosted/$run/fixture.json");ADAPTER='GenericHttp';AUTHORITY="http://127.0.0.1:12345/realms/nexa-review-it-$run";PAYMENT_URL='http://127.0.0.1:12346/';REPORTING_URL='http://127.0.0.1:12347/';PROVIDER_URL='https://127.0.0.1:12348/';BROKER='amqp://127.0.0.1:12349/'}
foreach($suffix in @('platform','restaurant','authorization','order','payment','reporting')){$defaults['DB_'+$suffix.ToUpperInvariant()]="Host=127.0.0.1;Database=nexa_review_it_${run}_$suffix;Username=guard-fixture;Password=synthetic"}
foreach($name in @('PLATFORMDIRECTORY','AUTHORIZATION','RESTAURANT','ORDER')){$defaults['SERVICE_'+$name]='http://127.0.0.1:12350/'}
$changes=@(@{ENABLED='0'},@{DB_PAYMENT="Host=remote.invalid;Database=nexa_review_it_${run}_payment"},@{AUTHORITY='http://remote.invalid/realms/unsafe'},@{STATE_PATH=(Join-Path $root '.runstate/unrelated/fixture.json')},@{MANAGER_SUBJECT=$defaults.READER_SUBJECT})
foreach($change in $changes){
    $info=[Diagnostics.ProcessStartInfo]::new('dotnet');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.ArgumentList.Add($assembly);$info.ArgumentList.Add('run')
    foreach($name in @($info.Environment.Keys)){if($name-like'NEXACONNECT_*'){$info.Environment.Remove($name)|Out-Null}}
    foreach($entry in $defaults.GetEnumerator()){$info.Environment['NEXACONNECT_REFUND_'+$entry.Key]=[string]$entry.Value}
    foreach($entry in $change.GetEnumerator()){$info.Environment['NEXACONNECT_REFUND_'+$entry.Key]=[string]$entry.Value}
    $process=[Diagnostics.Process]::Start($info);$out=$process.StandardOutput.ReadToEndAsync();$err=$process.StandardError.ReadToEndAsync()
    try{if(-not $process.WaitForExit(10000)){$process.Kill($true);throw 'Guard timeout.'};$text=$err.GetAwaiter().GetResult();if($process.ExitCode-ne 1-or$text-notmatch 'failed at guard'-or$out.GetAwaiter().GetResult().Length){throw 'Tool guard did not fail before external access.'}}finally{$process.Dispose()}
}
Write-Output 'Refund acceptance guards passed: 3 launcher and 5 tool boundaries.'
