#requires -Version 7.4
$ErrorActionPreference='Stop'
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/local-pilot-helpers.ps1"
$count=0
function Require([bool]$Value){if(-not $Value){throw 'Local pilot guard failed.'};$script:count++}
function Reject([scriptblock]$Work){try{& $Work|Out-Null}catch{$script:count++;return};throw 'Unsafe local pilot input accepted.'}
Require ((Get-LocalPilotIdentityBinding @('127.0.0.1:49001') 1 49001 $true) -ceq 'owned')
Require ((Get-LocalPilotIdentityBinding @() 0 49001 $false) -ceq 'stopped')
Require ((Get-LocalPilotIdentityBinding @() 0 49001 $true) -ceq 'changed-owner')
Require ((Get-LocalPilotIdentityBinding @('0.0.0.0:49001') 1 49001 $true) -ceq 'changed-owner')
Require ((Get-LocalPilotIdentityBinding @('127.0.0.1:49002') 1 49001 $true) -ceq 'changed-owner')
Require ((Get-LocalPilotIdentityBinding @('127.0.0.1:49001') 2 49001 $true) -ceq 'changed-owner')
$responding=@(@{service='Catalog';status='responding'},@{service='Identity';status='responding'})
Require ((Get-LocalPilotRecoveryPlan $responding $false) -ceq 'open')
Require ((Get-LocalPilotRecoveryPlan $responding $true) -ceq 'open')
$missing=@(@{service='Catalog';status='stopped'})
Require ((Get-LocalPilotRecoveryPlan $missing $false) -ceq 'resume')
Reject {Get-LocalPilotRecoveryPlan $missing $true}
Reject {Get-LocalPilotRecoveryPlan @(@{service='Catalog';status='changed-owner'}) $false}
$source=@{revision='fixture';dirty=$true;inputHash='a'*64}
$state=@{publishedSource=$source;publishedArtifacts='b'*64}
Assert-LocalPilotPublished $state $source ('b'*64);$count++
Assert-LocalPilotPublished @{qrPilotSource=$source;qrPilotArtifacts='b'*64} $source ('b'*64);$count++
Reject {Assert-LocalPilotPublished @{} $source ('b'*64)}
Reject {Assert-LocalPilotPublished $state $source ('c'*64)}
Reject {Assert-LocalPilotPublished $state @{revision='changed';dirty=$true;inputHash='a'*64} ('b'*64)}
$id='a'*32;$state=@{runId=$id;realm="nexa-review-it-$id";project="nexa-draft-$id";status='Ready';provisionedBranch=$true;authority="http://127.0.0.1:49001/realms/nexa-review-it-$id";urls=@{POS='http://127.0.0.1:49002/';Order='http://127.0.0.1:49003/';Catalog='http://127.0.0.1:49004/';Payment='http://127.0.0.1:49005/'}}
$scope=@{};$reference=@{recoveryLabAccessCompleted=$false}
foreach($key in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){$scope[$key]=[guid]::NewGuid().ToString('D');$reference[$key.Substring(0,1).ToLowerInvariant()+$key.Substring(1)]=$scope[$key]}
$profile=@{Identity=@{Authority=$state.authority;ClientId='nexaconnect-pos';RedirectUri='nexaconnect-pos://oauth/callback'};Services=@{PosApi=$state.urls.POS;OrderApi=$state.urls.Order;CatalogApi=$state.urls.Catalog;PaymentApi=$state.urls.Payment};Pos=$scope.Clone()}
$profile.Pos.Currency='THB';$profile.Pos.PaymentMethod='cash_manual';$profile.Pos.EnableOmiseTestCheckout=$false
Assert-LocalPilotProfile $state $profile $reference $false;$count++
Reject {Assert-LocalPilotProfile $state $profile $reference $true}
$reference.recoveryLabAccessCompleted=$true;Reject {Assert-LocalPilotProfile $state $profile $reference $false};$reference.recoveryLabAccessCompleted=$false
$profile.Pos.BranchId=[guid]::NewGuid().ToString('D');Reject {Assert-LocalPilotProfile $state $profile $reference $false};$profile.Pos.BranchId=$scope.BranchId
$profile.Services.CatalogApi='http://127.0.0.1:49999/';Reject {Assert-LocalPilotProfile $state $profile $reference $false};$profile.Services.CatalogApi=$state.urls.Catalog
$state.status='Failed';Reject {Assert-LocalPilotProfile $state $profile $reference $false}
Write-Output "PASS: $count normal-pilot scope/source/recovery guards. No services or recovery changed."
