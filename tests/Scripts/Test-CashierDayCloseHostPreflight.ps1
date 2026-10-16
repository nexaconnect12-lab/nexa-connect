#requires -Version 7.0
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$fixture=Join-Path $root 'src/Tools/NexaConnect.FinancialPortalAcceptance/bin/Debug/net10.0/NexaConnect.FinancialPortalAcceptance.dll'
$runId=[Guid]::NewGuid().ToString('N');$run=Join-Path $root ".runstate/cashier-day-close/$runId"
New-Item -ItemType Directory -Path $run -Force|Out-Null
$clock=Join-Path $run 'clock.json'
@{runId=$runId;mode='historical';atUtc=[DateTimeOffset]::UtcNow.AddDays(-1).ToString('O')}|ConvertTo-Json|Set-Content -LiteralPath $clock
$settings=@{Acceptance__RunId=$runId;Acceptance__ConfirmDisposable='1';Acceptance__ClockPath=$clock;Acceptance__Port='19001';Authentication__Authority="http://127.0.0.1:19002/realms/nexa-review-it-$runId";ConnectionStrings__POS="Host=127.0.0.1;Port=19003;Database=nexa_review_it_${runId}_pos;Username=unused;Password=unused"}
$cases=@(@{Acceptance__ConfirmDisposable='0'},@{Acceptance__RunId='invalid'},@{Acceptance__ClockPath=(Join-Path $root 'clock.json')},@{ConnectionStrings__POS='Host=example.org;Database=production'},@{ConnectionStrings__POS='Host=127.0.0.1;Database=production'},@{Authentication__Authority='https://example.org/realms/production'},@{Acceptance__Port='80'},@{ClockMode='future'})
try{
 foreach($case in $cases){
  $info=[Diagnostics.ProcessStartInfo]::new('dotnet');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
  $info.ArgumentList.Add($fixture);$info.ArgumentList.Add('host-pos')
  foreach($key in @($info.Environment.Keys)){if($key-match '^(Acceptance|ConnectionStrings|Authentication)__'){$info.Environment.Remove($key)|Out-Null}}
  foreach($e in $settings.GetEnumerator()){$info.Environment[$e.Key]=[string]$e.Value}
  foreach($e in $case.GetEnumerator()){if($e.Key-ne'ClockMode'){$info.Environment[$e.Key]=[string]$e.Value}}
  if($case.ContainsKey('ClockMode')){@{runId=$runId;mode='historical';atUtc=[DateTimeOffset]::UtcNow.AddDays(1).ToString('O')}|ConvertTo-Json|Set-Content -LiteralPath $clock}
  $child=[Diagnostics.Process]::Start($info);$out=$child.StandardOutput.ReadToEndAsync();$errorText=$child.StandardError.ReadToEndAsync()
  try{if(-not$child.WaitForExit(15000)){throw 'Guard unexpectedly attempted a host startup.'};if($child.ExitCode-ne1-or $errorText.GetAwaiter().GetResult()-notmatch 'failed at command-host'){throw 'Preflight rejection missing.'};if($out.GetAwaiter().GetResult()){throw 'Unexpected preflight stdout.'}}finally{if(-not$child.HasExited){$child.Kill($true);$child.WaitForExit(10000)|Out-Null};$child.Dispose()}
 }
 Write-Output 'Passed all eight acceptance-host preflight rejection cases without infrastructure access.'
}finally{
 $resolved=[IO.Path]::GetFullPath($run);$expected=[IO.Path]::GetFullPath((Join-Path $root ".runstate/cashier-day-close/$runId"))
 if($resolved-ne$expected-or $runId-notmatch '^[a-f0-9]{32}$'){throw 'Refusing unverified preflight directory cleanup.'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
