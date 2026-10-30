#requires -Version 7.4
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-drawer-helpers.ps1')
$script:count=0
function Require([bool]$Value){if(-not $Value){throw 'Drawer history profile assertion failed.'};$script:count++}
function Reject([scriptblock]$Work){try{& $Work}catch{$script:count++;return};throw 'Unsafe drawer history profile accepted.'}
Require (-not (Test-DraftDrawerHistoryEnabled @{}))
foreach($value in @('true',1,$null)){Reject {Test-DraftDrawerHistoryEnabled @{drawerHistoryEnabled=$value}}}
foreach($case in @(@('Restart',$false,0),@('Validate',$false,0),@('Start',$true,0),@('Start',$false,1))){
 $state=@{processes=@{}};if($case[2]){$state.processes.POS=@{}}
 Reject {Enable-DraftDrawerHistory $state $case[0] $case[1]}
 Require (-not (Test-DraftDrawerHistoryEnabled $state))
}
$state=@{processes=@{};paymentMethod='cash_manual';omiseTestCheckoutEnabled=$true;branchPromptPayQrEnabled=$true}
Enable-DraftDrawerHistory $state Start $false
$roundtrip=$state|ConvertTo-Json|ConvertFrom-Json -AsHashtable
Require (Test-DraftDrawerHistoryEnabled $roundtrip)
Require ($roundtrip.paymentMethod -ceq 'cash_manual' -and $roundtrip.omiseTestCheckoutEnabled -and $roundtrip.branchPromptPayQrEnabled)
$settings=Get-DraftDrawerServiceSettings POS $roundtrip
Require ($settings.DrawerIntents__Enabled -ceq 'false' -and $settings.DrawerIntents__HistoryEnabled -ceq 'true')
Require ((Get-DraftDrawerServiceSettings POS @{}).DrawerIntents__HistoryEnabled -ceq 'false')
foreach($service in @('Order','Payment','Bff','Restaurant')){Require ((Get-DraftDrawerServiceSettings $service $state).Count -eq 0)}
Write-Output "PASS: $script:count drawer history profile guards; no infrastructure, payment or device commands."
