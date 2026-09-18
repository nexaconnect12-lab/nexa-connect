#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $ConfirmDisposableInfrastructure,
    [switch] $ConfirmProcessTermination,
    [switch] $SmokeTestOnly,
    [string] $DockerExecutable = 'docker'
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Local provider acceptance requires Windows.' }
if (-not $SmokeTestOnly -and (-not $ConfirmDisposableInfrastructure -or -not $ConfirmProcessTermination)) {
    throw 'Pass -ConfirmDisposableInfrastructure and -ConfirmProcessTermination for the hosted recovery matrix.'
}
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runRoot = Join-Path $root ('.runstate/order-provider-recovery-local/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$names = @('NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL','NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY',
    'NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT','NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY',
    'NEXACONNECT_PAYMENT_PROVIDER_AUTHORIZATION_PATH','NEXACONNECT_PAYMENT_PROVIDER_AUTHORIZATION_STATUS_PATH',
    'NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_PATH','NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_STATUS_PATH',
    'NEXACONNECT_PAYMENT_PROVIDER_VOID_PATH','NEXACONNECT_PAYMENT_PROVIDER_VOID_STATUS_PATH')
$saved = @{}
$names += 'NEXACONNECT_PAYMENT_PROVIDER_SIMULATOR_CERT_SHA256'
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$process = $null; $certificate = $null; $rsa = $null; $client = $null; $passed = $false
$pfxPath = Join-Path $runRoot 'simulator.pfx'
if(-not ('NexaSimulatorPinnedClient' -as [type])){
    Add-Type @'
using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
public static class NexaSimulatorPinnedClient {
 public static HttpClient Create(string pin) {
  var handler = new HttpClientHandler();
  handler.ServerCertificateCustomValidationCallback = (request,cert,chain,errors) =>
   request.RequestUri.Host == "127.0.0.1" && cert != null &&
   DateTime.UtcNow >= cert.NotBefore.ToUniversalTime() && DateTime.UtcNow <= cert.NotAfter.ToUniversalTime() &&
   (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) == 0 &&
   Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)) == pin;
  return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
 }
}
'@
}
function Invoke-Simulator([string]$Url,[string]$Method='GET',$Headers=@{},[string]$Body,
    [string]$ContentType,[int]$TimeoutSec,[switch]$SkipHttpErrorCheck){
    $message=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method),$Url)
    try{
        foreach($entry in $Headers.GetEnumerator()){$message.Headers.TryAddWithoutValidation($entry.Key,[string]$entry.Value)|Out-Null}
        if($Body){$message.Content=[Net.Http.StringContent]::new($Body,[Text.Encoding]::UTF8,'application/json')}
        $response=$client.SendAsync($message).GetAwaiter().GetResult()
        try{
            if($SkipHttpErrorCheck){return @{StatusCode=[int]$response.StatusCode}}
            $response.EnsureSuccessStatusCode()|Out-Null
            $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if($text){return $text|ConvertFrom-Json}
        }finally{$response.Dispose()}
    }finally{$message.Dispose()}
}
try {
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=NexaConnect disposable provider simulator ' + [Guid]::NewGuid().ToString('N'), $rsa,
        [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $san.AddDnsName('localhost'); $san.AddIpAddress([Net.IPAddress]::Loopback)
    $request.CertificateExtensions.Add($san.Build())
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddHours(2))
    $password = [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllBytes($pfxPath,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password))
    $pin=$certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
    $client=[NexaSimulatorPinnedClient]::Create($pin)
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
    $baseUrl = "https://127.0.0.1:$port/"
    $apiKey = [Guid]::NewGuid().ToString('N')
    $output = Join-Path $runRoot 'bin/'
    $project=Join-Path $root 'src/Tools/NexaConnect.PaymentProviderSimulator/NexaConnect.PaymentProviderSimulator.csproj'
    & dotnet restore $project --configfile (Join-Path (Split-Path $project) 'NuGet.Config') --verbosity minimal
    if($LASTEXITCODE -ne 0){throw 'Local simulator restore failed.'}
    & dotnet build $project --configuration Release --no-restore --verbosity minimal "-p:OutputPath=$output"
    if ($LASTEXITCODE -ne 0) { throw 'Local simulator build failed.' }
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.ArgumentList.Add((Join-Path $output 'NexaConnect.PaymentProviderSimulator.dll'))
    $start.Environment['ASPNETCORE_ENVIRONMENT']='Testing'
    $start.Environment['DOTNET_ENVIRONMENT']='Testing'
    $start.Environment['ASPNETCORE_URLS']=$baseUrl
    $start.Environment['Kestrel__Certificates__Default__Path']=$pfxPath
    $start.Environment['Kestrel__Certificates__Default__Password']=$password
    $start.Environment['Simulator__ApiKey']=$apiKey
    $process=[Diagnostics.Process]::Start($start)
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(20)
    do {
        if($process.HasExited){throw 'Local simulator exited before HTTPS readiness.'}
        try { Invoke-Simulator ($baseUrl+'health/live') -TimeoutSec 2 | Out-Null; break } catch {
            if([DateTimeOffset]::UtcNow -ge $deadline){throw 'Local simulator HTTPS readiness failed.'}
            Start-Sleep -Milliseconds 200
        }
    } while($true)
    $headers=@{Authorization="Bearer $apiKey"}
    $id=[Guid]::NewGuid().ToString('D')
    $body=@{paymentIntentId=$id;orderId=[Guid]::NewGuid().ToString('D');amount=1.00;currency='THB';paymentMethod='card'}
    $auth=Invoke-Simulator ($baseUrl+'v1/authorizations') -Method Post -Headers $headers -ContentType 'application/json' -Body ($body|ConvertTo-Json)
    $replay=Invoke-Simulator ($baseUrl+'v1/authorizations') -Method Post -Headers $headers -ContentType 'application/json' -Body ($body|ConvertTo-Json)
    if($auth.providerTransactionId -ne $replay.providerTransactionId){throw 'Authorization replay changed identity.'}
    $body.amount=2
    $conflict=Invoke-Simulator ($baseUrl+'v1/authorizations') -Method Post -Headers $headers -ContentType 'application/json' -Body ($body|ConvertTo-Json) -SkipHttpErrorCheck
    if($conflict.StatusCode -ne 409){throw 'Changed authorization replay did not conflict.'}
    $denied=Invoke-Simulator ($baseUrl+'v1/authorizations/'+$id) -Headers @{Authorization='Bearer invalid'} -SkipHttpErrorCheck
    if($denied.StatusCode -ne 401){throw 'Invalid simulator credential was accepted.'}
    $authorizationStatus=Invoke-Simulator ($baseUrl+'v1/authorizations/'+$id) -Headers $headers
    if($authorizationStatus.status -ne 'authorized'){throw 'Authorization status failed.'}
    $headers['Idempotency-Key']=$id
    $captureBody=@{paymentIntentId=$id;providerAuthorizationId=$auth.providerTransactionId;amount=1.00;currency='THB'}|ConvertTo-Json
    $badHeaders=@{Authorization="Bearer $apiKey";'Idempotency-Key'='wrong'}
    $badCapture=Invoke-Simulator ($baseUrl+'v1/captures') -Method Post -Headers $badHeaders -Body $captureBody -SkipHttpErrorCheck
    if($badCapture.StatusCode -ne 400){throw 'Capture accepted an invalid idempotency key.'}
    $capture=Invoke-Simulator ($baseUrl+'v1/captures') -Method Post -Headers $headers -ContentType 'application/json' -Body $captureBody
    $replay=Invoke-Simulator ($baseUrl+'v1/captures') -Method Post -Headers $headers -ContentType 'application/json' -Body $captureBody
    $status=Invoke-Simulator ($baseUrl+'v1/captures/'+$id) -Headers $headers
    if($capture.providerTransactionId -ne $replay.providerTransactionId -or $status.status -ne 'captured'){throw 'Capture replay/status failed.'}
    $headers['Idempotency-Key']="void:$id"
    $voidBody=@{paymentIntentId=$id;providerAuthorizationId=$auth.providerTransactionId}|ConvertTo-Json
    $capturedVoid=Invoke-Simulator ($baseUrl+'v1/voids') -Method Post -Headers $headers -Body $voidBody -SkipHttpErrorCheck
    if($capturedVoid.StatusCode -ne 409){throw 'Simulator allowed voiding a captured payment.'}
    $voidId=[Guid]::NewGuid().ToString('D')
    $body.paymentIntentId=$voidId; $body.orderId=[Guid]::NewGuid().ToString('D'); $body.amount=1.00
    $voidAuth=Invoke-Simulator ($baseUrl+'v1/authorizations') -Method Post -Headers $headers -Body ($body|ConvertTo-Json)
    $voidBody=@{paymentIntentId=$voidId;providerAuthorizationId=$voidAuth.providerTransactionId}|ConvertTo-Json
    $badVoid=Invoke-Simulator ($baseUrl+'v1/voids') -Method Post -Headers $badHeaders -Body $voidBody -SkipHttpErrorCheck
    if($badVoid.StatusCode -ne 400){throw 'Void accepted an invalid idempotency key.'}
    $headers['Idempotency-Key']="void:$voidId"
    $void=Invoke-Simulator ($baseUrl+'v1/voids') -Method Post -Headers $headers -Body $voidBody
    $voidReplay=Invoke-Simulator ($baseUrl+'v1/voids') -Method Post -Headers $headers -Body $voidBody
    $voidStatus=Invoke-Simulator ($baseUrl+'v1/voids/'+$voidId) -Headers $headers
    if($void.providerTransactionId -ne $voidReplay.providerTransactionId -or $voidStatus.status -ne 'voided'){throw 'Void replay/status failed.'}
    $headers['Idempotency-Key']=$voidId
    $captureAfterVoid=@{paymentIntentId=$voidId;providerAuthorizationId=$voidAuth.providerTransactionId;amount=1.00;currency='THB'}|ConvertTo-Json
    $voidedCapture=Invoke-Simulator ($baseUrl+'v1/captures') -Method Post -Headers $headers -Body $captureAfterVoid -SkipHttpErrorCheck
    if($voidedCapture.StatusCode -ne 409){throw 'Simulator allowed capture after void.'}
    $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL=$baseUrl
    $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY=$apiKey
    $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT='1.00'
    $env:NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY='THB'
    $env:NEXACONNECT_PAYMENT_PROVIDER_SIMULATOR_CERT_SHA256=$pin
    $env:NEXACONNECT_PAYMENT_PROVIDER_AUTHORIZATION_PATH='v1/authorizations'
    $env:NEXACONNECT_PAYMENT_PROVIDER_AUTHORIZATION_STATUS_PATH='v1/authorizations'
    $env:NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_PATH='v1/captures'
    $env:NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_STATUS_PATH='v1/captures'
    $env:NEXACONNECT_PAYMENT_PROVIDER_VOID_PATH='v1/voids'
    $env:NEXACONNECT_PAYMENT_PROVIDER_VOID_STATUS_PATH='v1/voids'
    if(-not $SmokeTestOnly){
        & (Join-Path $PSScriptRoot 'test-order-provider-recovery-live.ps1') -ConfirmDisposableInfrastructure -ConfirmProcessTermination -ConfirmSandboxTransactions -DockerExecutable $DockerExecutable
    }
    $passed=$true
} finally {
    try {
        if($null -ne $process){
            if(-not $process.HasExited){$process.Kill($true);if(-not $process.WaitForExit(10000)){throw 'Simulator process cleanup failed.'}}
            $process.Dispose()
        }
    } finally {
        if($null -ne $client){$client.Dispose()}
        if($null -ne $certificate){$certificate.Dispose()}
        if($null -ne $rsa){$rsa.Dispose()}
        try { if(Test-Path -LiteralPath $pfxPath){Remove-Item -LiteralPath $pfxPath -Force} }
        finally {
            foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}
            @{localSimulator=$true;passed=$passed;smokeTestOnly=[bool]$SmokeTestOnly;externalProviderVerified=$false;privateKeyRemoved=(-not (Test-Path -LiteralPath $pfxPath))}|ConvertTo-Json|Set-Content (Join-Path $runRoot 'summary.json')
        }
    }
}
Write-Output "Local HTTPS provider acceptance passed. Sanitized summary retained at '$runRoot'."
