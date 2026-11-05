#requires -Version 5.1
[CmdletBinding()]
param([string]$BundleDirectory=$PSScriptRoot,[string]$ExpectedManifestSha256)
$ErrorActionPreference='Stop'
try {
    $manifestPath=Join-Path $BundleDirectory 'onboarding.json'
    if(-not $ExpectedManifestSha256 -or $ExpectedManifestSha256 -cnotmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $ExpectedManifestSha256.ToLowerInvariant()){throw 'Supply the onboarding manifest SHA-256 obtained separately from the operator.'}
    $manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
    if($manifest.version -ne 1 -or $manifest.kind -cne 'physical-pos-onboarding'){throw 'Invalid bundle.'}
    $required=@('NexaConnect-POS-setup.exe','terminal-profile.json','client-ca.cer','Test-DeviceOnboarding.ps1','START-HERE.txt')
    if(@($manifest.files.PSObject.Properties).Count -ne $required.Count -or $manifest.privateUnsigned -isnot [bool]){throw 'Invalid bundle inventory.'}
    foreach($name in $required){$file=Join-Path $BundleDirectory $name;if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifest.files.$name){throw 'Bundle content changed; obtain a fresh copy.'}}
    $profile=Get-Content -LiteralPath (Join-Path $BundleDirectory 'terminal-profile.json') -Raw|ConvertFrom-Json
    $authority=[uri]$profile.Identity.Authority;$origin=$authority.GetLeftPart([UriPartial]::Authority)
    $ip=$null
    if(-not [Net.IPAddress]::TryParse($authority.Host,[ref]$ip) -or $ip.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or $authority.Scheme -cne 'https' -or $authority.Query -or $authority.Fragment -or $authority.UserInfo -or $authority.AbsolutePath -cnotmatch '^/realms/nexa-review-it-[a-f0-9]{32}$'){throw 'Invalid isolated LAN issuer.'}
    $b=$ip.GetAddressBytes();if(-not($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))){throw 'Private LAN origin required.'}
    foreach($name in @('PosApi','OrderApi','CatalogApi','PaymentApi','MediaApi')){if($profile.Services.$name -cne ($origin+'/')){throw 'Unexpected service origin.'}}
    if($profile.Pos.EnableOmiseTestCheckout -or $profile.Pos.PaymentMethod -cne 'cash_manual'){throw 'Manual pilot profile required.'}
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    if($null -ne [Net.ServicePointManager]::ServerCertificateValidationCallback){throw 'Remove custom certificate-validation bypasses before checking device trust.'}
    # Normal Windows trust validation; no certificate bypass and no authentication requests.
    $discovery=Invoke-RestMethod -Uri ($authority.AbsoluteUri+'/.well-known/openid-configuration') -Method Get -MaximumRedirection 0 -TimeoutSec 15
    if($discovery.issuer -cne $authority.AbsoluteUri -or @($discovery.code_challenge_methods_supported) -cnotcontains 'S256'){throw 'Unexpected OIDC issuer/PKCE policy.'}
    foreach($name in @('authorization_endpoint','token_endpoint','userinfo_endpoint','jwks_uri')){if(([uri]$discovery.$name).GetLeftPart([UriPartial]::Authority) -cne $origin){throw 'OIDC endpoint leaves the staff origin.'}}
    foreach($service in @('pos','order','catalog','payment','media')){
        $denied=$false
        try{Invoke-WebRequest -UseBasicParsing -Uri "$origin/api/$service/v1/onboarding-probe" -Method Get -MaximumRedirection 0 -TimeoutSec 15|Out-Null}
        catch{if($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401){$denied=$true}else{throw}}
        if(-not $denied){throw 'Anonymous API denial not verified.'}
    }
    $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $BundleDirectory 'NexaConnect-POS-setup.exe')
    if(-not $manifest.privateUnsigned -and $signature.Status -ne 'Valid'){throw 'Trusted installer signature required.'}
    Write-Output 'Bundle integrity, trusted HTTPS, exact OIDC issuer/PKCE and anonymous staff API denial passed. No sign-in or financial command performed.'
    if($manifest.privateUnsigned){Write-Output 'Private unsigned test package: hashes verify integrity, not publisher identity. Do not distribute publicly.'}
    Write-Output 'Next: finish work, close POS, then run setup as the intended ordinary Windows account and select terminal-profile.json.'
} catch {Write-Error 'Onboarding check failed. Check the separately supplied hash, network, certificate trust and new pilot status. No installation or financial action performed.';exit 1}
