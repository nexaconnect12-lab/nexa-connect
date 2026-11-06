# Physical-POS testing is a new, isolated draft graph, never a conversion of retained recovery.
function Test-PhysicalPosSameSubnet([string]$HostAddress,[string]$DeviceAddress,[int]$PrefixLength) {
    if($PrefixLength -lt 1 -or $PrefixLength -gt 32){return $false}
    $mask=[uint32]::MaxValue -shl (32-$PrefixLength)
    $hostBytes=([Net.IPAddress]$HostAddress).GetAddressBytes();$deviceBytes=([Net.IPAddress]$DeviceAddress).GetAddressBytes()
    if($hostBytes.Length -ne 4 -or $deviceBytes.Length -ne 4){return $false}
    [Array]::Reverse($hostBytes);[Array]::Reverse($deviceBytes)
    return ([BitConverter]::ToUInt32($hostBytes,0) -band $mask) -eq ([BitConverter]::ToUInt32($deviceBytes,0) -band $mask)
}
function Get-PhysicalPosEdgeHash([string]$Directory) {
    $files=[ordered]@{}
    foreach($file in Get-ChildItem -LiteralPath $Directory -Recurse -File|Sort-Object FullName){[void](Assert-TerminalPath $file.FullName);$files[[IO.Path]::GetRelativePath($Directory,$file.FullName).Replace('\','/')]=(Get-FileHash $file.FullName).Hash}
    return [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($files|ConvertTo-Json -Compress))))
}
function Assert-PhysicalPosNetwork($Plan) {
    $interfaces=@(Get-NetIPAddress -AddressFamily IPv4|Where-Object IPAddress -eq $Plan.hostAddress)
    if(-not $interfaces.Count){throw 'Configured staff listener is not on this PC.'}
    if(-not @($interfaces|Where-Object {Test-PhysicalPosSameSubnet $Plan.hostAddress $Plan.deviceAddress $_.PrefixLength}).Count){throw 'Host and physical POS must be on the same directly connected IPv4 subnet for this pilot.'}
}
function Read-PhysicalPosPlan([string]$Path) {
    $pathFull=Assert-TerminalPath $Path
    if((Get-Item -LiteralPath $pathFull).Length -gt 65536){throw 'Physical POS plan too large.'}
    $p=Get-Content -LiteralPath $pathFull -Raw|ConvertFrom-Json -AsHashtable
    if($p.version -ne 1 -or $p.runId -cnotmatch '^[a-f0-9]{32}$' -or $p.confirmFreshTestData -cne $true){throw 'A new isolated physical-POS plan is required.'}
    foreach($name in @('hostAddress','deviceAddress')){
        $ip=$null
        if(-not [Net.IPAddress]::TryParse([string]$p[$name],[ref]$ip) -or $ip.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork){throw 'Exact private IPv4 addresses required.'}
        $b=$ip.GetAddressBytes();if(-not($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))){throw 'Private IPv4 addresses required.'}
    }
    $origin=$null
    if(-not [uri]::TryCreate([string]$p.origin,[UriKind]::Absolute,[ref]$origin) -or $origin.Scheme -cne 'https' -or $origin.Host -cne $p.hostAddress -or $origin.Port -lt 1024 -or $origin.GetLeftPart([UriPartial]::Authority) -cne $p.origin -or $origin.UserInfo -or $origin.Query -or $origin.Fragment -or $p.deviceAddress -eq $p.hostAddress){throw 'Exact HTTPS host origin and a separate device address required.'}
    foreach($key in @('certificatePath','privateKeyPath')){$p[$key]=Assert-TerminalPath ([string]$p[$key]);if(-not(Test-Path -LiteralPath $p[$key] -PathType Leaf)){throw 'Certificate and private-key files required on the development PC.'}}
    if($p.hostTrustConfirmed -cne $true -or $p.deviceTrustConfirmed -cne $true -or $p.deviceWindowsAccount -isnot [string] -or [string]::IsNullOrWhiteSpace($p.deviceWindowsAccount) -or $p.deviceWindowsAccount.Length -gt 100){throw 'Confirm certificate trust and intended device account; never put a password in this plan.'}
    return $p
}
function Stop-PhysicalPosEdge($State,[string]$Run) {
    if(-not $State.Contains('staffEdge')){return}
    $record=$State.staffEdge;$process=Get-Process -Id $record.id -ErrorAction SilentlyContinue
    if($process -and $process.StartTime.ToUniversalTime().Ticks -eq $record.startTicks){
        $actual=Get-CimInstance Win32_Process -Filter "ProcessId = $($record.id)"
        $assembly=Join-Path $Run 'apps/StaffEdge/NexaConnect.PosPilotEdge.dll'
        if($actual.ExecutablePath -ne (Get-Command dotnet).Source -or $actual.CommandLine -notlike "*`"$assembly`"*"){throw 'Staff edge ownership changed; no process stopped.'}
        $process.Kill();if(-not $process.WaitForExit(10000)){throw 'Staff edge did not stop.'}
    }
    $State.Remove('staffEdge')
}
function Start-PhysicalPosEdge($State,[string]$Run,[string]$RepositoryRoot,[bool]$NoBuild) {
    if(-not $State.Contains('physicalPosPilot')){return}
    $p=Read-PhysicalPosPlan (Join-Path $Run 'physical-pos-plan.json')
    if($p.runId -cne $State.runId -or $p.origin -cne $State.physicalPosPilot.origin -or (Get-FileHash (Join-Path $Run 'physical-pos-plan.json')).Hash -cne $State.physicalPosPilot.planHash){throw 'Physical pilot plan changed; preserve original identity and recovery.'}
    if((Get-FileHash $p.certificatePath).Hash -cne $State.physicalPosPilot.certificateHash -or (Get-FileHash $p.privateKeyPath).Hash -cne $State.physicalPosPilot.privateKeyHash){throw 'Staff TLS files changed; certificate rotation requires reviewed maintenance.'}
    Assert-PhysicalPosNetwork $p
    $directory=Join-Path $Run 'apps/StaffEdge';$assembly=Join-Path $directory 'NexaConnect.PosPilotEdge.dll'
    if($State.Contains('staffEdge')){Stop-PhysicalPosEdge $State $Run}
    if($NoBuild){if(-not(Test-Path $assembly) -or (Get-PhysicalPosEdgeHash $directory) -cne $State.physicalPosPilot.edgeArtifactsHash){throw 'Rebuild the exact staff edge before restart.'}}
    else{& dotnet publish (Join-Path $RepositoryRoot 'src/Tools/NexaConnect.PosPilotEdge') -c Release --output $directory --verbosity quiet -m:1;if($LASTEXITCODE -ne 0){throw 'Staff edge publication failed.'}}
    $backends=@{identity="http://127.0.0.1:$($State.infrastructure.keycloak)/"}
    foreach($entry in @{pos='POS';order='Order';catalog='Catalog';payment='Payment';media='Media'}.GetEnumerator()){$backends[$entry.Key]=$State.urls[$entry.Value]}
    @{StaffPilot=@{PublicOrigin=$p.origin;ListenAddress=$p.hostAddress;DeviceAddress=$p.deviceAddress;Realm=$State.realm;CertificatePath=$p.certificatePath;PrivateKeyPath=$p.privateKeyPath;Backends=$backends};Observability=@{OtlpEnabled=$false}}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $directory 'appsettings.json')
    if(Get-NetTCPConnection -State Listen -LocalPort ([uri]$p.origin).Port -ErrorAction SilentlyContinue){throw 'Staff edge port already occupied; no listener adopted.'}
    $process=Start-Process (Get-Command dotnet).Source -ArgumentList @("`"$assembly`"") -WorkingDirectory $directory -Environment @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';Observability__OtlpEnabled='false'} -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Run 'StaffEdge.log') -RedirectStandardError (Join-Path $Run 'StaffEdge.error.log')
    $State.staffEdge=@{id=$process.Id;startTicks=$process.StartTime.ToUniversalTime().Ticks;assemblyName='NexaConnect.PosPilotEdge'}
    $State.physicalPosPilot.edgeArtifactsHash=Get-PhysicalPosEdgeHash $directory
}

function Set-PhysicalGuestQrProfile($State,[int]$Port,[bool]$NoBuild) {
    if(-not $State.Contains('physicalPosPilot') -or $State.Contains('qrPilotOrigin') -or $State.processes.Count -or
        $State.Contains('staffEdge') -or $State.Contains('physicalGuestEdge') -or $NoBuild){
        throw 'Guest QR changes require a stopped physical POS pilot and a build-enabled Start.'
    }
    if($Port -eq 0){
        if($State.Contains('physicalGuestQrOrigin')){
            $State.physicalGuestQrPreviousOrigin=$State.physicalGuestQrOrigin
            $State.Remove('physicalGuestQrOrigin')
        }
        return
    }
    $staff=[uri]$State.physicalPosPilot.origin
    $reserved=@($State.urls.Values|ForEach-Object{([uri]$_).Port})+@($State.infrastructure.Values)+@($staff.Port)
    if($Port -lt 1024 -or $Port -gt 65535 -or $Port -in $reserved){
        throw 'Choose a separate unprivileged guest HTTPS port.'
    }
    $origin="https://$($staff.Host):$Port"
    Assert-DraftQrPilotOrigin $origin
    $previous=$State['physicalGuestQrOrigin'] ?? $State['physicalGuestQrPreviousOrigin']
    if($previous -and $previous -cne $origin){
        throw 'The existing guest origin is retained because issued links depend on it.'
    }
    $State.physicalGuestQrOrigin=$origin
}

function Stop-PhysicalGuestEdge($State,[string]$Run) {
    if(-not $State.Contains('physicalGuestEdge')){return}
    $record=$State.physicalGuestEdge
    $process=Get-Process -Id $record.id -ErrorAction SilentlyContinue
    if(Assert-DraftProcess $record $process){
        $actual=Get-CimInstance Win32_Process -Filter "ProcessId = $($record.id)"
        $assembly=Join-Path $Run 'apps/PhysicalGuestEdge/NexaConnect.QrPilotEdge.dll'
        if($actual.ExecutablePath -ne (Get-Command dotnet).Source -or $actual.CommandLine -notlike ('*"'+$assembly+'"*')){
            throw 'Physical guest edge ownership changed; no process stopped.'
        }
        $process.Kill();if(-not $process.WaitForExit(10000)){throw 'Physical guest edge did not stop.'}
    }
    $State.Remove('physicalGuestEdge')
}

function Start-PhysicalGuestEdge($State,[string]$Run,[string]$RepositoryRoot,[bool]$NoBuild,[string]$BackendCertificatePassword) {
    if(-not $State.Contains('physicalGuestQrOrigin')){return}
    $origin=$State.physicalGuestQrOrigin
    Assert-DraftQrPilotOrigin $origin
    $uri=[uri]$origin
    $plan=Read-PhysicalPosPlan (Join-Path $Run 'physical-pos-plan.json')
    if($plan.runId -cne $State.runId -or $plan.origin -cne $State.physicalPosPilot.origin -or
        $uri.Host -cne $plan.hostAddress -or $uri.Port -eq ([uri]$plan.origin).Port -or
        (Get-FileHash $plan.certificatePath).Hash -cne $State.physicalPosPilot.certificateHash -or
        (Get-FileHash $plan.privateKeyPath).Hash -cne $State.physicalPosPilot.privateKeyHash){
        throw 'The guest endpoint must use the bound physical pilot host and TLS files on its own port.'
    }
    if($State.Contains('physicalGuestEdge')){throw 'Stop the owned guest listener before starting it again.'}
    if(Get-NetTCPConnection -State Listen -LocalPort $uri.Port -ErrorAction SilentlyContinue){
        throw 'Physical guest port already occupied; no listener adopted.'
    }
    $directory=Assert-TerminalPath (Join-Path $Run 'apps/PhysicalGuestEdge')
    $assembly=Join-Path $directory 'NexaConnect.QrPilotEdge.dll'
    if($NoBuild){
        if(-not (Test-Path -LiteralPath $assembly) -or -not $State.Contains('physicalGuestEdgeHash') -or
            (Get-PhysicalPosEdgeHash $directory) -cne $State.physicalGuestEdgeHash){throw 'Rebuild the exact guest edge before restart.'}
    }else{
        & dotnet publish (Join-Path $RepositoryRoot 'src/Tools/NexaConnect.QrPilotEdge') -c Release --output $directory --verbosity quiet -m:1
        if($LASTEXITCODE -ne 0){throw 'Physical guest edge publication failed.'}
    }
    $cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new(
        (Join-Path $Run 'local.pfx'),$BackendCertificatePassword,
        [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    try{$pin=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($cert.RawData))}finally{$cert.Dispose()}
    @{QrPilot=@{PublicOrigin=$origin;ListenAddress=$plan.hostAddress;BackendOrigin="https://localhost:$(([uri]$State.urls.Bff).Port)";
        BackendCertificateSha256=$pin;CertificatePath=$plan.certificatePath;PrivateKeyPath=$plan.privateKeyPath}} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $directory 'appsettings.json')
    $old=@{}
    try{
        foreach($key in @([Environment]::GetEnvironmentVariables('Process').Keys)){
            if($key -match '__|^NEXACONNECT_|^NEXA_|^ASPNETCORE_|^DOTNET_ENVIRONMENT$|^OTEL_'){
                $old[$key]=[Environment]::GetEnvironmentVariable($key);Remove-Item -LiteralPath ("Env:"+$key) -ErrorAction SilentlyContinue
            }
        }
        $launch=@{FilePath=(Get-Command dotnet).Source;ArgumentList=@(('"'+$assembly+'"'));WorkingDirectory=$directory;
            Environment=@{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';Observability__OtlpEnabled='false'};
            WindowStyle='Hidden';PassThru=$true;RedirectStandardOutput=(Join-Path $Run 'PhysicalGuestEdge.log');
            RedirectStandardError=(Join-Path $Run 'PhysicalGuestEdge.error.log')}
        $process=Start-Process @launch
        $State.physicalGuestEdge=@{id=$process.Id;startTicks=$process.StartTime.ToUniversalTime().Ticks;assemblyName='NexaConnect.QrPilotEdge'}
    }finally{Restore-DraftEnvironment $old}
}

function Assert-PhysicalGuestEdgeReady($State,[string]$Run) {
    if(-not $State.Contains('physicalGuestQrOrigin')){return}
    $origin=$State.physicalGuestQrOrigin
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(30)
    $ready=$false
    while([DateTimeOffset]::UtcNow -lt $deadline){
        try{if((Invoke-WebRequest ($origin+'/order') -TimeoutSec 3).StatusCode -eq 200){$ready=$true;break}}catch{}
        Start-Sleep -Milliseconds 500
    }
    if(-not $ready){throw 'Physical guest order page did not pass the trusted HTTPS probe.'}
    $uri=[uri]$origin
    if(-not @(Get-NetTCPConnection -LocalAddress $uri.Host -LocalPort $uri.Port -State Listen -ErrorAction SilentlyContinue |
        Where-Object OwningProcess -eq $State.physicalGuestEdge.id).Count){throw 'Physical guest listener ownership was not verified.'}
    $guest=Invoke-WebRequest ($origin+'/bff/guest/qr/context') -TimeoutSec 10 -SkipHttpErrorCheck
    $staff=Invoke-WebRequest ($origin+'/bff/customer/qr-entries/csrf') -TimeoutSec 10 -SkipHttpErrorCheck
    if($guest.StatusCode -ne 401 -or $staff.StatusCode -ne 404){throw 'Physical guest admission or staff route isolation failed.'}
    $State.physicalGuestEdgeHash=Get-PhysicalPosEdgeHash (Join-Path $Run 'apps/PhysicalGuestEdge')
}
