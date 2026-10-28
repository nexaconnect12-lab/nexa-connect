# Shared local draft lifecycle guards. No business rules or production defaults.
Set-StrictMode -Version Latest
function Assert-DraftQrPilotOrigin([string]$Origin) {
    $uri=$null
    if(-not [Uri]::TryCreate($Origin,[UriKind]::Absolute,[ref]$uri) -or $Origin.Length -gt 256 -or $uri.Scheme -ne 'https' -or $uri.IsLoopback -or $uri.Port -lt 1024 -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment -or $uri.UserInfo -or $uri.GetLeftPart([UriPartial]::Authority) -cne $Origin){throw 'QR pilot origin must be an exact non-loopback HTTPS origin on an unprivileged port.'}
}
function Get-DraftQrPilotSettings([string]$Name,[string]$Origin) {
    Assert-DraftQrPilotOrigin $Origin
    switch($Name){
        'Restaurant' {return @{QrEntry__Enabled='true'}}
        'Order' {return @{QrMenu__Enabled='true';QrRequests__Enabled='true';QrAcceptance__Enabled='true';QrCollection__Enabled='true'}}
        'Bff' {return @{QrGuest__Enabled='true';QrGuest__MenuEnabled='true';QrGuest__RequestsEnabled='true';QrGuest__AcceptanceEnabled='true';QrGuest__PublicOrigin=$Origin}}
        default {return @{}}
    }
}
function Get-DraftQrPilotArtifactHash([string]$RunDirectory) {
    $inventory=[ordered]@{}
    $names=@('PlatformDirectory','Authorization','Restaurant','Catalog','Inventory','Kitchen','Payment','POS','Reporting','Order','Bff')
    $profile=Get-Content -LiteralPath (Join-Path $RunDirectory 'state.json') -Raw|ConvertFrom-Json -AsHashtable
    if($profile.Contains('mediaEnabled') -and $profile.mediaEnabled -isnot [bool]){throw 'Invalid Media profile selection.'}
    if($profile.Contains('mediaEnabled') -and $profile.mediaEnabled){$names+=@('Media')}
    foreach($name in $names){
        $directory=Join-Path $RunDirectory "apps/$name"
        if(-not (Test-Path -LiteralPath $directory) -or ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'QR pilot application artifact directory missing or linked.'}
        $entries=@(Get-ChildItem -LiteralPath $directory -Recurse -Force)
        if(@($entries|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'QR pilot application inventory contains links.'}
        $files=@($entries|Where-Object {-not $_.PSIsContainer -and [IO.Path]::GetRelativePath($directory,$_.FullName).Replace('\','/') -notmatch '^\.runstate/'}|Sort-Object FullName)
        if(-not $files.Count){throw 'QR pilot application inventory is empty.'}
        foreach($file in $files){if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'QR pilot application inventory contains links.'};$inventory[[IO.Path]::GetRelativePath($RunDirectory,$file.FullName).Replace('\','/')]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    return [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($inventory|ConvertTo-Json -Compress))))
}
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
function Get-DraftServiceCatalog([string]$RepositoryRoot,[switch]$IncludeMedia) {
    $catalog = [ordered]@{}
    $names=@('PlatformDirectory','Authorization','Restaurant','Catalog','Inventory','Kitchen','Payment','POS','Reporting','Order')
    if($IncludeMedia){$names+=@('Media')}
    foreach ($name in $names) {
        $directory = Join-Path $RepositoryRoot "src/Tools/NexaConnect.DataMigration/Scripts/$name"
        $migrations = @(Get-ChildItem -LiteralPath $directory -Directory | ForEach-Object {
            Get-Content -LiteralPath (Join-Path $_.FullName 'migration.json') -Raw | ConvertFrom-Json
        } | Sort-Object version)
        if ($migrations.Count -eq 0) { throw "No migration catalog for $name." }
        $latest = $migrations[-1]
        $minimum = ($migrations | Where-Object {$_.PSObject.Properties['minimumApplicationVersion']} | Sort-Object { [version]$_.minimumApplicationVersion })[-1].minimumApplicationVersion
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
function Set-DraftPosServiceUrls($Configuration,[System.Collections.IDictionary]$State) {
    if($State.Contains('mediaEnabled') -and $State.mediaEnabled -isnot [bool]){throw 'Invalid Media profile selection.'}
    Assert-DraftUrls $State.urls
    foreach($name in @('POS','Order','Payment','Catalog')){if(-not $State.urls.Contains($name)){throw 'POS service inventory is incomplete.'}}
    $mediaUrl=''
    if($State.Contains('mediaEnabled') -and $State.mediaEnabled){
        if(-not $State.urls.Contains('Media')){throw 'Enabled Media service URL is missing.'}
        $mediaUrl=$State.urls.Media
    }
    $Configuration.Services.PosApi=$State.urls.POS;$Configuration.Services.OrderApi=$State.urls.Order
    $Configuration.Services.PaymentApi=$State.urls.Payment;$Configuration.Services.CatalogApi=$State.urls.Catalog
    $Configuration.Services | Add-Member -NotePropertyName MediaApi -NotePropertyValue $mediaUrl -Force
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
    $media=@();$stateFile=Join-Path (Get-DraftRunDirectory $RepositoryRoot $RunId) 'state.json'
    if(Test-Path -LiteralPath $stateFile){$profile=Get-Content -LiteralPath $stateFile -Raw|ConvertFrom-Json -AsHashtable;if($profile.Contains('mediaEnabled') -and $profile.mediaEnabled -isnot [bool]){throw 'Invalid Media profile selection.'};if($profile.Contains('mediaEnabled') -and $profile.mediaEnabled){$media=@('-f',(Join-Path $RepositoryRoot 'docker/first-draft/media.compose.yaml'))}}
    return @('compose','--env-file',(Join-Path (Get-DraftRunDirectory $RepositoryRoot $RunId) 'compose.env'),
        '-f',(Join-Path $RepositoryRoot 'docker/first-draft/compose.yaml'))+$media+@('-p',"nexa-draft-$RunId")
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
function Set-DraftComposeEnvironment($Secrets,[string]$RunId,[int]$BffPort,$Infrastructure=@{}) {
    $portNames=@{postgres='POSTGRES';keycloak='KEYCLOAK';rabbitmq='RABBITMQ';redis='REDIS'}
    if($Infrastructure.Contains('minio') -or $Infrastructure.Contains('clamav')){$portNames.minio='MINIO';$portNames.clamav='CLAMAV'}
    if($Infrastructure.Count){
        $ports=@($BffPort)
        foreach($name in $portNames.Keys){
            $port=0
            if(-not $Infrastructure.Contains($name) -or -not [int]::TryParse([string]$Infrastructure[$name],[ref]$port) -or $port -lt 1024 -or $port -gt 65535){throw 'Draft infrastructure requires generated loopback ports.'}
            $ports+=$port
        }
        if(($ports|Sort-Object -Unique).Count -ne $ports.Count){throw 'Draft infrastructure ports must be distinct.'}
    }
    $previous = @{}
    if($Infrastructure.Contains('minio')){
        $name='NEXACONNECT_DRAFT_MEDIA_ORIGIN';$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
        [Environment]::SetEnvironmentVariable($name,"https://127.0.0.1:$BffPort",'Process')
    }
    foreach ($entry in $Secrets.GetEnumerator()) {
        $name = 'NEXACONNECT_JOINED_' + $entry.Key
        $previous[$name] = [Environment]::GetEnvironmentVariable($name,'Process')
        [Environment]::SetEnvironmentVariable($name,[string]$entry.Value,'Process')
    }
    foreach ($entry in @{NEXACONNECT_JOINED_RUN_ID=$RunId;NEXACONNECT_JOINED_BFF_PORT=[string]$BffPort}.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key,'Process')
        [Environment]::SetEnvironmentVariable($entry.Key,[string]$entry.Value,'Process')
    }
    if($Infrastructure.Count){foreach($entry in $portNames.GetEnumerator()){
        $name='NEXACONNECT_DRAFT_'+$entry.Value+'_PORT'
        $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
        [Environment]::SetEnvironmentVariable($name,[string]$Infrastructure[$entry.Key],'Process')
    }}
    return $previous
}
function Restore-DraftEnvironment($Previous) {
    foreach ($entry in $Previous.GetEnumerator()) {
        # PowerShell can bind a missing value as an empty string; modern .NET keeps
        # empty environment variables, which breaks typed configuration binders.
        if($null -eq $entry.Value){Remove-Item -LiteralPath ("Env:"+$entry.Key) -ErrorAction SilentlyContinue}
        else{[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
    }
}
function Ensure-DraftMediaAudience($State,$Secrets) {
    Assert-DraftRunId $State.runId
    $port=0
    if(-not [int]::TryParse([string]$State.infrastructure.keycloak,[ref]$port) -or $port -lt 1024 -or $port -gt 65535){throw 'Generated local identity port required.'}
    if($State.project -cne "nexa-draft-$($State.runId)" -or $State.realm -cne "nexa-review-it-$($State.runId)" -or
       $State.authority -cne "http://127.0.0.1:$port/realms/$($State.realm)"){throw 'Owned local realm required for Media audience setup.'}
    $base="http://127.0.0.1:$port"
    $admin=Invoke-RestMethod "$base/realms/master/protocol/openid-connect/token" -Method Post -Body @{grant_type='password';client_id='admin-cli';username='acceptance-admin';password=$Secrets.KEYCLOAK_ADMIN_PASSWORD}
    $headers=@{Authorization='Bearer '+$admin.access_token}
    $realmBase="$base/admin/realms/$($State.realm)"
    $clients=@(Invoke-RestMethod "$realmBase/clients?clientId=nexaconnect-media-service" -Headers $headers|ForEach-Object{$_})
    if($clients.Count -ne 1 -or $clients[0].clientId -cne 'nexaconnect-media-service' -or $clients[0].publicClient -or -not $clients[0].serviceAccountsEnabled){throw 'Expected private Media workload client missing.'}
    $path="$realmBase/clients/$($clients[0].id)/protocol-mappers/models"
    $mappers=@(Invoke-RestMethod $path -Headers $headers|ForEach-Object{$_})
    $existing=@($mappers|Where-Object name -ceq 'nexaconnect-api-audience')
    if($existing.Count){
        if($existing.Count -ne 1 -or $existing[0].protocolMapper -cne 'oidc-audience-mapper' -or $existing[0].config.'included.custom.audience' -cne 'nexaconnect-api' -or $existing[0].config.'access.token.claim' -cne 'true'){throw 'Conflicting Media audience mapper; no mapper was overwritten.'}
    }else{
        $mapper=@{name='nexaconnect-api-audience';protocol='openid-connect';protocolMapper='oidc-audience-mapper';config=@{'included.custom.audience'='nexaconnect-api';'access.token.claim'='true';'id.token.claim'='false';'userinfo.token.claim'='false'}}
        Invoke-RestMethod $path -Method Post -Headers $headers -ContentType 'application/json' -Body ($mapper|ConvertTo-Json -Depth 4)|Out-Null
    }
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
