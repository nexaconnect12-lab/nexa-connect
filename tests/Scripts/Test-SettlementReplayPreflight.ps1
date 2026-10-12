#requires -Version 7.0
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskDll=Join-Path $taskRoot 'src/Tools/NexaConnect.CashCloseReplay/bin/Debug/net10.0/NexaConnect.CashCloseReplay.dll'
if(!(Test-Path -LiteralPath $taskDll)){throw 'Build the replay executable before preflight.'}
$taskPrior=@{};foreach($name in @('NEXACONNECT_SETTLEMENT_REPLAY_DB','NEXACONNECT_SETTLEMENT_REPLAY_BROKER')){
 $taskPrior[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$null)
}
try{
 $taskHelp=& dotnet $taskDll --settlements --help 2>&1
 if($LASTEXITCODE-ne0 -or ($taskHelp-join' ') -notmatch 'Settlement replay:'){throw 'Settlement replay help failed without infrastructure.'}
 $taskId='11111111-1111-4111-8111-111111111111'
 $taskBase=@('--organization',$taskId,'--restaurant',$taskId,'--branch',$taskId,'--from-date','2026-09-01','--to-date','2026-09-02','--limit','1')
 $taskCases=@(
  @('--unknown','private-marker'),
  @('--execute','--execute'),
  @('--organization',$taskId),
  @('--execute','--operator','invalid','--reason','retry','--manifest',('A'*64)),
  @('--execute','--operator',$taskId,'--reason','private-marker','--manifest',('A'*64)),
  @('--execute','--operator',$taskId,'--reason','retry','--manifest','bad'),
  @('--operator',$taskId),
  @('--reason','retry'),
  @('--manifest',('A'*64)),
  @('--help','--execute')
 )
 foreach($case in $taskCases){
  $taskArgs=@('--settlements')+$taskBase+$case
  $taskOutput=& dotnet $taskDll @taskArgs 2>&1
  if($LASTEXITCODE-ne1 -or ($taskOutput-join' ')-match 'private-marker|grossSales|Password='){throw 'Malformed replay arguments escaped the safe preflight boundary.'}
 }
 foreach($replacement in @('2026-02-30','2099-01-01')){
  $taskArgs=@('--settlements')+$taskBase.Clone();$taskArgs[10]=$replacement
  $taskOutput=& dotnet $taskDll @taskArgs 2>&1
  if($LASTEXITCODE-ne1){throw 'Invalid settlement date accepted.'}
 }
 $global:LASTEXITCODE=0
 Write-Output 'Passed settlement help and twelve malformed execution/date cases without database or broker credentials.'
}finally{foreach($name in $taskPrior.Keys){[Environment]::SetEnvironmentVariable($name,$taskPrior[$name])}}
