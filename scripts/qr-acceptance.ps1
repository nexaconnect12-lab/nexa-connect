#requires -Version 7.4
[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidateSet('Diagnose','Prepare','Record','Report')][string]$Action,
 [Parameter(Mandatory)][string]$PilotId,
 [string]$ObservationFile,
 [switch]$ConfirmObserved
)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Use the current Windows pilot PC.'}
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
Assert-DraftRunId $PilotId
$root=Split-Path $PSScriptRoot -Parent
$run=Join-Path $root ".runstate/qr7/$PilotId"
function Assert-Qr9Path([string]$Path){
 for($cursor=[IO.Path]::GetFullPath($Path);$cursor;$cursor=[IO.Path]::GetDirectoryName($cursor)){
  if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'QR9 paths cannot traverse links or junctions.'}
 }
}
Assert-Qr9Path $run
if(-not(Test-Path -LiteralPath $run -PathType Container)){throw 'Specify an existing QR7 pilot.'}
$lock=Open-DraftLifecycleLock $run
try{
 $tool=Join-Path $PSScriptRoot 'release/qr9.mjs'
 if($Action -eq 'Diagnose'){
  # QR7 report validates immutable plan/file bindings even when its candidate is stale.
  $statusJson=& node (Join-Path $PSScriptRoot 'release/qr7.mjs') report $run
  if($LASTEXITCODE -ne 0){throw 'Pilot binding is invalid; no network diagnostic attempted.'}
  $status=$statusJson|ConvertFrom-Json
  if(-not $status.configured){throw 'Configure the pilot before running network diagnostics.'}
  $plan=Get-Content -LiteralPath (Join-Path $run 'plan.json') -Raw|ConvertFrom-Json
  $activation=Get-Content -LiteralPath (Join-Path $run 'activation.json') -Raw|ConvertFrom-Json
  Assert-DraftQrPilotOrigin $plan.publicOrigin
  $origin=[uri]$plan.publicOrigin
  $addresses=@([Net.Dns]::GetHostAddresses($origin.DnsSafeHost))
  if(-not $addresses.Count -or @($addresses|Where-Object {$_.ToString() -cne $plan.listenAddress}).Count){throw 'Pilot origin no longer resolves exclusively to its configured private address.'}
  $leaf=[Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem((Get-Content -LiteralPath (Join-Path $run 'certificate.pem') -Raw))
  $ca=[Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem((Get-Content -LiteralPath (Join-Path $run 'client-ca.cer') -Raw))
  $chain=[Security.Cryptography.X509Certificates.X509Chain]::new()
  try{
   $chain.ChainPolicy.TrustMode=[Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
   [void]$chain.ChainPolicy.CustomTrustStore.Add($ca)
   $chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
   $chain.ChainPolicy.DisableCertificateDownloads=$true
   [void]$chain.ChainPolicy.ApplicationPolicy.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
   $validChain=$chain.Build($leaf)
   $expectedPin=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($leaf.RawData))
   $trustedTls=$false;$liveMatches=$false;$protocol=$null
   $tcp=[Net.Sockets.TcpClient]::new();$tls=$null;$deadline=[Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(10))
   try{
    [void]$tcp.ConnectAsync($origin.DnsSafeHost,$origin.Port,$deadline.Token).AsTask().GetAwaiter().GetResult()
    # Default OS chain/name validation: no accepting callback or TLS bypass.
    $tls=[Net.Security.SslStream]::new($tcp.GetStream(),$false)
    $options=[Net.Security.SslClientAuthenticationOptions]::new();$options.TargetHost=$origin.DnsSafeHost
    [void]$tls.AuthenticateAsClientAsync($options,$deadline.Token).GetAwaiter().GetResult()
    $trustedTls=$true;$protocol=$tls.SslProtocol.ToString()
    $liveMatches=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($tls.RemoteCertificate.GetRawCertData())) -ceq $expectedPin
   }catch{# Only bounded categories are reported; exception messages may contain network/private details.
   }finally{if($tls){$tls.Dispose()};$tcp.Dispose();$deadline.Dispose()}
   $pageStatus=$null;$staffStatus=$null
   if($trustedTls -and $liveMatches){
    try{$pageStatus=(Invoke-WebRequest ($plan.publicOrigin+'/order') -MaximumRedirection 0 -TimeoutSec 10 -SkipHttpErrorCheck).StatusCode}catch{}
    try{$staffStatus=(Invoke-WebRequest ($plan.publicOrigin+'/bff/customer/me') -MaximumRedirection 0 -TimeoutSec 10 -SkipHttpErrorCheck).StatusCode}catch{}
   }
   $diagnostic=[ordered]@{version=1;pilotId=$PilotId;candidateBinding=$plan.candidateBinding;activationBinding=$activation.binding;observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');origin=$plan.publicOrigin;
    candidatePrerequisites=@($status.prerequisites);rootSha256=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($ca.RawData));
    leafSha256=$expectedPin;leafNotBeforeUtc=$leaf.NotBefore.ToUniversalTime().ToString('O');leafNotAfterUtc=$leaf.NotAfter.ToUniversalTime().ToString('O');
    exactSanMatches=$leaf.MatchesHostname($origin.DnsSafeHost,$false,$false);serverAuthChainValid=$validChain;windowsTrustedTls=$trustedTls;
    liveLeafMatches=$liveMatches;tlsProtocol=$protocol;guestPageStatus=$pageStatus;staffDeniedStatus=$staffStatus;
    localChecksPassed=($validChain -and $leaf.MatchesHostname($origin.DnsSafeHost,$false,$false) -and $trustedTls -and $liveMatches -and $pageStatus -eq 200 -and $staffStatus -eq 404);
    deviceTrust='not-observed';releaseReady=$false;productionVerified=$false}
   $directory=Join-Path $run 'diagnostics';Assert-Qr9Path $directory;New-Item -ItemType Directory -Path $directory -Force|Out-Null;Protect-DraftDirectory $directory
   $destination=Join-Path $directory ("qr9-"+[guid]::NewGuid().ToString('N')+'.json')
   $diagnostic|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $destination
   $diagnostic|ConvertTo-Json -Depth 4
  }finally{$leaf.Dispose();$ca.Dispose();$chain.Dispose()}
  return
 }
 if($Action -eq 'Record'){
  if(-not $ConfirmObserved){throw 'Record only the checks you actually observed; supply ConfirmObserved.'}
  if(-not $ObservationFile){throw 'Specify a completed QR9 observation template.'}
  & node $tool record $run ([IO.Path]::GetFullPath($ObservationFile)) observed
 }elseif($Action -eq 'Prepare'){& node $tool prepare $run}
 else{& node $tool report $run}
 if($LASTEXITCODE -ne 0){throw 'QR9 acceptance validation failed; inspect prerequisites and the bounded checklist.'}
 if($Action -eq 'Prepare'){Protect-DraftDirectory (Join-Path $run 'qr9')}
}finally{$lock.Dispose()}
