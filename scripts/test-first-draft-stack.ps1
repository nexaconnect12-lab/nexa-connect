#requires -Version 7.4
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure,[string]$RunId,[switch]$NoBuild,[string]$QrPilotOrigin)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm isolated draft startup, migrations and exact cleanup.'}
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$existing=[bool]$RunId
if(-not $RunId){$RunId=[Guid]::NewGuid().ToString('N')}
$run=Get-DraftRunDirectory $root $RunId
$passed=$false;$cleanup=$false;$checks=[ordered]@{}
function Browser-Smoke([string]$Mode) {
    $state=Get-Content (Join-Path $run 'state.json') -Raw | ConvertFrom-Json -AsHashtable
    $secrets=Read-DraftSecrets (Join-Path $run 'secrets.clixml')
    $settings=@{NEXACONNECT_DRAFT_URL=$state.urls.Bff;NEXACONNECT_DRAFT_STATE=(Join-Path $run 'browser-state.json');NEXACONNECT_DRAFT_SMOKE_PASSWORD=$secrets.READER_PASSWORD;NEXACONNECT_DRAFT_SMOKE_SUBJECT=$state.smokeSubject;NEXACONNECT_DRAFT_SMOKE_MODE=$Mode}
    $previous=@{}
    try {
        foreach($entry in $settings.GetEnumerator()){$previous[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key,'Process');[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
        & node (Join-Path $root 'src/Frontend/e2e/first-draft/smoke.mjs')
        if($LASTEXITCODE -ne 0){throw 'Real draft OIDC/session smoke failed.'}
    } finally {Restore-DraftEnvironment $previous}
}
try {
    & (Join-Path $PSScriptRoot 'test-first-draft-guards.ps1')
    if(-not $existing){& (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Start -RunId $RunId -ConfirmTestResources -NoBuild:$NoBuild -QrPilotOrigin $QrPilotOrigin}
    $state=Get-Content (Join-Path $run 'state.json') -Raw | ConvertFrom-Json -AsHashtable
    if($state.status -ne 'Ready'){throw 'Only a Ready owned draft can enter verification.'}
    if(-not (Test-Path (Join-Path $run 'pos/appsettings.json'))){throw 'POS artifact/configuration missing; build the draft first.'}
    $pos=Get-Content (Join-Path $run 'pos/appsettings.json') -Raw | ConvertFrom-Json
    if($pos.Pos.Currency -ne 'THB' -or $pos.Pos.PaymentMethod -ne 'cash_manual' -or $pos.Pos.EnableOmiseTestCheckout){throw 'Manual-tender draft configuration not enforced.'}
    $checks.manualConfiguration=$true
    if($QrPilotOrigin){
        if(-not $pos.Pos.UseIsolatedLocalPilotStorage){throw 'QR pilot must use isolated current-user terminal storage.'}
        if(-not $state.Contains('qrPilotOrigin') -or $state.qrPilotOrigin -cne $QrPilotOrigin -or $state.qrPilotArtifacts -cne (Get-DraftQrPilotArtifactHash $run)){throw 'QR pilot profile/source binding missing.'}
        $reply=Invoke-WebRequest ($state.urls.Bff+'bff/guest/qr/context') -SkipCertificateCheck -SkipHttpErrorCheck
        if($reply.StatusCode -ne 403){throw 'Loopback Host must not bypass the configured public guest origin.'}
        $reply=Invoke-WebRequest ($state.urls.Bff+'bff/guest/qr/context') -Headers @{Host=([uri]$QrPilotOrigin).Authority} -SkipCertificateCheck -SkipHttpErrorCheck
        if($reply.StatusCode -ne 401){throw 'Enabled exact-host guest context must require its separate visitor cookie.'}
        $checks.qrPilotProfile=$true
    }
    foreach($name in @('Order','POS','Reporting')) {
        $body=Invoke-RestMethod ($state.urls[$name]+'health/live')
        if($body.status -ne 'Healthy' -or @($body.PSObject.Properties).Count -ne 1){throw 'Liveness response disclosed extra fields or was malformed.'}
    }
    $checks.anonymousLiveness=$true
    $secrets=Read-DraftSecrets (Join-Path $run 'secrets.clixml')
    $compose=Get-DraftComposeArguments $root $RunId
    $previous=Set-DraftComposeEnvironment $secrets $RunId ([uri]$state.urls.Bff).Port $state.infrastructure
    $oldPgPassword=[Environment]::GetEnvironmentVariable('PGPASSWORD','Process')
    try {
        [Environment]::SetEnvironmentVariable('PGPASSWORD',$secrets.RUNTIME_PASSWORD,'Process')
        foreach($name in (Get-DraftServiceCatalog $root).Keys) {
            $suffix=if($name -eq 'PlatformDirectory'){'platform'}else{$name.ToLowerInvariant()}
            $role=if($name -eq 'PlatformDirectory'){'platform_directory_app'}else{'nexaconnect_'+$suffix+'_app'}
            $otherSuffix=if($suffix -eq 'platform'){'order'}else{'platform'}
            $sql="SELECT has_column_privilege(current_user,'nexaconnect_schema_migrations','version','SELECT'), has_table_privilege(current_user,'nexaconnect_schema_migrations','INSERT'), has_table_privilege(current_user,'nexaconnect_schema_migrations','UPDATE'), has_table_privilege(current_user,'nexaconnect_schema_migrations','DELETE'), has_schema_privilege(current_user,'public','CREATE'), has_database_privilege(current_user,'nexa_review_it_${RunId}_$otherSuffix','CONNECT');"
            $grantState=(& docker @compose exec -T -e PGPASSWORD postgres psql -h 127.0.0.1 -U $role -d "nexa_review_it_${RunId}_$suffix" -t -A -c $sql) -join ''
            if($LASTEXITCODE -ne 0 -or $grantState.Trim() -ne 't|f|f|f|f|f'){throw 'Owning runtime/history grants failed least-privilege verification.'}
        }
        $checks.runtimeGrantIsolation=$true
    } finally { [Environment]::SetEnvironmentVariable('PGPASSWORD',$oldPgPassword,'Process');Restore-DraftEnvironment $previous }
    Browser-Smoke 'login';$checks.realOidcLogin=$true
    & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Restart -RunId $RunId -Service Bff
    Browser-Smoke 'resume';$checks.bffRedisRestart=$true
    & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Restart -RunId $RunId -Service Kitchen
    $checks.kitchenRestart=$true
    & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Stop -RunId $RunId
    & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Start -RunId $RunId -NoBuild
    Browser-Smoke 'resume';$checks.fullStopStartPersistence=$true
    if($QrPilotOrigin){$reply=Invoke-WebRequest ($state.urls.Bff+'bff/guest/qr/context') -Headers @{Host=([uri]$QrPilotOrigin).Authority} -SkipCertificateCheck -SkipHttpErrorCheck;if($reply.StatusCode -ne 401){throw 'QR profile was lost after restart.'};$checks.qrPilotRestart=$true}
    & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Status -RunId $RunId | Out-Null
    $checks.status=$true;$passed=$true
} finally {
    if(Test-Path -LiteralPath (Join-Path $run 'state.json')) {
        try {
            & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Reset -RunId $RunId -ConfirmTestResources
            $finalState=Get-Content (Join-Path $run 'state.json') -Raw | ConvertFrom-Json
            $cleanup=$finalState.status -eq 'Reset'
        } catch { $cleanup=$false }
        if($cleanup){foreach($file in @('browser-state.json','local.pfx')){if(Test-Path (Join-Path $run $file)){Remove-Item -LiteralPath (Join-Path $run $file)}}}
        @{runId=$RunId;passed=($passed -and $cleanup);checks=$checks;cleanupVerified=$cleanup;productionVerified=$false;branchProvisioned=$false;sourceRevision=(& git -C $root rev-parse HEAD);sourceDirty=[bool](& git -C $root status --porcelain)} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'verification.json')
    }
    if(-not $cleanup){throw 'Draft verification cleanup could not be verified.'}
}
Write-Output "Draft lifecycle verification passed: $run/verification.json"
