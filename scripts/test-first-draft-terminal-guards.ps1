#requires -Version 7.4
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-terminal-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('nexa-terminal-it-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$checks=0
function Rejected([scriptblock]$Action){$rejected=$false;try{& $Action}catch{$rejected=$true};if(-not $rejected){throw 'Expected terminal guard rejection.'};$script:checks++}
try{
    foreach($scriptName in @('first-draft-terminal.ps1','first-draft-terminal-helpers.ps1','test-first-draft-terminal-guards.ps1','test-first-draft-checkout.ps1')){
        $tokens=$null;$parseErrors=$null
        [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $scriptName),[ref]$tokens,[ref]$parseErrors)
        if($parseErrors){throw "Terminal script syntax failed: $scriptName"};$checks++
    }
    # Exercise actual Git's empty clean-status output, not a fabricated status string.
    $source=Join-Path $testRoot 'source'
    New-Item -ItemType Directory -Path (Join-Path $source 'scripts') -Force|Out-Null
    Set-Content (Join-Path $source 'scripts/input.txt') 'synthetic input'
    & git -C $source init -q
    & git -C $source add scripts/input.txt
    & git -C $source -c user.name='NexaConnect Guard' -c user.email='guard@nexa.invalid' commit -q -m 'Synthetic source fixture'
    if($LASTEXITCODE -ne 0){throw 'Unable to create local source fixture.'}
    $clean=Get-DraftCandidate $source
    if($clean.dirty -isnot [bool] -or $clean.dirty){throw 'Clean Git source was marked dirty.'};$checks++
    Assert-TerminalCandidateUnchanged $clean (Get-DraftCandidate $source);$checks++
    Add-Content (Join-Path $source 'scripts/input.txt') 'changed input'
    $changed=Get-DraftCandidate $source
    Rejected {Assert-TerminalCandidateUnchanged $clean $changed}
    Assert-TerminalCandidateUnchanged $changed (Get-DraftCandidate $source);$checks++
    $invalid=@{revision=$clean.revision;inputHash=$clean.inputHash;dirty='false'}
    Rejected {Assert-TerminalCandidateUnchanged $clean $invalid}
    $invalid=@{revision=$clean.revision;inputHash=$clean.inputHash;dirty=$true}
    Rejected {Assert-TerminalCandidateUnchanged $clean $invalid}
    $configuration=@{Identity=@{Authority='http://127.0.0.1:18500/realms/nexa-review-it-test';ClientId='nexaconnect-pos';RedirectUri='nexaconnect-pos://oauth/callback'};Services=@{PosApi='http://127.0.0.1:18501/';OrderApi='http://127.0.0.1:18502/';CatalogApi='http://127.0.0.1:18503/'};Pos=@{OrganizationId=[Guid]::NewGuid();RestaurantId=[Guid]::NewGuid();BranchId=[Guid]::NewGuid();StoreId=[Guid]::NewGuid();TerminalId=[Guid]::NewGuid();Currency='THB';PaymentMethod='cash_manual';EnableOmiseTestCheckout=$false}}
    $scope=Get-TerminalScope $configuration
    Assert-TerminalScopeEqual $scope (Get-TerminalScope $configuration);$checks++
    $configuration.Pos.UseIsolatedLocalPilotStorage=$true
    $isolatedScope=Get-TerminalScope $configuration
    Rejected {Assert-TerminalScopeEqual $scope $isolatedScope}
    Assert-TerminalScopeEqual $isolatedScope (Get-TerminalScope $configuration);$checks++
    $jsonConfiguration=$configuration|ConvertTo-Json -Depth 5|ConvertFrom-Json
    Assert-TerminalScopeEqual $isolatedScope (Get-TerminalScope $jsonConfiguration);$checks++
    $jsonConfiguration.Pos.UseIsolatedLocalPilotStorage=$false
    Assert-TerminalScopeEqual $scope (Get-TerminalScope $jsonConfiguration);$checks++
    $jsonConfiguration.Pos.UseIsolatedLocalPilotStorage='true'
    Rejected {Get-TerminalScope $jsonConfiguration}
    $configuration.Pos.UseIsolatedLocalPilotStorage='true'
    Rejected {Get-TerminalScope $configuration}
    $configuration.Pos.Remove('UseIsolatedLocalPilotStorage')
    foreach($relative in @('NexaConnect/POS','NexaConnect/POS-Pilots','NexaConnect/POS-Pilots/profile/releases','NexaConnect')){
        Rejected {Assert-TerminalRecoverySeparation (Join-Path $testRoot $relative) $testRoot}
    }
    Assert-TerminalRecoverySeparation (Join-Path $testRoot 'NexaConnect/DraftTerminal') $testRoot;$checks++
    $configuration.Pos.TerminalId=[Guid]::NewGuid()
    Rejected {Assert-TerminalScopeEqual $scope (Get-TerminalScope $configuration)}
    $configuration.Pos.TerminalId=$scope.TerminalId
    $configuration.Identity.Authority='http://127.0.0.1:18500/realms/another-run'
    Rejected {Assert-TerminalScopeEqual $scope (Get-TerminalScope $configuration)}
    $configuration.Identity.Authority=$scope.Authority
    $configuration.Pos.EnableOmiseTestCheckout=$true;Rejected {Get-TerminalScope $configuration};$configuration.Pos.EnableOmiseTestCheckout=$false
    $configuration.Pos.Currency='SGD';Rejected {Get-TerminalScope $configuration};$configuration.Pos.Currency='THB'
    $configuration.Services.PosApi='http://remote.example.invalid/';Rejected {Get-TerminalScope $configuration};$configuration.Services.PosApi='http://127.0.0.1:18501/'
    $configuration.Identity.RedirectUri='https://example.invalid/';Rejected {Get-TerminalScope $configuration};$configuration.Identity.RedirectUri='nexaconnect-pos://oauth/callback'
    $configuration|ConvertTo-Json -Depth 8|Set-Content (Join-Path $testRoot 'appsettings.json')
    Set-Content (Join-Path $testRoot 'NexaConnect.POS.exe') 'guard fixture; never execute'
    @{format=1;releaseId=[Guid]::NewGuid().ToString('N');runId=[Guid]::NewGuid().ToString('N');scope=$scope;files=(Get-TerminalFileHashes $testRoot)}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $testRoot 'terminal-package.json')
    [void](Read-TerminalPackage $testRoot);$checks++
    Add-Content (Join-Path $testRoot 'NexaConnect.POS.exe') 'changed';Rejected {Read-TerminalPackage $testRoot}
    Set-Content (Join-Path $testRoot 'extra.txt') 'unexpected';Rejected {Read-TerminalPackage $testRoot}
    Rejected {Assert-TerminalInstallOwnership $testRoot}
    @{kind='unrelated';id=[Guid]::NewGuid().ToString('N')}|ConvertTo-Json|Set-Content (Join-Path $testRoot 'terminal-install-owner.json')
    Rejected {Assert-TerminalInstallOwnership $testRoot}
    @{kind='nexaconnect-draft-terminal';id=[Guid]::NewGuid().ToString('N')}|ConvertTo-Json|Set-Content (Join-Path $testRoot 'terminal-install-owner.json')
    Assert-TerminalInstallOwnership $testRoot;$checks++
    $realmPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'docker/keycloak/realm/nexa-dev-realm.json'
    $realm=[regex]::Replace((Get-Content -LiteralPath $realmPath -Raw),'(:\s*)\$\{[A-Z0-9_]+\}','${1}false')|ConvertFrom-Json
    $orderClient=$realm.clients|Where-Object clientId -eq 'nexaconnect-order-service'
    $mapper=@($orderClient.protocolMappers|Where-Object {$_.name -eq 'order-service-account-username'})
    if($mapper.Count -ne 1 -or $mapper[0].config.'claim.name' -cne 'preferred_username' -or $mapper[0].config.'user.attribute' -cne 'username' -or
        $mapper[0].config.'access.token.claim' -cne 'true' -or $mapper[0].config.'id.token.claim' -cne 'false' -or $mapper[0].config.'userinfo.token.claim' -cne 'false'){throw 'Order workload principal mapper is incomplete.'};$checks++
    Write-Output "$checks terminal package safety checks passed."
}finally{
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if((Split-Path $resolved -Leaf) -cnotmatch '^nexa-terminal-it-[a-f0-9]{32}$' -or (Split-Path $resolved -Parent) -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')){throw 'Unexpected fixture cleanup path.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
