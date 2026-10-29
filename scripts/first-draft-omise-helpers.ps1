# Pure local sandbox profile/configuration helpers. Never print key material.
function Assert-DraftOmiseTestKey([string]$Key) {
    if($Key -cnotmatch '\Askey_test_[a-z0-9]{10,64}\z'){throw 'A valid Omise test secret is required; live keys and whitespace are rejected.'}
}
function Test-DraftOmiseEnabled($State) {
    if($State.Contains('omiseTestCheckoutEnabled') -and $State.omiseTestCheckoutEnabled -isnot [bool]){throw 'Omise sandbox selection must be Boolean.'}
    return $State.Contains('omiseTestCheckoutEnabled') -and $State.omiseTestCheckoutEnabled
}
function Remove-DraftOmiseInputEnvironment {
    $previous=@{}
    foreach($name in @([Environment]::GetEnvironmentVariables('Process').Keys)){
        if($name -match '^NEXACONNECT_(JOINED_)?OMISE_|^PaymentProvider__OmiseSecretKey$'){
            $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
            [Environment]::SetEnvironmentVariable($name,$null,'Process')
        }
    }
    return $previous
}
function Enable-DraftOmiseProfile($State,$Secrets,[string]$Action,[bool]$NoBuild,[string]$Key) {
    if($Action -cne 'Start' -or $State.processes.Count -or $NoBuild){throw 'Omise activation requires a stopped draft and build-enabled Start.'}
    Assert-DraftOmiseTestKey $Key
    if($Secrets.Contains('OMISE_TEST_SECRET') -and $Secrets.OMISE_TEST_SECRET -cne $Key){throw 'Provider key rotation is a separate recovery procedure.'}
    $Secrets.OMISE_TEST_SECRET=$Key
    $State.omiseTestCheckoutEnabled=$true
}
function Get-DraftOmiseServiceSettings([string]$Name,$State,$Secrets) {
    if(-not (Test-DraftOmiseEnabled $State)){return @{}}
    if(-not $Secrets.Contains('OMISE_TEST_SECRET')){throw 'Restore the protected Omise test credential before starting the enabled profile.'}
    Assert-DraftOmiseTestKey $Secrets.OMISE_TEST_SECRET
    if($Name -ceq 'Order'){return @{CardCheckout__EnableOmiseTestCheckout='true'}}
    if($Name -ceq 'Payment'){
        return @{PaymentProvider__Adapter='Omise';PaymentProvider__BaseUrl='https://api.omise.co/';PaymentProvider__OmiseSecretKey=$Secrets.OMISE_TEST_SECRET;
            PaymentProvider__AuthorizationRecoveryEnabled='true';PaymentProvider__CaptureRecoveryEnabled='true';PaymentProvider__RefundRecoveryEnabled='true'}
    }
    return @{}
}
