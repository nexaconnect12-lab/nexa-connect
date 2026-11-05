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
