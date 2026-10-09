#requires -Version 7.0
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$template=Get-Content (Join-Path $root 'docker/keycloak/realm/nexa-dev-realm.json') -Raw
$template=[regex]::Replace($template,'(:\s*)\$\{[^}]+\}','$1false')
$directory=Join-Path $root ('.runstate/identity-preflight/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory -Force|Out-Null
$path=Join-Path $directory 'realm.json'
try{
 foreach($mode in @('missing','wrong-audience','disabled-access')){
  $realm=$template|ConvertFrom-Json;$client=@($realm.clients|Where-Object {$_.clientId -eq 'nexaconnect-inventory-service'})[0]
  if($mode-eq'missing'){$client.protocolMappers=@()}elseif($mode-eq'wrong-audience'){$client.protocolMappers[0].config.'included.custom.audience'='other-api'}else{$client.protocolMappers[0].config.'access.token.claim'='false'}
  $realm|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $path
  $info=[Diagnostics.ProcessStartInfo]::new('pwsh');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
  foreach($argument in @('-NoProfile','-File',(Join-Path $root 'scripts/test-keycloak-realm.ps1'),'-RealmFile',$path)){$info.ArgumentList.Add($argument)}
  $child=[Diagnostics.Process]::Start($info);$out=$child.StandardOutput.ReadToEndAsync();$errorText=$child.StandardError.ReadToEndAsync()
  try{if(-not$child.WaitForExit(15000)){throw 'Audience guard timeout.'};if($child.ExitCode-eq0-or $errorText.GetAwaiter().GetResult()-notmatch 'Inventory workload client must emit'){throw 'Audience defect was not rejected.'};$out.GetAwaiter().GetResult()|Out-Null}finally{if(-not$child.HasExited){$child.Kill($true);$child.WaitForExit(10000)|Out-Null};$child.Dispose()}
 }
 Write-Output 'Passed three Inventory audience rejection cases.'
}finally{Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue;Remove-Item -LiteralPath $directory -Force}
