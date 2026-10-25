# Local draft package integrity and scope checks. Hashes detect drift; they are not signatures.
Set-StrictMode -Version Latest
function Assert-TerminalCandidateUnchanged($Before,$After) {
    if($Before.dirty -isnot [bool] -or $After.dirty -isnot [bool] -or $Before.revision -cne $After.revision -or
       $Before.inputHash -cne $After.inputHash -or $Before.dirty -ne $After.dirty){throw 'Source identity changed during packaging; package cannot be handed over.'}
}
function Assert-TerminalPath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    $cursor=$full
    while($cursor){
        if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Terminal paths must not traverse links or junctions.'}
        $parent=Split-Path $cursor -Parent
        if($parent -eq $cursor){break};$cursor=$parent
    }
    return $full
}
function Get-TerminalScope($Configuration) {
    $pos=$Configuration.Pos;$identity=$Configuration.Identity
    if($identity.ClientId -cne 'nexaconnect-pos' -or $identity.RedirectUri -cne 'nexaconnect-pos://oauth/callback'){throw 'Draft POS requires the public client and installed callback.'}
    $scope=[ordered]@{}
    foreach($name in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){
        $id=[Guid]::Empty
        if(-not [Guid]::TryParse([string]$pos.$name,[ref]$id) -or $id -eq [Guid]::Empty){throw 'Provision the complete terminal scope before packaging.'}
        $scope[$name]=$id.ToString('D')
    }
    if($pos.Currency -cne 'THB' -or $pos.PaymentMethod -cnotin @('cash_manual','promptpay_manual') -or $pos.EnableOmiseTestCheckout){throw 'Draft terminal requires THB manual payment with card checkout disabled.'}
    foreach($value in @($identity.Authority,$Configuration.Services.PosApi,$Configuration.Services.OrderApi,$Configuration.Services.CatalogApi)){
        $uri=$null
        if(-not [uri]::TryCreate([string]$value,[UriKind]::Absolute,[ref]$uri) -or -not $uri.IsLoopback -or $uri.Scheme -notin @('http','https') -or $uri.UserInfo -or $uri.Query -or $uri.Fragment){throw 'This package supports only the owned loopback draft.'}
    }
    $scope.Authority=[string]$identity.Authority;$scope.ClientId=[string]$identity.ClientId
    $scope.Currency=[string]$pos.Currency;$scope.PaymentMethod=[string]$pos.PaymentMethod
    $hasPilotSetting=if($pos -is [System.Collections.IDictionary]){$pos.Contains('UseIsolatedLocalPilotStorage')}else{$null -ne $pos.PSObject.Properties['UseIsolatedLocalPilotStorage']}
    if($hasPilotSetting){
        if($pos.UseIsolatedLocalPilotStorage -isnot [bool]){throw 'Pilot storage selection must be Boolean.'}
        if($pos.UseIsolatedLocalPilotStorage){$scope.UseIsolatedLocalPilotStorage='true'}
    }
    return $scope
}
function Assert-TerminalInstallOwnership([string]$Directory) {
    if(-not (Test-Path -LiteralPath $Directory)){return}
    $marker=Join-Path $Directory 'terminal-install-owner.json'
    if(-not (Test-Path -LiteralPath $marker)){
        if(@(Get-ChildItem -LiteralPath $Directory -Force).Count){throw 'Refusing a nonempty directory not owned by the draft terminal installer.'}
        return
    }
    $owner=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if($owner.kind -cne 'nexaconnect-draft-terminal' -or $owner.id -cnotmatch '^[a-f0-9]{32}$'){throw 'Invalid terminal installation ownership.'}
}
function Assert-TerminalRecoverySeparation([string]$Install,[string]$LocalApplicationData) {
    $target=[IO.Path]::GetFullPath($Install)
    foreach($name in @('POS','POS-Pilots')){
        $recovery=[IO.Path]::GetFullPath((Join-Path $LocalApplicationData "NexaConnect/$name"))
        if($target -eq $recovery -or $target.StartsWith($recovery+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
            $recovery.StartsWith($target+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Install directory must be separate from protected POS recovery.'}
    }
}
function Assert-TerminalScopeEqual($Expected,$Actual) {
    if($Expected.Count -ne $Actual.Count){throw 'Terminal scope inventory changed.'}
    foreach($name in $Expected.Keys){if([string]$Expected[$name] -cne [string]$Actual[$name]){throw 'Terminal update changes retained scope, identity realm or tender. Preserve recovery and restore the original configuration.'}}
}
function Get-TerminalFileHashes([string]$Directory) {
    $hashes=[ordered]@{}
    foreach($entry in Get-ChildItem -LiteralPath $Directory -Recurse -Force){[void](Assert-TerminalPath $entry.FullName)}
    foreach($file in Get-ChildItem -LiteralPath $Directory -File -Recurse -Force | Sort-Object FullName){
        [void](Assert-TerminalPath $file.FullName)
        $relative=[IO.Path]::GetRelativePath($Directory,$file.FullName).Replace('\','/')
        if($relative -eq 'terminal-package.json'){continue}
        $hashes[$relative]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $hashes
}
function Read-TerminalPackage([string]$Directory) {
    [void](Assert-TerminalPath $Directory)
    $manifest=Get-Content -LiteralPath (Join-Path $Directory 'terminal-package.json') -Raw | ConvertFrom-Json -AsHashtable
    if($manifest.format -ne 1 -or $manifest.releaseId -cnotmatch '^[a-f0-9]{32}$' -or $manifest.runId -cnotmatch '^[a-f0-9]{32}$'){throw 'Invalid terminal package identity.'}
    $hashes=Get-TerminalFileHashes $Directory
    if($hashes.Count -ne $manifest.files.Count){throw 'Terminal package file inventory changed.'}
    foreach($key in $hashes.Keys){if($hashes[$key] -cne $manifest.files[$key]){throw 'Terminal package content changed; rebuild it from reviewed source.'}}
    if(-not $hashes.Contains('NexaConnect.POS.exe') -or -not $hashes.Contains('appsettings.json')){throw 'Terminal package is incomplete.'}
    $config=Get-Content -LiteralPath (Join-Path $Directory 'appsettings.json') -Raw | ConvertFrom-Json
    Assert-TerminalScopeEqual $manifest.scope (Get-TerminalScope $config)
    return $manifest
}
