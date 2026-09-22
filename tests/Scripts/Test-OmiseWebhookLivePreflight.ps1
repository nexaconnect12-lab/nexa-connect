$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$runner=Join-Path $root 'scripts/test-payment-omise-webhook-live.ps1'
$runnerSource=Get-Content -LiteralPath $runner -Raw
if($runnerSource-match'(?i)(?<!\.)\$host\b'){throw 'The runner must not use the read-only PowerShell Host automatic variable as a process handle.'}
if($runnerSource-match'(?i)\breturn(?=\[)'){throw 'The runner must separate the return keyword from a following type conversion.'}
$localProbe='Test-WebhookRoute ([Uri]"http://127.0.0.1:$ListenerPort/api/payment/v1/webhooks/omise") ''Local Payment'''
$publicProbe='Test-WebhookRoute $PublicWebhookUrl ''Public HTTPS'''
if(-not$runnerSource.Contains($localProbe)-or-not$runnerSource.Contains($publicProbe)-or$runnerSource-notmatch"(?s)function Test-WebhookRoute.*-Method Post.*expected 400 from the signed non-event probe"){throw 'The runner must distinguish local Payment routing from public HTTPS tunnel failures with a signed non-event probe before authorization.'}
$names=@('NEXACONNECT_OMISE_TEST_SECRET_KEY','NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN','NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET');$saved=@{}
foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name)}
try{
 $env:NEXACONNECT_OMISE_TEST_SECRET_KEY='skey_test_abcdefghijklmnopqrstuvwxyz'
 $env:NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN='tokn_test_abcdefghijklmnopqrstuvwxyz'
 $env:NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET=[Convert]::ToBase64String((1..32|ForEach-Object{[byte]$_}))
 $json=&$runner -PublicWebhookUrl 'https://acceptance.example.test/api/payment/v1/webhooks/omise' -ValidateOnly|ConvertFrom-Json
 if(-not$json.configurationValidated-or$json.providerRequestsSent-ne 0-or$json.financialCommandsSent-ne 0){throw 'Valid preflight did not remain side-effect free.'}
 $cases=@(
  @{Url='https://localhost/api/payment/v1/webhooks/omise';Expected='Use the exact non-production*'},
  @{Url='https://acceptance.example.test/wrong';Expected='Use the exact non-production*'},
  @{Url='https://production-payments.example.test/api/payment/v1/webhooks/omise';Expected='Use the exact non-production*'}
 )
 foreach($case in $cases){$message=$null;try{&$runner -PublicWebhookUrl $case.Url -ValidateOnly|Out-Null}catch{$message=$_.Exception.Message};if($message-notlike$case.Expected){throw "Unexpected webhook preflight result for '$($case.Url)'."}}
 $env:NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN=$null;$message=$null
 try{&$runner -PublicWebhookUrl 'https://acceptance.example.test/api/payment/v1/webhooks/omise' -ValidateOnly|Out-Null}catch{$message=$_.Exception.Message}
 if($message-notlike'Inject one fresh token*'){throw 'Missing-token preflight did not fail closed.'}
 'Omise webhook live preflight tests passed (8 cases).'
}finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
