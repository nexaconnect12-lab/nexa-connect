#requires -Version 7.4
param([Parameter(Mandatory)][string]$RunId)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
$root=Split-Path $PSScriptRoot -Parent;$run=Get-DraftRunDirectory $root $RunId
$lock=Open-DraftLifecycleLock $run;$old=@{}
try{
 $state=Get-Content (Join-Path $run 'state.json') -Raw|ConvertFrom-Json -AsHashtable
 if($state.runId -cne $RunId -or $state.project -cne "nexa-draft-$RunId" -or $state.status -ne 'Ready' -or -not $state.provisionedBranch -or -not $state.Contains('mediaEnabled') -or $state.mediaEnabled -ne $true){throw 'A provisioned Ready local Media draft is required.'}
 Assert-DraftUrls $state.urls
 foreach($record in $state.processes.Values){if(-not(Assert-DraftProcess $record (Get-Process -Id $record.id -ErrorAction SilentlyContinue))){throw 'Owned draft process is not running.'}}
 $secrets=Read-DraftSecrets (Join-Path $run 'secrets.clixml');$reference=Get-Content (Join-Path $run 'reference.json') -Raw|ConvertFrom-Json
 $result=Join-Path $run ('media-acceptance-'+[guid]::NewGuid().ToString('N')+'.json')
 $settings=@{NEXACONNECT_DRAFT_URL=$state.urls.Bff;NEXACONNECT_DRAFT_MEDIA_PASSWORD=$secrets.RESOLVER_PASSWORD;NEXACONNECT_DRAFT_MEDIA_CASHIER_PASSWORD=$secrets.READER_PASSWORD;NEXACONNECT_DRAFT_MEDIA_ORGANIZATION=[string]$reference.organizationId;NEXACONNECT_DRAFT_MEDIA_PRODUCT=[string]$reference.products[0];NEXACONNECT_DRAFT_MEDIA_RESULT=$result}
 $settings.NEXACONNECT_DRAFT_MEDIA_BRANCH=[string]$reference.branchId
 foreach($entry in $settings.GetEnumerator()){$old[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key,'Process');[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
 & node (Join-Path $root 'src/Frontend/e2e/first-draft/media.mjs')
 if($LASTEXITCODE -ne 0){throw 'Local Media acceptance failed. No existing image/order/payment was changed by test cleanup.'}
 Write-Output "Media acceptance receipt: $result"
}finally{Restore-DraftEnvironment $old;$lock.Dispose()}
