#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Package','Install','Status','Launch')][string]$Action,
    [string]$RunId,
    [string]$PackageDirectory,
    [string]$InstallDirectory=(Join-Path $env:LOCALAPPDATA 'NexaConnect/DraftTerminal'),
    [switch]$RegisterProtocol
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-terminal-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
if(-not $IsWindows){throw 'Draft terminal packaging/install requires Windows.'}
if($Action -eq 'Package'){
    $run=Get-DraftRunDirectory $root $RunId
    $lock=Open-DraftLifecycleLock $run
    try{
        $state=Get-Content (Join-Path $run 'state.json') -Raw | ConvertFrom-Json
        if($state.runId -cne $RunId -or $state.status -ne 'Ready' -or -not $state.provisionedBranch){throw 'Package requires the provisioned Ready draft.'}
        $config=Get-Content (Join-Path $run 'pos-configuration.json') -Raw | ConvertFrom-Json
        $scope=Get-TerminalScope $config
        $candidate=Get-DraftCandidate $root
        $release=[Guid]::NewGuid().ToString('N')
        $destination=Join-Path $run "terminal-packages/$release"
        New-Item -ItemType Directory -Path $destination | Out-Null
        & dotnet publish (Join-Path $root 'src/Clients/NexaConnect.POS') -c Release -r win-x64 --self-contained false --artifacts-path (Join-Path $run "terminal-build/$release") --output $destination --verbosity quiet -m:1
        if($LASTEXITCODE -ne 0){throw 'Terminal publication failed; no install was activated.'}
        if($config.Pos.PromptPayQrImagePath){
            $qr=Assert-TerminalPath ([string]$config.Pos.PromptPayQrImagePath)
            if(-not (Test-Path -LiteralPath $qr -PathType Leaf)){throw 'Configured test QR image is missing.'}
            $leaf='draft-qr'+[IO.Path]::GetExtension($qr)
            Copy-Item -LiteralPath $qr -Destination (Join-Path $destination $leaf)
            $config.Pos.PromptPayQrImagePath=$leaf
        }
        $config | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $destination 'appsettings.json')
        $revision=(& git -C $root rev-parse HEAD).Trim()
        $dirty=([string](& git -C $root status --porcelain)).Length -gt 0
        if((Get-DraftCandidate $root).inputHash -cne $candidate.inputHash -or $revision -cne $candidate.revision){throw 'Executable inputs changed during packaging; package cannot be handed over.'}
        @{format=1;releaseId=$release;runId=$RunId;sourceRevision=$revision;sourceDirty=$dirty;sourceInputHash=$candidate.inputHash;runtime='win-x64 framework-dependent .NET 10 Windows Desktop';scope=$scope;files=(Get-TerminalFileHashes $destination)} |
            ConvertTo-Json -Depth 10 | Set-Content (Join-Path $destination 'terminal-package.json')
        [void](Read-TerminalPackage $destination)
        Write-Output "Draft terminal package: $destination"
    }finally{$lock.Dispose()}
    return
}
$install=Assert-TerminalPath $InstallDirectory
Assert-TerminalInstallOwnership $install
$recovery=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'NexaConnect/POS'))
if($install -eq $recovery -or $install.StartsWith($recovery+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
    $recovery.StartsWith($install+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Install directory must be separate from protected POS recovery.'}
if($Action -in @('Status','Launch')){
    $active=Get-Content (Join-Path $install 'active.json') -Raw | ConvertFrom-Json -AsHashtable
    if($active.releaseId -cnotmatch '^[a-f0-9]{32}$'){throw 'Invalid active terminal release.'}
    $manifest=Read-TerminalPackage (Join-Path $install "releases/$($active.releaseId)")
    $exe=Join-Path $install "releases/$($active.releaseId)/NexaConnect.POS.exe"
    if($Action -eq 'Launch'){Start-Process -FilePath $exe;return}
    [pscustomobject]@{releaseId=$manifest.releaseId;runId=$manifest.runId;sourceRevision=$manifest.sourceRevision;sourceDirty=$manifest.sourceDirty;executablePath=$exe;recoveryPreserved=$true}
    return
}
if(Get-Process -Name 'NexaConnect.POS' -ErrorAction SilentlyContinue){throw 'Close POS before updating; preserve pending work for restart verification.'}
$package=Assert-TerminalPath $PackageDirectory
if($install -eq $package -or $install.StartsWith($package+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
    $package.StartsWith($install+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Install and source package directories must be separate.'}
$manifest=Read-TerminalPackage $package
New-Item -ItemType Directory -Path $install -Force | Out-Null
Protect-DraftDirectory $install
$lock=Open-DraftLifecycleLock $install
try{
    $ownerPath=Join-Path $install 'terminal-install-owner.json'
    if(-not (Test-Path -LiteralPath $ownerPath)){
        @{kind='nexaconnect-draft-terminal';id=[Guid]::NewGuid().ToString('N')}|ConvertTo-Json|Set-Content -LiteralPath $ownerPath
    }
    $activePath=Join-Path $install 'active.json'
    if(Test-Path -LiteralPath $activePath){
        $active=Get-Content -LiteralPath $activePath -Raw | ConvertFrom-Json -AsHashtable
        if($active.releaseId -cnotmatch '^[a-f0-9]{32}$'){throw 'Invalid prior terminal release.'}
        $prior=Read-TerminalPackage (Join-Path $install "releases/$($active.releaseId)")
        Assert-TerminalScopeEqual $prior.scope $manifest.scope
    }
    $target=Join-Path $install "releases/$($manifest.releaseId)"
    if(-not (Test-Path -LiteralPath $target)){
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item -Path (Join-Path $package '*') -Destination $target -Recurse
    }
    [void](Read-TerminalPackage $target)
    $exe=Join-Path $target 'NexaConnect.POS.exe'
    $check=Start-Process -FilePath $exe -ArgumentList '--validate-install' -WindowStyle Hidden -Wait -PassThru
    if($check.ExitCode -ne 0){throw 'Configuration or retained recovery validation failed. Preserve recovery; restore the original terminal configuration before retrying.'}
    if(Get-Process -Name 'NexaConnect.POS' -ErrorAction SilentlyContinue){throw 'POS started during update; close it before activation.'}
    # Stage before atomic activation. Never replace any file in the POS recovery directory.
    $manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $install 'active.pending.json')
    [IO.File]::Move((Join-Path $install 'active.pending.json'),$activePath,$true)
    if($RegisterProtocol){& (Join-Path $PSScriptRoot 'register-pos-protocol.ps1') -ExecutablePath $exe}
    Write-Output "Draft terminal installed: $exe"
    Write-Output 'Launch this executable interactively. Protected recovery was preserved. PDF and session acceptance remain separate checks.'
}finally{$lock.Dispose()}
