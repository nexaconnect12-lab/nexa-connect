#requires -Version 7.4
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
$passed=0
function Reject([scriptblock]$Operation){$rejected=$false;try{& $Operation|Out-Null}catch{$rejected=$true};if(-not $rejected){throw 'Pilot guard accepted an unsafe input.'};$script:passed++}
foreach($bad in @('http://qr.example.test:8443','https://localhost:8443','https://qr.example.test','https://u:p@qr.example.test:8443','https://qr.example.test:8443/order','https://qr.example.test:8443?token=private')){Reject {Assert-DraftQrPilotOrigin $bad}}
Assert-DraftQrPilotOrigin 'https://qr.example.test:8443';$passed++
$settings=Get-DraftQrPilotSettings Order 'https://qr.example.test:8443'
if($settings.Count -ne 4 -or @($settings.Values|Where-Object {$_ -cne 'true'}).Count){throw 'Order QR settings incomplete.'};$passed++
$settings=Get-DraftQrPilotSettings Bff 'https://qr.example.test:8443'
if($settings.QrGuest__PublicOrigin -cne 'https://qr.example.test:8443' -or $settings.QrGuest__Enabled -cne 'true' -or $settings.QrGuest__AcceptanceEnabled -cne 'true'){throw 'BFF exact origin/flags missing.'};$passed++
if((Get-DraftQrPilotSettings Restaurant 'https://qr.example.test:8443').QrEntry__Enabled -cne 'true'){throw 'Restaurant admission disabled.'};$passed++
if((Get-DraftQrPilotSettings Payment 'https://qr.example.test:8443').Count -ne 0){throw 'QR profile broadened payment provider authority.'};$passed++
$root=Split-Path $PSScriptRoot -Parent
$source=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'qr-pilot.ps1') -Raw
if($source -match 'Import-Certificate|New-NetFirewallRule|Stop-Process\s+-Name|SkipCertificateCheck'){throw 'Pilot tooling changed trust/firewall or used unsafe transport/process cleanup.'};$passed++
$id=[guid]::NewGuid().ToString('N');$directory=Join-Path $root ".runstate/qr7/$id";New-Item -ItemType Directory -Path $directory|Out-Null
try{
 @{version=1;id=$id;stackRunId=[guid]::NewGuid().ToString('N');candidateBinding=('a'*64);publicOrigin='https://qr.example.test:8443';listenAddress='192.168.1.10'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $directory 'plan.json')
 & (Join-Path $PSScriptRoot 'qr-pilot.ps1') -Action Certificates -PilotId $id|Out-Null
 if(-not (Test-Path -LiteralPath (Join-Path $directory 'client-ca.cer'))){throw 'Public CA export missing.'};$passed++
 $certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile((Join-Path $directory 'certificate.pem'),(Join-Path $directory 'private-key.pem'))
 try{if(-not $certificate.HasPrivateKey -or -not $certificate.MatchesHostname('qr.example.test',$false,$false)){throw 'Leaf certificate does not match its protected key and origin.'};$passed++}finally{$certificate.Dispose()}
 $ca=[Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadCertificateFromFile((Join-Path $directory 'client-ca.cer'))
 try{if($ca.HasPrivateKey){throw 'Test CA signing key was retained.'};$passed++}finally{$ca.Dispose()}
 Reject {& (Join-Path $PSScriptRoot 'qr-pilot.ps1') -Action Certificates -PilotId $id}
 if(Test-Path -LiteralPath (Join-Path $directory 'edge-process.json')){throw 'Certificate preparation opened a listener.'};$passed++
}finally{
 foreach($file in @(Get-ChildItem -LiteralPath $directory -File)){Remove-Item -LiteralPath $file.FullName}
 Remove-Item -LiteralPath $directory
}
Write-Output "QR pilot tooling guards: $passed passed"
