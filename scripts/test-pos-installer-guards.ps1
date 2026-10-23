#requires -Version 7.4
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$checks=0
foreach($file in @('package-pos-installer.ps1','test-pos-installer-guards.ps1')){
    $tokens=$null;$parseErrors=$null
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$parseErrors)
    if($parseErrors){throw 'Installer tooling syntax failed.'};$checks++
}
$iss=Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/windows-pos/NexaConnect.POS.iss') -Raw
foreach($required in @('PrivilegesRequired=lowest','CloseApplications=no','RestartApplications=no','AppMutex=NexaConnect.POS.SingleInstance',"CreateMutex('NexaConnect.POS.Maintenance')",'Flags: external ignoreversion uninsneveruninstall','--validate-package','GetVersionNumbers','TAKEOVERCALLBACK','installer-owner.txt')){
    if(-not $iss.Contains($required)){throw 'A required installer custody/maintenance guard is absent.'};$checks++
}
if($iss -match '(?im)^\[UninstallDelete\]|^\[InstallDelete\]|^\[Run\]'){throw 'Installer must not delete recovery, auto-start POS or silently close operational work.'};$checks++
$builder=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-pos-installer.ps1') -Raw
if(-not $builder.Contains('--self-contained true') -or -not $builder.Contains('installerTemplateHash') -or -not $builder.Contains('Assert-TerminalCandidateUnchanged')){throw 'Installer runtime/source binding is incomplete.'};$checks++
$profile=Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/windows-pos/profile.example.json') -Raw | ConvertFrom-Json
if($profile.Identity.ClientId -cne 'nexaconnect-pos' -or $profile.Identity.RedirectUri -cne 'nexaconnect-pos://oauth/callback' -or $profile.Pos.EnableOmiseTestCheckout){throw 'Example onboarding must remain public-client/manual only.'};$checks++
Write-Output "$checks installer tooling guards passed. No installation or registry mutation performed."
