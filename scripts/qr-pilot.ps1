#requires -Version 7.4
[CmdletBinding()]
param(
 [ValidateSet('Init','Status','Certificates','Configure','Start','Stop','Record','Report')][string]$Action='Status',
 [string]$PilotId,[string]$CandidateDirectory,
 [ValidateSet('ios-safari','android-chrome')][string]$Device,[string]$CaseId,
 [ValidateSet('passed','failed','blocked')][string]$Result,[string]$EvidenceId,[switch]$ConfirmObserved
)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'QR7 local PC tooling requires Windows and current-user protection.'}
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
if(-not $PilotId -and $Action -eq 'Init'){$PilotId=[guid]::NewGuid().ToString('N')}
Assert-DraftRunId $PilotId
$run=Join-Path $root ".runstate/qr7/$PilotId";$tool=Join-Path $PSScriptRoot 'release/qr7.mjs'
function Assert-PilotPath([string]$Path){
 $current=[IO.Path]::GetFullPath($Path)
 while($current){
  if(Test-Path -LiteralPath $current){if((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'QR pilot paths cannot traverse links.'}}
  $parent=[IO.Directory]::GetParent($current);$current=if($parent){$parent.FullName}else{$null}
 }
}
Assert-PilotPath $run
if($Action -eq 'Init'){
 if(Test-Path -LiteralPath $run){throw 'Use a fresh PilotId; existing pilot records are immutable.'}
 if(-not $CandidateDirectory){throw 'Supply a fresh QR6 candidate directory from completed verification.'}
 New-Item -ItemType Directory -Path $run|Out-Null;Protect-DraftDirectory $run
 & node $tool init $run ([IO.Path]::GetFullPath($CandidateDirectory));if($LASTEXITCODE -ne 0){throw 'QR7 initialization failed.'}
 Write-Output "QR7 prerequisites prepared. PilotId=$PilotId Plan=$run/plan.json";return
}
if(-not (Test-Path -LiteralPath $run)){throw 'Pilot record does not exist.'}
$lock=Open-DraftLifecycleLock $run
try{
 $plan=Get-Content -LiteralPath (Join-Path $run 'plan.json') -Raw|ConvertFrom-Json -AsHashtable
 if($plan.id -cne $PilotId){throw 'Pilot identity mismatch.'}
 if($Action -in @('Status','Report')){& node $tool report $run;if($LASTEXITCODE -ne 0){throw 'QR7 report binding invalid.'};return}
 if($Action -eq 'Record'){
  & node $tool record $run $Device $CaseId $Result $EvidenceId $(if($ConfirmObserved){'observed'}else{'unconfirmed'})
  if($LASTEXITCODE -ne 0){throw 'QR7 observation rejected. Confirm actual observation and original configured binding.'};return
 }
 $processFile=Join-Path $run 'edge-process.json';$edge=Join-Path $run 'edge';$assembly=Join-Path $edge 'NexaConnect.QrPilotEdge.dll'
 function Stop-Edge {
  if(-not (Test-Path -LiteralPath $processFile)){return}
  $record=Get-Content -LiteralPath $processFile -Raw|ConvertFrom-Json -AsHashtable
  $process=Get-Process -Id $record.id -ErrorAction SilentlyContinue
  if(Assert-DraftProcess $record $process){
   $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.id)"
   if($actual.ExecutablePath -cne (Get-Command dotnet).Source -or $actual.CommandLine -notlike "*`"$assembly`"*" -or $actual.CommandLine -notlike "*`"$edge`"*"){throw 'Edge process identity changed; no process was stopped.'}
   $process.Kill();if(-not $process.WaitForExit(10000)){throw 'Owned pilot edge did not stop.'}
  }
  Remove-Item -LiteralPath $processFile
 }
 if($Action -eq 'Stop'){Stop-Edge;Write-Output 'Owned QR edge stopped; stack/order recovery and data remain intact.';return}
 if(Test-Path -LiteralPath $processFile){throw 'Stop the owned edge before certificate/configuration changes or another Start.'}
 Assert-DraftQrPilotOrigin $plan.publicOrigin
 if($plan.listenAddress -notmatch '^(10\.\d+\.\d+\.\d+|172\.(1[6-9]|2\d|3[01])\.\d+\.\d+|192\.168\.\d+\.\d+)$'){throw 'Configure the PC private IPv4 address in its plan.'}
 if($Action -eq 'Certificates'){
  Protect-DraftDirectory $run
  if(Test-Path -LiteralPath (Join-Path $run 'activation.json')){throw 'Configured certificate identity cannot be replaced. Preserve the pilot and prepare a fresh candidate.'}
  foreach($name in @('certificate.pem','private-key.pem','client-ca.cer')){if(Test-Path -LiteralPath (Join-Path $run $name)){throw 'Certificate files already exist; no private material was overwritten.'}}
  $rootKey=[Security.Cryptography.RSA]::Create(3072);$leafKey=[Security.Cryptography.RSA]::Create(3072)
  try{
   $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=NexaConnect QR7 $PilotId",$rootKey,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
   $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true,$false,0,$true))
   $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign,$true))
   $ca=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddDays(30))
   try{
    $leafRequest=[Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=NexaConnect QR7 leaf",$leafKey,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $leafRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
    $oid=[Security.Cryptography.OidCollection]::new();[void]$oid.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
    $leafRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oid,$true))
    $san=[Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new();$hostName=([uri]$plan.publicOrigin).DnsSafeHost;$ip=$null
    if([Net.IPAddress]::TryParse($hostName,[ref]$ip)){$san.AddIpAddress($ip)}else{$san.AddDnsName($hostName)}
    $leafRequest.CertificateExtensions.Add($san.Build())
    $signed=$leafRequest.Create($ca,[DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddDays(14),[Security.Cryptography.RandomNumberGenerator]::GetBytes(16))
    try{
     [IO.File]::WriteAllText((Join-Path $run 'certificate.pem'),$signed.ExportCertificatePem())
     [IO.File]::WriteAllText((Join-Path $run 'private-key.pem'),$leafKey.ExportPkcs8PrivateKeyPem())
     [IO.File]::WriteAllText((Join-Path $run 'client-ca.cer'),$ca.ExportCertificatePem())
     Write-Output ('Public CA SHA256: '+[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($ca.RawData)))
    }finally{$signed.Dispose()}
   }finally{$ca.Dispose()}
  }finally{$rootKey.Dispose();$leafKey.Dispose()}
  Write-Output 'Local test certificates prepared. No trust store, firewall, DNS or listener changed. The CA signing key was discarded.';return
 }
 $reportJson=& node $tool report $run;if($LASTEXITCODE -ne 0){throw 'QR7 binding invalid.'};$report=$reportJson|ConvertFrom-Json
 if(@($report.prerequisites).Count){throw 'QR7 prerequisites remain pending; inspect Status. No listener was opened.'}
 if(@(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop|Where-Object IPAddress -ceq $plan.listenAddress).Count -ne 1){throw 'The configured private listener address does not belong to this PC.'}
 $resolved=@([Net.Dns]::GetHostAddresses(([uri]$plan.publicOrigin).Host))
 if(-not $resolved.Count -or @($resolved|Where-Object {$_.ToString() -cne $plan.listenAddress}).Count){throw 'Pilot DNS must resolve exclusively to this PC private IPv4.'}
 $stack=Get-DraftRunDirectory $root $plan.stackRunId
 $state=Get-Content -LiteralPath (Join-Path $stack 'state.json') -Raw|ConvertFrom-Json -AsHashtable
 if($state.runId -cne $plan.stackRunId -or $state.project -cne "nexa-draft-$($plan.stackRunId)" -or $state.status -ne 'Ready' -or -not $state.provisionedBranch -or -not $state.Contains('qrPilotOrigin') -or $state.qrPilotOrigin -cne $plan.publicOrigin){throw 'Use only the planned fresh Ready, provisioned QR profile; retained drafts cannot be adopted.'}
 Assert-DraftUrls $state.urls
 $expectedNames=@((Get-DraftServiceCatalog $root -IncludeMedia:($state.Contains('mediaEnabled') -and $state.mediaEnabled)).Keys)+@('Bff')
 if($state.processes.Count -ne $expectedNames.Count -or $state.urls.Count -ne $expectedNames.Count){throw 'Planned stack inventory is incomplete.'}
 foreach($name in $expectedNames){
  if(-not $state.processes.Contains($name) -or -not $state.urls.Contains($name)){throw 'Planned stack service is missing.'}
  $record=$state.processes[$name];if(-not (Assert-DraftProcess $record (Get-Process -Id $record.id -ErrorAction SilentlyContinue))){throw 'A planned stack service is not running.'}
  $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.id)";$expectedAssembly=Join-Path $stack "apps/$name/$($record.assemblyName).dll"
  if($record.assemblyName -cne $(if($name -eq 'Bff'){'NexaConnect.CustomerBff'}else{"NexaConnect.Services.$name"}) -or $actual.ExecutablePath -cne (Get-Command dotnet).Source -or $actual.CommandLine -notlike "*`"$expectedAssembly`"*" -or -not @(Get-NetTCPConnection -LocalPort ([uri]$state.urls[$name]).Port -State Listen -ErrorAction SilentlyContinue|Where-Object OwningProcess -eq $record.id).Count){throw 'Planned stack listener/executable ownership changed.'}
 }
 $current=& node (Join-Path $PSScriptRoot 'release/source.mjs');if($LASTEXITCODE -ne 0 -or -not $state.Contains('qrPilotSource') -or ($current|ConvertFrom-Json|ConvertTo-Json -Compress) -cne ($state.qrPilotSource|ConvertTo-Json -Compress)){throw 'QR stack source differs from the current verified source.'}
 $backendArtifacts=Get-DraftQrPilotArtifactHash $stack
 if(-not $state.Contains('qrPilotArtifacts') -or $state.qrPilotArtifacts -cne $backendArtifacts){throw 'QR backend publication inventory changed.'}
 function Files {
  $map=[ordered]@{};foreach($name in @('certificate.pem','private-key.pem','client-ca.cer','edge/appsettings.json')){$map[$name]=(Get-FileHash -LiteralPath (Join-Path $run $name) -Algorithm SHA256).Hash.ToLowerInvariant()}
  foreach($file in @(Get-ChildItem -LiteralPath $edge -Recurse -File|Where-Object {$_.Extension -ne '.pdb' -and $_.Name -ne 'appsettings.json'}|Sort-Object FullName)){Assert-PilotPath $file.FullName;$relative=[IO.Path]::GetRelativePath($run,$file.FullName).Replace('\','/');$map[$relative]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
  return $map
 }
 if($Action -eq 'Configure'){
  Protect-DraftDirectory $run;Assert-PilotPath $edge
  if(Test-Path -LiteralPath $edge){foreach($entry in @(Get-ChildItem -LiteralPath $edge -Recurse -Force)){Assert-PilotPath $entry.FullName}}
  if(Test-Path -LiteralPath (Join-Path $run 'activation.json')){throw 'Activation binding is write-once; do not overwrite a configured pilot.'}
  $secrets=Read-DraftSecrets (Join-Path $stack 'secrets.clixml')
  $backendCertificate=[Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12FromFile((Join-Path $stack 'local.pfx'),$secrets.CERT_PASSWORD)
  try{$backendPin=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($backendCertificate.RawData))}finally{$backendCertificate.Dispose()}
  $certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile((Join-Path $run 'certificate.pem'),(Join-Path $run 'private-key.pem'))
  try{
   if(-not $certificate.MatchesHostname(([uri]$plan.publicOrigin).Host,$false,$false) -or $certificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or $certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow.AddDays(1)){throw 'Pilot certificate scope or lifetime is invalid.'}
   $ca=[Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadCertificateFromFile((Join-Path $run 'client-ca.cer'))
   $chain=[Security.Cryptography.X509Certificates.X509Chain]::new()
   try{
    $chain.ChainPolicy.TrustMode=[Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
    $chain.ChainPolicy.CustomTrustStore.Add($ca);$chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    $chain.ChainPolicy.ApplicationPolicy.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
    if(-not $chain.Build($certificate)){throw 'Pilot leaf does not chain to its distributed test CA for TLS server authentication.'}
   }finally{$chain.Dispose();$ca.Dispose()}
  }finally{$certificate.Dispose()}
  & dotnet publish (Join-Path $root 'src/Tools/NexaConnect.QrPilotEdge') -c Release --output $edge --verbosity quiet
  if($LASTEXITCODE -ne 0){throw 'Pilot edge publication failed.'}
  $backendOrigin='https://localhost:'+([uri]$state.urls.Bff).Port
  @{QrPilot=@{PublicOrigin=$plan.publicOrigin;ListenAddress=$plan.listenAddress;BackendOrigin=$backendOrigin;BackendCertificateSha256=$backendPin;CertificatePath=(Join-Path $run 'certificate.pem');PrivateKeyPath=(Join-Path $run 'private-key.pem')}}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $edge 'appsettings.json')
  $pos=Get-Content -LiteralPath (Join-Path $stack 'pos-configuration.json') -Raw|ConvertFrom-Json
  $scope=[ordered]@{};foreach($field in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){$scope[$field]=$pos.Pos.$field}
  @{stackRunId=$plan.stackRunId;scope=$scope;backendOrigin=$backendOrigin;backendCertificateSha256=$backendPin;backendArtifacts=$backendArtifacts;files=(Files)}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'activation-input.json')
  & node $tool activate $run (Join-Path $run 'activation-input.json');if($LASTEXITCODE -ne 0){throw 'Pilot activation binding failed.'};return
 }
 if($Action -eq 'Start'){
  $activation=Get-Content -LiteralPath (Join-Path $run 'activation.json') -Raw|ConvertFrom-Json -AsHashtable
  if($activation.backendArtifacts -cne $backendArtifacts){throw 'Configured QR backend artifacts changed.'}
  $actualFiles=Files
  if($activation.files.Count -ne $actualFiles.Count -or @($actualFiles.Keys|Where-Object {$activation.files[$_] -cne $actualFiles[$_]}).Count){throw 'Pilot certificate/configuration/executables changed.'}
  $port=([uri]$plan.publicOrigin).Port
  if(@(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue).Count){throw 'Pilot port is occupied; no listener was adopted.'}
  $old=@{}
  try{
   foreach($key in @([Environment]::GetEnvironmentVariables('Process').Keys)){if($key -match '__|^NEXACONNECT_|^NEXA_|^ASPNETCORE_|^DOTNET_ENVIRONMENT$|^OTEL_'){$old[$key]=[Environment]::GetEnvironmentVariable($key);Remove-Item -LiteralPath ("Env:"+$key) -ErrorAction SilentlyContinue}}
   $process=Start-Process (Get-Command dotnet).Source -ArgumentList @("`"$assembly`"",'--contentRoot',"`"$edge`"") -Environment @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development'} -WindowStyle Hidden -WorkingDirectory $edge -PassThru -RedirectStandardOutput (Join-Path $run 'edge.private.log') -RedirectStandardError (Join-Path $run 'edge-error.private.log')
   @{id=$process.Id;startTicks=$process.StartTime.ToUniversalTime().Ticks}|ConvertTo-Json|Set-Content -LiteralPath $processFile
  }finally{Restore-DraftEnvironment $old}
  try{
   $ready=$false;for($i=0;$i -lt 20;$i++){try{if((Invoke-WebRequest ($plan.publicOrigin+'/order') -TimeoutSec 3).StatusCode -eq 200){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 500}
   if(-not $ready -or -not @(Get-NetTCPConnection -LocalAddress $plan.listenAddress -LocalPort $port -State Listen -ErrorAction SilentlyContinue|Where-Object OwningProcess -eq $process.Id).Count){throw 'Pilot listener/trusted HTTPS could not be verified.'}
  }catch{Stop-Edge;throw 'QR edge startup failed; private logs retained and owned listener stopped.'}
  Write-Output 'Owned guest-only HTTPS listener ready. Actual phone/POS/PDF acceptance remains pending.';return
 }
}finally{$lock.Dispose()}
