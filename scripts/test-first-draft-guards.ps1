#requires -Version 7.4
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$passed=0
function Expect-Rejection([scriptblock]$Operation) {
    $rejected=$false
    try { & $Operation | Out-Null } catch { $rejected=$true }
    if(-not $rejected){throw 'A lifecycle safety guard accepted an invalid input.'}
    $script:passed++
}
foreach($bad in @('','../other','ABCDEF0123456789ABCDEF0123456789AB','1234',('a'*33))){Expect-Rejection { Assert-DraftRunId $bad }}
$id=[Guid]::NewGuid().ToString('N')
$directory=Get-DraftRunDirectory $root $id
if([IO.Path]::GetFullPath($directory) -ne [IO.Path]::GetFullPath((Join-Path $root ".runstate/first-draft/$id"))){throw 'Wrong run directory.'};$passed++
Expect-Rejection { Assert-DraftPayment 'card_omise_test' '' }
Expect-Rejection { Assert-DraftPayment 'promptpay_manual' '' }
Assert-DraftPayment 'cash_manual' '';$passed++
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('nexa-draft-guards-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $lock=Open-DraftLifecycleLock $temporary
    try { Expect-Rejection { Open-DraftLifecycleLock $temporary } } finally { $lock.Dispose() }
    $lock=Open-DraftLifecycleLock $temporary;$lock.Dispose();$passed++
    $image=Join-Path $temporary 'synthetic.png'
    [IO.File]::WriteAllBytes($image,[Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN8sAAAAASUVORK5CYII='))
    Assert-DraftPayment 'promptpay_manual' $image;$passed++
    $text=Join-Path $temporary 'wrong.txt';Set-Content -LiteralPath $text -Value 'synthetic'
    Expect-Rejection { Assert-DraftPayment 'promptpay_manual' $text }
    if($IsWindows) {
        $protected=Join-Path $temporary 'protected.clixml'
        Save-DraftSecrets $protected @{fixture='synthetic-guard-secret'}
        if((Read-DraftSecrets $protected).fixture -ne 'synthetic-guard-secret' -or
            (Get-Content -LiteralPath $protected -Raw).Contains('synthetic-guard-secret')){throw 'Protected credential round-trip failed.'};$passed++
    }
} finally {
    foreach($file in @(Get-ChildItem -LiteralPath $temporary -File)){Remove-Item -LiteralPath $file.FullName}
    Remove-Item -LiteralPath $temporary
}
Assert-DraftUrls @{Order='http://127.0.0.1:12345/';Bff='https://127.0.0.1:12346/'};$passed++
Expect-Rejection { Assert-DraftUrls @{Order='http://example.invalid:12345/'} }
Expect-Rejection { Assert-DraftUrls @{Order='http://127.0.0.1:12345/';POS='http://127.0.0.1:12345/'} }
$actual=[pscustomobject]@{Id=123;StartTime=[DateTime]::UtcNow}
$record=@{id=123;startTicks=$actual.StartTime.ToUniversalTime().Ticks}
if(-not (Assert-DraftProcess $record $actual)){throw 'Matching process was denied.'};$passed++
Expect-Rejection { Assert-DraftProcess @{id=124;startTicks=$record.startTicks} $actual }
Expect-Rejection { Assert-DraftProcess @{id=123;startTicks=$record.startTicks-1} $actual }
if(Assert-DraftProcess $record $null){throw 'Missing process accepted as live.'};$passed++
$catalog=Get-DraftServiceCatalog $root
if($catalog.POS.version -ne 23 -or $catalog.Reporting.version -ne 22 -or $catalog.Count -ne 10){throw 'Unexpected service inventory.'};$passed++
$argsList=Get-DraftComposeArguments $root $id
if($argsList -notcontains "nexa-draft-$id" -or $argsList -notcontains (Join-Path $directory 'compose.env')){throw 'Compose isolation missing.'};$passed++
$probe='NEXACONNECT_JOINED_GUARD_PROBE'
$original=[Environment]::GetEnvironmentVariable($probe,'Process')
try {
    [Environment]::SetEnvironmentVariable($probe,'before','Process')
    $previous=Set-DraftComposeEnvironment @{GUARD_PROBE='synthetic-private-value'} $id 12345 @{postgres=12346;keycloak=12347;rabbitmq=12348;redis=12349}
    if($env:NEXACONNECT_DRAFT_RABBITMQ_PORT -ne '12348'){throw 'Retained broker port was not supplied.'};$passed++
    Restore-DraftEnvironment $previous
    if([Environment]::GetEnvironmentVariable($probe,'Process') -ne 'before'){throw 'Environment was not restored.'};$passed++
} finally {[Environment]::SetEnvironmentVariable($probe,$original,'Process')}
$stateSource=Get-Content (Join-Path $root 'scripts/first-draft-stack.ps1') -Raw
Expect-Rejection { Set-DraftComposeEnvironment @{} $id 12345 @{postgres=12346;keycloak=12347;rabbitmq=12345;redis=12349} }
Expect-Rejection { Set-DraftComposeEnvironment @{} $id 12345 @{postgres=0;keycloak=12347;rabbitmq=12348;redis=12349} }
if($stateSource -match 'Stop-Process\s+-Name|docker\s+system\s+prune'){throw 'Unscoped cleanup was introduced.'};$passed++
if($IsWindows) {
    $fakeId=[Guid]::NewGuid().ToString('N');$fakeRun=Get-DraftRunDirectory $root $fakeId
    New-Item -ItemType Directory -Path $fakeRun | Out-Null
    try {
        @{runId=$fakeId;project="nexa-draft-$fakeId";status='Reset'} | ConvertTo-Json | Set-Content (Join-Path $fakeRun 'state.json')
        Expect-Rejection { & (Join-Path $root 'scripts/first-draft-stack.ps1') -Action Status -RunId $fakeId }
        $lock=Open-DraftLifecycleLock $fakeRun;$lock.Dispose();$passed++
        Remove-Item -LiteralPath (Join-Path $fakeRun 'state.json')
        Set-Content -LiteralPath (Join-Path $fakeRun 'secrets.clixml') -Value 'synthetic-recovery-marker'
        Expect-Rejection { & (Join-Path $root 'scripts/first-draft-stack.ps1') -Action Start -RunId $fakeId -ConfirmTestResources }
        if((Get-Content -LiteralPath (Join-Path $fakeRun 'secrets.clixml') -Raw).Trim() -ne 'synthetic-recovery-marker'){throw 'Missing-state recovery credentials were overwritten.'};$passed++
    } finally {
        foreach($file in @(Get-ChildItem -LiteralPath $fakeRun -File)){Remove-Item -LiteralPath $file.FullName}
        Remove-Item -LiteralPath $fakeRun
    }
}
Write-Output "$passed draft lifecycle safety checks passed."
