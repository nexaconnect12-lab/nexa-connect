#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Init','Status','Start','Stop','Bundle')][string]$Action,
 [string]$PlanPath,[string]$HostAddress,[string]$DeviceAddress,[string]$InstallerDirectory,[string]$OutputDirectory,[string]$PreviousBundle,
 [switch]$ConfirmTestResources)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-terminal-helpers.ps1')
. (Join-Path $PSScriptRoot 'physical-pos-pilot-helpers.ps1')
if(-not $IsWindows){throw 'Use the development Windows PC for this private pilot.'}
if($Action -eq 'Init'){
    $id=[Guid]::NewGuid().ToString('N');$directory=Join-Path $root ".runstate/physical-pos/$id"
    New-Item -ItemType Directory $directory|Out-Null;Protect-DraftDirectory $directory
    @{version=1;runId=$id;hostAddress=$HostAddress;deviceAddress=$DeviceAddress;origin=$(if($HostAddress){"https://${HostAddress}:9443"}else{$null});deviceWindowsAccount=$null;certificatePath=$null;privateKeyPath=$null;clientCaPath=$null;hostTrustConfirmed=$false;deviceTrustConfirmed=$false;confirmFreshTestData=$false}|ConvertTo-Json|Set-Content (Join-Path $directory 'plan.json')
    Write-Output "Physical POS plan: $directory/plan.json"
    Write-Output 'Fill exact network, certificate and intended-account prerequisites. No listener, firewall, trust or existing pilot changed.';return
}
if(-not $PlanPath){throw 'Supply the plan printed by Init.'}
$PlanPath=Assert-TerminalPath $PlanPath
if($Action -eq 'Status'){& node (Join-Path $PSScriptRoot 'release/physical-pos.mjs') status $PlanPath;if($LASTEXITCODE -ne 0){throw 'Physical pilot status failed.'};return}
$plan=Read-PhysicalPosPlan $PlanPath
$run=Get-DraftRunDirectory $root $plan.runId
if($Action -eq 'Start'){
    if(-not $ConfirmTestResources){throw 'Start requires -ConfirmTestResources for the explicitly separate new pilot.'}
    Assert-PhysicalPosNetwork $plan
    if(-not(Test-Path (Join-Path $run 'state.json'))){
        & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Start -RunId $plan.runId -PhysicalPosPlan $PlanPath -EnableMedia -EnableBranchPromptPayQr -ConfirmTestResources
    }else{
        $state=Get-Content (Join-Path $run 'state.json') -Raw|ConvertFrom-Json -AsHashtable
        if(-not $state.Contains('physicalPosPilot') -or $state.physicalPosPilot.planHash -cne (Get-FileHash $PlanPath).Hash){throw 'Refusing to adopt an existing unrelated run.'}
        & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Start -RunId $plan.runId -EnableMedia -ConfirmTestResources
    }
    & (Join-Path $PSScriptRoot 'provision-first-draft.ps1') -RunId $plan.runId -ConfirmTestResources
    Write-Output 'Separate LAN test branch/menu/terminal provisioned. Build a fresh installer, then use Bundle. No device installation or financial transaction performed.';return
}
$state=Get-Content (Join-Path $run 'state.json') -Raw|ConvertFrom-Json -AsHashtable
if(-not $state.Contains('physicalPosPilot') -or $state.physicalPosPilot.planHash -cne (Get-FileHash $PlanPath).Hash){throw 'Refusing to adopt unrelated pilot state.'}
if($Action -eq 'Stop'){
    if(Get-CimInstance Win32_Process -Filter "Name = 'NexaConnect.POS.exe'"){throw 'Finish work and close local POS first; also close the physical POS before stopping its backend.'}
    & (Join-Path $PSScriptRoot 'first-draft-stack.ps1') -Action Stop -RunId $plan.runId
    return
}
if(-not $InstallerDirectory -or -not $OutputDirectory){throw 'Bundle requires a freshly built installer directory and a new output directory.'}
$bundleLock=Open-DraftLifecycleLock $run
try {
if((Get-DraftQrPilotArtifactHash $run) -cne $state.publishedArtifacts -or (Get-PhysicalPosEdgeHash (Join-Path $run 'apps/StaffEdge')) -cne $state.physicalPosPilot.edgeArtifactsHash){throw 'Published backend/staff-edge artifacts changed; rebuild and verify before bundling.'}
$caPath=Assert-TerminalPath ([string]$plan.clientCaPath)
$ca=[Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.File]::ReadAllBytes($caPath))
try{if($ca.HasPrivateKey -or $ca.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow -or -not @($ca.Extensions|Where-Object {$_.Oid.Value -eq '2.5.29.19' -and $_.CertificateAuthority}).Count){throw 'A valid public CA certificate is required.'}}finally{$ca.Dispose()}
$argsList=@((Join-Path $PSScriptRoot 'release/physical-pos.mjs'),'bundle',$PlanPath,(Join-Path $run 'state.json'),(Join-Path $run 'pos-configuration.json'),(Assert-TerminalPath $InstallerDirectory),(Assert-TerminalPath $OutputDirectory))
if($PreviousBundle){$argsList+=Assert-TerminalPath $PreviousBundle}
& node @argsList
if($LASTEXITCODE -ne 0){throw 'Onboarding bundle preparation failed; no installation performed.'}
Protect-DraftDirectory $OutputDirectory
} finally {$bundleLock.Dispose()}
