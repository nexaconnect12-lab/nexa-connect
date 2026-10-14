# Shared local draft lifecycle guards. No business rules or production defaults.
Set-StrictMode -Version Latest
function Assert-DraftRunId([string]$RunId) {
    if ($RunId -cnotmatch '^[a-f0-9]{32}$') { throw 'Draft RunId must be a generated 32-character lowercase UUID.' }
}
function Get-DraftRunDirectory([string]$RepositoryRoot, [string]$RunId) {
    Assert-DraftRunId $RunId
    return Join-Path $RepositoryRoot ".runstate/first-draft/$RunId"
}
function Open-DraftLifecycleLock([string]$Directory) {
    try {
        return [IO.File]::Open((Join-Path $Directory 'lifecycle.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    } catch [IO.IOException] { throw 'Another lifecycle command owns this draft; retry after it finishes.' }
}
function Get-DraftServiceCatalog([string]$RepositoryRoot) {
    $catalog = [ordered]@{}
    foreach ($name in @('PlatformDirectory','Authorization','Restaurant','Catalog','Inventory','Kitchen','Payment','POS','Reporting','Order')) {
        $directory = Join-Path $RepositoryRoot "src/Tools/NexaConnect.DataMigration/Scripts/$name"
        $migrations = @(Get-ChildItem -LiteralPath $directory -Directory | ForEach-Object {
            Get-Content -LiteralPath (Join-Path $_.FullName 'migration.json') -Raw | ConvertFrom-Json
        } | Sort-Object version)
        if ($migrations.Count -eq 0) { throw "No migration catalog for $name." }
        $latest = $migrations[-1]
        $minimum = ($migrations | Sort-Object { [version]$_.minimumApplicationVersion })[-1].minimumApplicationVersion
        $catalog[$name] = @{ version=[int]$latest.version; minimumApplicationVersion=[string]$minimum }
    }
    return $catalog
}
function Assert-DraftPayment([string]$PaymentMethod, [string]$QrImagePath) {
    if ($PaymentMethod -cnotin @('cash_manual','promptpay_manual')) { throw 'Draft checkout supports only THB cash_manual or promptpay_manual.' }
    if ($PaymentMethod -eq 'promptpay_manual') {
        if (-not $QrImagePath -or -not (Test-Path -LiteralPath $QrImagePath -PathType Leaf) -or
            [IO.Path]::GetExtension($QrImagePath).ToLowerInvariant() -notin @('.png','.jpg','.jpeg','.bmp')) {
            throw 'PromptPay requires an existing deployment-controlled PNG/JPEG/BMP test QR image.'
        }
    }
}
function Assert-DraftUrls($Urls) {
    $ports=@()
    foreach($entry in $Urls.GetEnumerator()) {
        $uri=$null
        $scheme=if($entry.Key -eq 'Bff'){'https'}else{'http'}
        if(-not [uri]::TryCreate($entry.Value,[UriKind]::Absolute,[ref]$uri) -or $uri.Host -ne '127.0.0.1' -or
            $uri.Scheme -ne $scheme -or $uri.Port -lt 1024 -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/') {
            throw 'Draft service URLs must be distinct generated loopback listeners.'
        }
        $ports+=$uri.Port
    }
    if(($ports | Sort-Object -Unique).Count -ne $ports.Count){throw 'Draft service ports overlap.'}
}
function Assert-DraftProcess($Recorded, $Actual) {
    if (-not $Actual) { return $false }
    if ($Actual.Id -ne $Recorded.id -or $Actual.StartTime.ToUniversalTime().Ticks -ne $Recorded.startTicks) {
        throw 'Draft process identity mismatch; refusing to stop a reused PID.'
    }
    return $true
}
function Get-DraftComposeArguments([string]$RepositoryRoot,[string]$RunId) {
    Assert-DraftRunId $RunId
    # Explicit empty env-file prevents Compose from importing the repository .env.
    return @('compose','--env-file',(Join-Path (Get-DraftRunDirectory $RepositoryRoot $RunId) 'compose.env'),
        '-f',(Join-Path $RepositoryRoot 'docker/first-draft/compose.yaml'),'-p',"nexa-draft-$RunId")
}
function Protect-DraftDirectory([string]$Directory) {
    if (-not $IsWindows) { throw 'The first-draft host requires Windows and current-user DPAPI.' }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls $Directory /inheritance:r /grant:r "*$($sid):(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to restrict the draft run directory to its Windows owner.' }
}
function Save-DraftSecrets([string]$Path,$Secrets) {
    ($Secrets | ConvertTo-Json -Compress) | ConvertTo-SecureString -AsPlainText -Force | Export-Clixml -LiteralPath $Path
}
function Read-DraftSecrets([string]$Path) {
    return (Import-Clixml -LiteralPath $Path | ConvertFrom-SecureString -AsPlainText) | ConvertFrom-Json -AsHashtable
}
function Set-DraftComposeEnvironment($Secrets,[string]$RunId,[int]$BffPort) {
    $previous = @{}
    foreach ($entry in $Secrets.GetEnumerator()) {
        $name = 'NEXACONNECT_JOINED_' + $entry.Key
        $previous[$name] = [Environment]::GetEnvironmentVariable($name,'Process')
        [Environment]::SetEnvironmentVariable($name,[string]$entry.Value,'Process')
    }
    foreach ($entry in @{NEXACONNECT_JOINED_RUN_ID=$RunId;NEXACONNECT_JOINED_BFF_PORT=[string]$BffPort}.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key,'Process')
        [Environment]::SetEnvironmentVariable($entry.Key,[string]$entry.Value,'Process')
    }
    return $previous
}
function Restore-DraftEnvironment($Previous) {
    foreach ($entry in $Previous.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process') }
}
function Wait-DraftHttp([string]$Url,[int]$TimeoutSeconds=90,$Headers=@{}) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $reply = Invoke-WebRequest -Uri $Url -TimeoutSec 5 -SkipCertificateCheck -Headers $Headers
            if ($reply.StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    throw 'Owned draft HTTP readiness probe failed; inspect restricted run logs.'
}
