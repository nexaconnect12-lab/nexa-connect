#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Prepare','Record','Defect','Resolve','Report')][string]$Action,
    [string]$AcceptanceId,[string]$RunId,[string]$PilotId,
    [string]$InstallDirectory=(Join-Path $env:LOCALAPPDATA 'NexaConnect/DraftTerminal'),
    [string]$CaseId,[ValidateSet('passed','failed','blocked')][string]$Status,[string]$EvidenceId,
    [ValidateSet('financial','security','data-loss','unrecoverable','usability')][string]$Category,
    [ValidateSet('blocker','high','minor')][string]$Severity,[string]$DefectId,[switch]$ConfirmObserved
)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Use the current Windows pilot account.'}
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/first-draft-terminal-helpers.ps1"
. "$PSScriptRoot/first-draft-uat-helpers.ps1"
. "$PSScriptRoot/pos-acceptance-helpers.ps1"
function Read-PosJson([string]$Path){
    $safe=Assert-TerminalPath $Path
    if((Get-Item -LiteralPath $safe).Length -gt 4MB){throw 'Acceptance input exceeds its bound.'}
    ConvertFrom-UatJson (Get-Content -LiteralPath $safe -Raw)
}
function Get-InstalledBinding([string]$Draft,[string]$Pilot,[string]$Install){
    Assert-UatId $Draft;Assert-UatId $Pilot
    $run=Assert-TerminalPath (Get-DraftRunDirectory $root $Draft)
    $lock=Open-DraftLifecycleLock $run
    try{
        $state=Read-PosJson "$run/state.json"
        if($state.runId -cne $Draft -or $state.project -cne "nexa-draft-$Draft" -or $state.status -cne 'Ready' -or -not $state.provisionedBranch){throw 'An owned provisioned Ready pilot is required.'}
        Assert-DraftUrls $state.urls
        $install=Assert-TerminalPath $Install
        Assert-TerminalInstallOwnership $install
        $active=Read-PosJson "$install/active.json";Assert-UatId $active.releaseId
        $packagePath=Assert-TerminalPath "$install/releases/$($active.releaseId)"
        $package=Read-TerminalPackage $packagePath
        if($package.runId -cne $Draft -or $package.releaseId -cne $active.releaseId){throw 'Installed pilot ownership changed.'}
        $configuration=Read-PosJson "$run/pos-configuration.json"
        $installed=Read-PosJson "$packagePath/appsettings.json"
        Assert-TerminalScopeEqual $package.scope (Get-TerminalScope $configuration)
        foreach($pair in @(@('PosApi','POS'),@('OrderApi','Order'),@('CatalogApi','Catalog'),@('PaymentApi','Payment'),@('RestaurantApi','Restaurant'),@('MediaApi','Media'))){
            if($installed.Services.Contains($pair[0]) -and ([uri]$installed.Services[$pair[0]]).AbsoluteUri -cne ([uri]$state.urls[$pair[1]]).AbsoluteUri){throw 'Installed service endpoint differs from pilot.'}
        }
        if($installed.Identity.Authority -cne $state.authority -or $installed.Pos.PaymentMethod -cne $state.paymentMethod -or $installed.Pos.EnableOmiseTestCheckout -cne $state.omiseTestCheckoutEnabled -or $installed.Pos.UseBranchPromptPayQr -cne $state.branchPromptPayQrEnabled){throw 'Installed profile differs from pilot.'}
        foreach($name in $state.processes.Keys){
            $record=$state.processes[$name]
            if(-not(Assert-DraftProcess $record (Get-Process -Id $record.id -ErrorAction SilentlyContinue))){throw 'Owned pilot service is not running.'}
            $expected=Join-Path $run "apps/$name/$($record.assemblyName).dll"
            $actual=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.id)"
            if($actual.ExecutablePath -cne (Get-Command dotnet).Source -or $actual.CommandLine -notlike "*`"$expected`"*" -or -not @(Get-NetTCPConnection -State Listen -LocalPort ([uri]$state.urls[$name]).Port -ErrorAction SilentlyContinue|Where-Object OwningProcess -eq $record.id).Count){throw 'Owned pilot executable/listener changed.'}
        }
        if($state.processes.Count -ne $state.urls.Count){throw 'Pilot process inventory is incomplete.'}
        $artifacts=Get-DraftQrPilotArtifactHash $run
        if($artifacts -cne $state.qrPilotArtifacts){throw 'Published pilot inventory changed.'}
        $pilotPath=Assert-TerminalPath "$root/.runstate/qr7/$Pilot"
        $plan=Read-PosJson "$pilotPath/plan.json";$activation=Read-PosJson "$pilotPath/activation.json"
        if($plan.id -cne $Pilot -or $plan.stackRunId -cne $Draft -or $activation.stackRunId -cne $Draft -or $activation.backendArtifacts -cne $artifacts){throw 'QR activation differs from pilot.'}
        if($plan.publicOrigin -cne $state.qrPilotOrigin){throw 'QR origin differs from pilot.'}
        foreach($key in @('OrganizationId','RestaurantId','BranchId','StoreId','TerminalId')){if($activation.scope[$key] -cne $package.scope[$key]){throw 'QR scope differs from installed terminal.'}}
        $reportJson=& node "$PSScriptRoot/release/qr7.mjs" report $pilotPath 2>$null
        if($LASTEXITCODE){throw 'QR binding validation failed.'}
        $qr=ConvertFrom-UatJson ($reportJson -join "`n")
        if(-not $qr.configured -or @($qr.prerequisites|Where-Object {$_ -cne 'fresh-matching-candidate'}).Count){throw 'QR configured files/prerequisites changed.'}
        # A historical deployed target is intentional. This never claims fresh QR9/current-source verification.
        $profile=[ordered]@{paymentMethod=$state.paymentMethod;omise=$state.omiseTestCheckoutEnabled;promptPay=$state.branchPromptPayQrEnabled;media=$state.mediaEnabled;history=$state.drawerHistoryEnabled;authority=$state.authority;urls=$state.urls}
        return @{snapshot=[ordered]@{runId=$Draft;releaseId=$package.releaseId;pilotId=$Pilot;
            activeManifestHash=(Get-FileHash "$install/active.json").Hash.ToLowerInvariant();
            packageManifestHash=(Get-FileHash "$packagePath/terminal-package.json").Hash.ToLowerInvariant();
            configurationHash=(Get-FileHash "$run/pos-configuration.json").Hash.ToLowerInvariant();backendArtifacts=$artifacts;
            qrActivationHash=(Get-FileHash "$pilotPath/activation.json").Hash.ToLowerInvariant();profileHash=(Get-PosAcceptanceHash $profile)};
            qrCandidateFresh=($qr.prerequisites.Count -eq 0)}
    }finally{$lock.Dispose()}
}
if($Action -ceq 'Prepare'){
    $binding=Get-InstalledBinding $RunId $PilotId $InstallDirectory
    $AcceptanceId=[guid]::NewGuid().ToString('N')
    $directory=Assert-TerminalPath "$root/.runstate/pos-acceptance/$AcceptanceId"
    New-Item -ItemType Directory -Path $directory|Out-Null;Protect-DraftDirectory $directory
    $record=@{version=1;id=$AcceptanceId;kind='installed-pos-operator-observations';installDirectory=(Assert-TerminalPath $InstallDirectory);binding=$binding.snapshot;events=@()}
    Write-PosAcceptanceRecord $directory $record
    Write-Output "POS acceptance prepared: $AcceptanceId (all 17 cases pending)"
    return
}
Assert-UatId $AcceptanceId
$directory=Assert-TerminalPath "$root/.runstate/pos-acceptance/$AcceptanceId"
$lock=Open-DraftLifecycleLock $directory
try{
    $record=Read-PosJson "$directory/record.json";Assert-PosAcceptanceRecord $record
    if($record.id -cne $AcceptanceId){throw 'Acceptance ownership mismatch.'}
    $valid=$false;$fresh=$false
    try{$live=Get-InstalledBinding $record.binding.runId $record.binding.pilotId $record.installDirectory;Assert-PosAcceptanceSnapshotEqual $record.binding $live.snapshot;$valid=$true;$fresh=$live.qrCandidateFresh}catch{}
    if($Action -ceq 'Report'){
        $report=Get-PosAcceptanceReport $record $valid $fresh
        $report|ConvertTo-Json -Depth 12|Set-Content (Assert-TerminalPath "$directory/report.json")
        $report|ConvertTo-Json -Depth 12;return
    }
    if(-not $ConfirmObserved){throw 'ConfirmObserved is required for operator observations; source tests are separate.'}
    Assert-UatId $EvidenceId
    if($CaseId -cnotin (Get-PosAcceptanceCases)){throw 'Select a documented POS acceptance case.'}
    $event=@{caseId=$CaseId;evidenceId=$EvidenceId;observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
    if($Action -ceq 'Record'){
        if($Status -cnotin @('passed','failed','blocked')){throw 'Select an observed result.'}
        $event.type='observe';$event.status=$Status;$event.bindingValid=$valid
    }elseif($Action -ceq 'Defect'){
        $event.type='defect';$event.defectId=[guid]::NewGuid().ToString('N');$event.category=$Category;$event.severity=$Severity
    }else{$event.type='resolve';$event.defectId=$DefectId}
    $updated=Add-PosAcceptanceEvent $record $event
    Write-PosAcceptanceRecord $directory $updated
    if($event.type -ceq 'defect'){Write-Output "POS defect: $($event.defectId)"}
    else{Write-Output 'Operator observation recorded; installed-target review remains separate from release readiness.'}
}finally{$lock.Dispose()}
