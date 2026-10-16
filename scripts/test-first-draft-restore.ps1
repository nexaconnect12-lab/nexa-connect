#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunId,[switch]$ConfirmDisposableInfrastructure)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure){throw 'Confirm isolated restore containers and stopping the generated checkout draft.'}
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
$run=Get-DraftRunDirectory $root $RunId
$memo=Get-Content (Join-Path $run 'reference.json.checkout.json') -Raw|ConvertFrom-Json
if($memo.runId -cne $RunId -or $memo.checks.managerReconciliation -isnot [bool] -or -not $memo.checks.managerReconciliation){throw 'A completed disposable manager checkout is required.'}
$endpoint=& docker context inspect --format '{{.Endpoints.docker.Host}}'
if($LASTEXITCODE -ne 0 -or $env:DOCKER_HOST -or $endpoint -notmatch '^npipe://'){throw 'Local Docker Desktop is required.'}
& (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Stop -RunId $RunId
$lock=Open-DraftLifecycleLock $run
$restoreId=[Guid]::NewGuid().ToString('N');$backup=Join-Path $run "restore-drill/$restoreId"
New-Item -ItemType Directory -Path $backup | Out-Null
Protect-DraftDirectory $backup
$owned=@();$passed=$false;$cleanup=$false;$prior=@{};$proof=@();$configuration=@()
function Docker-Checked([string[]]$Arguments){
    # Dump/restore errors may contain financial rows or password hashes: keep output private.
    & docker @Arguments >> (Join-Path $backup 'private-docker.log') 2>&1
    if($LASTEXITCODE -ne 0){throw 'Isolated backup/restore operation failed; private diagnostics retained.'}
}
function Fingerprint([string]$Container,[string]$User,[string]$Database,[bool]$Permissions=$false) {
    # pg_restore checks DDL execution. Data/sequences and owners/ACLs compare separately;
    # PostgreSQL may flatten redundant AND nodes in CHECK expressions on restore.
    $mode=if($Permissions){'--schema-only'}else{'--data-only'}
    $arguments=@('exec',$Container,'pg_dump','-U',$User,'-d',$Database,$mode,'-f','/tmp/drill-fingerprint.sql')
    if($Permissions){$arguments+='--create'}
    Docker-Checked $arguments
    $file=Join-Path $backup "private-fingerprint-$Permissions-$Container-$Database.sql"
    Docker-Checked @('cp',"${Container}:/tmp/drill-fingerprint.sql",$file)
    if($Permissions){return Get-DraftPermissionsHash $file}
    return Get-DraftDumpHash $file
}
try {
    $state=Get-Content (Join-Path $run 'state.json') -Raw|ConvertFrom-Json -AsHashtable
    Assert-DraftRestoreState $state $RunId
    $secrets=Read-DraftSecrets (Join-Path $run 'secrets.clixml')
    $prior=Set-DraftComposeEnvironment $secrets $RunId ([uri]$state.urls.Bff).Port $state.infrastructure
    $compose=Get-DraftComposeArguments $root $RunId
    Docker-Checked ($compose+@('up','-d','--wait','postgres','keycloak-db'))
    foreach($service in @('postgres','keycloak-db')) {
        $source=(& docker @compose ps -q $service).Trim()
        if($LASTEXITCODE -ne 0 -or $source -notmatch '^[a-f0-9]{12,64}$'){throw 'Missing owned backup source.'}
        $owner=& docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' $source
        if($LASTEXITCODE -ne 0 -or $owner -cne $state.project){throw 'Backup source owner mismatch.'}
        $image=(& docker inspect --format '{{.Image}}' $source).Trim()
        if($LASTEXITCODE -ne 0 -or $image -notmatch '^sha256:[a-f0-9]{64}$'){throw 'Source image identity unavailable.'}
        $user=if($service -eq 'postgres'){'postgres'}else{'keycloak'}
        $clone="nexa-draft-restore-$restoreId-$service";$admin='restore_'+$restoreId
        Docker-Checked @('run','--pull','never','--detach','--name',$clone,'--label',"nexa.restore=$restoreId",'--network','none','--tmpfs','/var/lib/postgresql/data:rw','-e',"POSTGRES_USER=$admin",'-e',"POSTGRES_PASSWORD=$([Guid]::NewGuid().ToString('N'))",$image)
        $owned+= $clone
        $ready=$false
        for($i=0;$i -lt 80;$i++) {& docker exec $clone pg_isready -U $admin -d $admin 2>$null | Out-Null;if($LASTEXITCODE -eq 0){$ready=$true;break};Start-Sleep -Milliseconds 250}
        if(-not $ready){throw 'Isolated restore database did not become ready.'}
        Docker-Checked @('exec',$source,'pg_dumpall','-U',$user,'--globals-only','-f','/tmp/drill-globals.sql')
        $globals=Join-Path $backup "$service-globals.sql"
        Docker-Checked @('cp',"${source}:/tmp/drill-globals.sql",$globals)
        Docker-Checked @('cp',$globals,"${clone}:/tmp/drill-globals.sql")
        Docker-Checked @('exec',$clone,'psql','-U',$admin,'-d',$admin,'-v','ON_ERROR_STOP=1','-f','/tmp/drill-globals.sql')
        $databases=if($service -eq 'postgres'){@((Get-DraftServiceCatalog $root).Keys|ForEach-Object {$suffix=if($_ -eq 'PlatformDirectory'){'platform'}else{$_.ToLowerInvariant()};"nexa_review_it_${RunId}_$suffix"})}else{@('keycloak')}
        foreach($db in $databases){
            $file=Join-Path $backup "$db.dump"
            Docker-Checked @('exec',$source,'pg_dump','-U',$user,'-d',$db,'--format=custom','--create','-f','/tmp/drill.dump')
            Docker-Checked @('cp',"${source}:/tmp/drill.dump",$file)
            Docker-Checked @('cp',$file,"${clone}:/tmp/drill.dump")
            Docker-Checked @('exec',$clone,'pg_restore','-U',$admin,'-d',$admin,'--exit-on-error','--create','/tmp/drill.dump')
            if((Fingerprint $source $user $db) -cne (Fingerprint $clone $admin $db)){throw 'Restored data/sequences differ from the quiesced source.'}
            if((Fingerprint $source $user $db $true) -cne (Fingerprint $clone $admin $db $true)){throw 'Restored ownership/permissions differ from the quiesced source.'}
            $proof+=@{service=$service;database=$db;dataAndSequencesMatch=$true;ownershipAndPermissionsMatch=$true;schemaRestoreVerified=$true;backupSha256=(Get-FileHash $file).Hash.ToLowerInvariant();sourceImage=$image}
        }
    }
    $paths=@('state.json','secrets.clixml','local.pfx','reference.json','pos-configuration.json','apps/Bff/.runstate/data-protection-keys/customer-bff')
    foreach($path in $paths){
        $source=Join-Path $run $path
        if(-not (Test-Path -LiteralPath $source)){throw 'Required owned configuration recovery material is missing.'}
        $target=Join-Path $backup "configuration/$path"
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Recurse
        $files=@(if(Test-Path $source -PathType Container){Get-ChildItem $source -File -Recurse}else{Get-Item $source})
        if(-not $files.Count){throw 'Configuration recovery directory is empty.'}
        foreach($file in $files){$relative=[IO.Path]::GetRelativePath($run,$file.FullName);$copy=Join-Path $backup "configuration/$relative";if((Get-FileHash $file.FullName).Hash -cne (Get-FileHash $copy).Hash){throw 'Configuration recovery checksum mismatch.'};$configuration+= $relative}
    }
    $recovered=Read-DraftSecrets (Join-Path $backup 'configuration/secrets.clixml')
    if(($recovered|ConvertTo-Json -Compress) -cne ($secrets|ConvertTo-Json -Compress)){throw 'Same-account protected credential recovery failed.'}
    $passed=$true
} finally {
    try {
        foreach($clone in $owned){
            if($clone -cnotmatch "^nexa-draft-restore-$restoreId-(postgres|keycloak-db)$"){throw 'Unexpected restore cleanup target.'}
            $owner=& docker inspect --format '{{ index .Config.Labels "nexa.restore" }}' $clone
            if($LASTEXITCODE -ne 0 -or $owner -cne $restoreId){throw 'Restore container owner mismatch.'}
            Docker-Checked @('rm','-f','-v',$clone)
        }
        $remaining=@(& docker ps -aq --filter "label=nexa.restore=$restoreId")
        if($LASTEXITCODE -ne 0 -or $remaining.Count){throw 'Restore cleanup is incomplete.'}
        $cleanup=$true
    } finally {
        Restore-DraftEnvironment $prior
        $lock.Dispose()
        @{runId=$RunId;restoreId=$restoreId;passed=($passed -and $cleanup);cleanupVerified=$cleanup;databases=$proof;configurationFiles=$configuration;sameWindowsAccountOnly=$true;restoredApplicationGraphVerified=$false;brokerRestoreVerified=$false;redisSessionRestoreVerified=$false;posLocalStateCopied=$false}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $backup 'verification.json')
    }
}
if(-not $passed -or -not $cleanup){throw 'Restore drill failed.'}
& (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Start -RunId $RunId -NoBuild
Write-Output 'Quiesced database/configuration restore comparison and exact clone cleanup passed; original draft resumed.'
