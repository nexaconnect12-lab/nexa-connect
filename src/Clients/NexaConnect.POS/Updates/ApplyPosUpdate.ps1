$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Read-UpdateJson([IO.Stream]$stream) {
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
    try {
        $buffer = New-Object char[] 8193
        $length = $reader.ReadBlock($buffer, 0, $buffer.Length)
        if ($length -eq 0 -or $length -gt 8192) { throw 'Invalid update metadata size.' }
        return ConvertFrom-Json (-join $buffer[0..($length - 1)])
    }
    finally { $reader.Dispose() }
}

function Open-UpdateRead([string]$location) {
    if ($null -eq $http) { return [IO.File]::OpenRead($location) }
    return $http.GetStreamAsync($location).GetAwaiter().GetResult()
}

function Get-FileVersion([string]$path) {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    return [version]::new($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart)
}

function Test-PosUpdateInstaller([string]$setup, [IO.Stream]$lockedSetup, [string]$expectedHash,
    [string]$publisher, [version]$targetVersion) {
    if ((Get-FileHash -InputStream $lockedSetup -Algorithm SHA256).Hash -ne $expectedHash) { return $false }
    $signature = Get-AuthenticodeSignature -LiteralPath $setup
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) { return $false }
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { $fingerprint = [BitConverter]::ToString($sha256.ComputeHash($signature.SignerCertificate.RawData)).Replace('-', '') }
    finally { $sha256.Dispose() }
    $productName = [string][Diagnostics.FileVersionInfo]::GetVersionInfo($setup).ProductName
    return $fingerprint -eq $publisher -and (Get-FileVersion $setup) -eq $targetVersion -and
        $productName.TrimEnd() -ceq 'NexaConnect POS'
}

$exe = $env:NEXA_UPDATE_EXE
$stage = $env:NEXA_UPDATE_STAGE
$setup = Join-Path $stage 'setup.exe'
$http = $null
$lease = $null
$lockedSetup = $null
$parent = $null
$apply = $false
$updated = $false
try {
    $parent = [Diagnostics.Process]::GetProcessById([int]$env:NEXA_UPDATE_PARENT)
    # Bind to the actual parent process before it can exit and its PID be reused.
    $null = $parent.Handle
    $lease = [Threading.Mutex]::new($false, 'NexaConnect.POS.StartupUpdate')
    $source = Read-UpdateJson ([IO.File]::OpenRead($env:NEXA_UPDATE_SOURCE))
    $location = [string]$source.manifest
    $publisher = [string]$source.publisherCertificateSha256
    if ($publisher -notmatch '\A[a-f0-9]{64}\z' -or $location.Length -gt 500) { return }
    if ($location.StartsWith('\\')) {
        $parts = $location.Substring(2).Split('\')
        if ($parts.Length -lt 3 -or $parts[0] -notmatch '\A[A-Za-z0-9][A-Za-z0-9.-]*\z' -or
            $parts[-1] -cne 'pos-update.json') { return }
        foreach ($part in $parts[1..($parts.Length - 1)]) {
            if ($part -notmatch '\A[A-Za-z0-9_. $-]+\z' -or $part.EndsWith('.') -or $part.EndsWith(' ')) { return }
        }
        $directory = [IO.Path]::GetDirectoryName($location)
    }
    else {
        $uri = $null
        if (-not [uri]::TryCreate($location, [UriKind]::Absolute, [ref]$uri) -or
            $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or
            $location.Contains('\') -or $uri.Segments[-1] -cne 'pos-update.json') { return }
        Add-Type -AssemblyName System.Net.Http
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $handler = New-Object Net.Http.HttpClientHandler
        $handler.AllowAutoRedirect = $false
        $handler.UseCookies = $false
        $http = New-Object Net.Http.HttpClient($handler)
        $http.Timeout = [timespan]::FromSeconds(8)
        $directory = [uri]::new($uri, '.')
    }
    $manifest = Read-UpdateJson (Open-UpdateRead $location)
    $versionText = [string]$manifest.version
    $expectedHash = [string]$manifest.sha256
    if ($versionText -notmatch '\A(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\z' -or
        $expectedHash -notmatch '\A[a-f0-9]{64}\z') { return }
    $targetVersion = [version]$versionText
    if ($targetVersion -le (Get-FileVersion $exe)) { return }
    $name = "NexaConnect-POS-$versionText-win-x64-setup.exe"
    $download = if ($null -eq $http) { Join-Path $directory $name } else { [uri]::new($directory, $name).AbsoluteUri }
    $null = [IO.Directory]::CreateDirectory($stage)
    $inputStream = Open-UpdateRead $download
    try {
        $outputStream = [IO.File]::Open($setup, 'CreateNew', 'Write', 'None')
        try {
            $buffer = New-Object byte[] 131072
            $total = 0L
            while (($count = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $total += $count
                if ($total -gt 268435456) { throw 'Installer exceeds the download limit.' }
                $outputStream.Write($buffer, 0, $count)
            }
        }
        finally { $outputStream.Dispose() }
    }
    finally { $inputStream.Dispose() }

    # Keep writes and replacement blocked from verification through Setup completion.
    $lockedSetup = [IO.File]::Open($setup, 'Open', 'Read', 'Read')
    if (-not (Test-PosUpdateInstaller $setup $lockedSetup $expectedHash $publisher $targetVersion)) { return }

    [Console]::WriteLine('NEXA-UPDATE-READY')
    [Console]::Out.Flush()
    if (-not $parent.WaitForExit(30000)) { return }
    $apply = $true
    $profile = Join-Path ([IO.Path]::GetDirectoryName($exe)) 'appsettings.json'
    $arguments = '/SP- /SILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /PROFILE="' + $profile + '"'
    $installer = Start-Process -FilePath $setup -ArgumentList $arguments -Wait -PassThru
    $updated = $installer.ExitCode -eq 0 -and (Get-FileVersion $exe) -eq $targetVersion
}
catch {
    # Feed, signature, download and installer failures never expose URLs or local details.
}
finally {
    if ($lockedSetup) { $lockedSetup.Dispose() }
    if ($http) { $http.Dispose() }
    if ($parent) { $parent.Dispose() }
    if ($lease) { $lease.Dispose() }
    try {
        [IO.File]::Delete($setup)
        if ([IO.Directory]::Exists($stage)) { [IO.Directory]::Delete($stage) }
    }
    catch { }
    if ($apply) {
        $flag = if ($updated) { '--skip-startup-update' } else { '--startup-update-failed' }
        Start-Process -FilePath $exe -ArgumentList $flag
    }
}
