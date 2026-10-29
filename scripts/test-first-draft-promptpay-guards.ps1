#requires -Version 7.4
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-promptpay-helpers.ps1')
$script:count=0
function Require([bool]$Value){if(-not $Value){throw 'Branch QR profile assertion failed.'};$script:count++}
function Reject([scriptblock]$Work){try{& $Work}catch{$script:count++;return};throw 'Unsafe branch QR profile accepted.'}
Require (-not (Test-DraftBranchPromptPayEnabled @{}))
Reject {Test-DraftBranchPromptPayEnabled @{branchPromptPayQrEnabled='true'}}
foreach($case in @(@('Restart',$false,0),@('Start',$true,0),@('Start',$false,1))){
 $state=@{processes=@{};mediaEnabled=$true};if($case[2]){$state.processes.POS=@{}}
 Reject {Enable-DraftBranchPromptPay $state $case[0] $case[1]}
 Require (-not (Test-DraftBranchPromptPayEnabled $state))
}
foreach($value in @($false,'true',$null)){
 $state=@{processes=@{};mediaEnabled=$value};Reject {Enable-DraftBranchPromptPay $state Start $false}
}
$state=@{processes=@{};mediaEnabled=$true;omiseTestCheckoutEnabled=$true}
Enable-DraftBranchPromptPay $state Start $false
Require (Test-DraftBranchPromptPayEnabled $state)
Require $state.omiseTestCheckoutEnabled
$roundtrip=$state|ConvertTo-Json|ConvertFrom-Json -AsHashtable
Require (Test-DraftBranchPromptPayEnabled $roundtrip)
Write-Output "PASS: $script:count branch QR profile guards; no infrastructure or payment commands."
