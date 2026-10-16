#requires -Version 7.4
param([Parameter(Mandatory)][string]$RunId,[Parameter(Mandatory)][ValidateSet('cashier','kitchen-operator','store-manager','accountant')][string]$Role)
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$run=Get-DraftRunDirectory $root $RunId
$state=Get-Content (Join-Path $run 'state.json') -Raw | ConvertFrom-Json
if($state.runId -ne $RunId -or $state.status -eq 'Reset' -or -not $state.provisionedBranch){throw 'A provisioned retained draft is required.'}
$secrets=Read-DraftSecrets (Join-Path $run 'secrets.clixml')
$key=switch($Role){'cashier'{'READER_PASSWORD'} 'kitchen-operator'{'SECONDMANAGER_PASSWORD'} 'store-manager'{'RESOLVER_PASSWORD'} 'accountant'{'ACCOUNTANT_PASSWORD'}}
# Explicit local interactive retrieval only. Never invoke this script in CI, transcripts or shared logs.
[pscustomobject]@{username="draft-$Role";password=$secrets[$key];portal=$state.urls.Bff}
