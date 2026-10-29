#requires -Version 7.4
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-omise-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-terminal-helpers.ps1')
$script:passed=0
function Require([bool]$Condition){if(-not $Condition){throw 'Sandbox profile guard assertion failed.'};$script:passed++}
function Reject([scriptblock]$Work){try{& $Work}catch{$script:passed++;return};throw 'Unsafe sandbox profile was accepted.'}
$key='skey_test_'+('a'*16)
Require (-not (Test-DraftOmiseEnabled @{}))
Reject {Test-DraftOmiseEnabled @{omiseTestCheckoutEnabled='true'}}
foreach($invalid in @('',('skey_live_'+('a'*16)),($key+"`n"),(' '+$key))){Reject {Assert-DraftOmiseTestKey $invalid}}
foreach($case in @(@('Restart',$false,0),@('Start',$true,0),@('Start',$false,1))){
    $state=@{processes=@{}};if($case[2]){$state.processes.Order=@{}}
    Reject {Enable-DraftOmiseProfile $state @{} $case[0] $case[1] $key}
    Require (-not $state.Contains('omiseTestCheckoutEnabled'))
}
$state=@{processes=@{}};$secrets=@{}
Enable-DraftOmiseProfile $state $secrets Start $false $key
Require (Test-DraftOmiseEnabled $state)
Require ($secrets.OMISE_TEST_SECRET -ceq $key)
Reject {Enable-DraftOmiseProfile $state $secrets Start $false ('skey_test_'+('b'*16))}
Reject {Get-DraftOmiseServiceSettings Payment $state @{}}
foreach($name in @('POS','Bff','Catalog','Media','Restaurant','Reporting','PlatformDirectory')){
    Require ((Get-DraftOmiseServiceSettings $name $state $secrets).Count -eq 0)
}
$order=Get-DraftOmiseServiceSettings Order $state $secrets
Require ($order.Count -eq 1 -and $order.CardCheckout__EnableOmiseTestCheckout -ceq 'true')
$payment=Get-DraftOmiseServiceSettings Payment $state $secrets
Require ($payment.PaymentProvider__Adapter -ceq 'Omise' -and $payment.PaymentProvider__BaseUrl -ceq 'https://api.omise.co/')
Require ($payment.PaymentProvider__OmiseSecretKey -ceq $key)
Require ($payment.PaymentProvider__AuthorizationRecoveryEnabled -ceq 'true' -and $payment.PaymentProvider__CaptureRecoveryEnabled -ceq 'true')
Require (-not $state.Contains('OMISE_TEST_SECRET'))
$config=@{Identity=@{Authority=('http://127.0.0.1:8080/realms/nexa-review-it-'+('a'*32));ClientId='nexaconnect-pos';RedirectUri='nexaconnect-pos://oauth/callback'};
    Services=@{PosApi='http://127.0.0.1:5001/';OrderApi='http://127.0.0.1:5002/';CatalogApi='http://127.0.0.1:5003/';PaymentApi='http://127.0.0.1:5004/'};
    Pos=@{Currency='THB';PaymentMethod='cash_manual';EnableOmiseTestCheckout=$false}}
foreach($name in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){$config.Pos[$name]=[guid]::NewGuid().ToString()}
$before=Get-TerminalScope $config
$config.Pos.EnableOmiseTestCheckout=$true
$after=Get-TerminalScope $config
Assert-TerminalScopeEqual $before $after
Require ($after.PaymentMethod -ceq 'cash_manual')
$config.Services.PaymentApi='https://provider.example.com/'
Reject {Get-TerminalScope $config}
$config.Services.PaymentApi='http://127.0.0.1:5004/';$config.Pos.EnableOmiseTestCheckout='true'
Reject {Get-TerminalScope $config}
foreach($name in @('NEXACONNECT_OMISE_TEST_SECRET_KEY','NEXACONNECT_JOINED_OMISE_TEST_SECRET','PaymentProvider__OmiseSecretKey')){
    $original=[Environment]::GetEnvironmentVariable($name,'Process')
    try{
        [Environment]::SetEnvironmentVariable($name,$key,'Process')
        $removed=Remove-DraftOmiseInputEnvironment
        Require ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name,'Process')))
        Restore-DraftEnvironment $removed
        Require ([Environment]::GetEnvironmentVariable($name,'Process') -ceq $key)
    }finally{[Environment]::SetEnvironmentVariable($name,$original,'Process')}
}
$beforeInfra=[Environment]::GetEnvironmentVariable('NEXACONNECT_JOINED_OMISE_TEST_SECRET','Process')
try{
    [Environment]::SetEnvironmentVariable('NEXACONNECT_JOINED_OMISE_TEST_SECRET',$null,'Process')
    $restoreInfra=Set-DraftComposeEnvironment @{OMISE_TEST_SECRET=$key} ('a'*32) 8443
    try{Require ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('NEXACONNECT_JOINED_OMISE_TEST_SECRET','Process')))}
    finally{Restore-DraftEnvironment $restoreInfra}
}finally{[Environment]::SetEnvironmentVariable('NEXACONNECT_JOINED_OMISE_TEST_SECRET',$beforeInfra,'Process')}
Write-Output "PASS: $script:passed sandbox profile guards; no infrastructure or provider requests."
