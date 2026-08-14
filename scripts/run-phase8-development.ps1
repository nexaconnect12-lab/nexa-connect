[CmdletBinding()]
param(
    [switch]$Build,
    [int]$StartupTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stateRoot = Join-Path $root '.runstate\phase8'
$stateFile = Join-Path $stateRoot 'processes.json'
$logRoot = Join-Path $root '.runlogs\phase8'

function Import-DotEnv([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Create $Path from .env.example and replace its placeholders before starting Phase 8."
    }
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*(?:#|$)') { continue }
        $separator = $line.IndexOf('=')
        if ($separator -le 0) { continue }
        $name = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).Trim().Trim('"', "'")
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
}

function Set-FromEnvironment([string]$Target, [string]$Source) {
    $current = [Environment]::GetEnvironmentVariable($Target)
    if ([string]::IsNullOrWhiteSpace($current) -or $current -match 'ReplaceWith|ReplaceFrom') {
        [Environment]::SetEnvironmentVariable($Target, [Environment]::GetEnvironmentVariable($Source), 'Process')
    }
}

function Assert-Settings([string[]]$Names) {
    $invalid = @($Names | Where-Object {
        $value = [Environment]::GetEnvironmentVariable($_)
        [string]::IsNullOrWhiteSpace($value) -or $value -match 'ReplaceWith|ReplaceFrom'
    })
    if ($invalid.Count -gt 0) { throw "Missing or placeholder Phase 8 settings in .env: $($invalid -join ', ')" }
}

function Start-Phase8Process($Service) {
    $previous = @{}
    $previousContentRoot = [Environment]::GetEnvironmentVariable('ASPNETCORE_CONTENTROOT')
    try {
        foreach ($entry in $Service.Environment.GetEnumerator()) {
            $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
            [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, 'Process')
        }
        $project = Join-Path $root $Service.Project
        $assemblyName = Split-Path $project -Leaf
        $workingDirectory = $project
        $assembly = Join-Path $project "bin\Debug\net10.0\$assemblyName.dll"
        if ($Service.Name -eq 'customer-bff') {
            $workingDirectory = Join-Path $root '.runartifacts\phase8\customer-bff'
            $assembly = Join-Path $workingDirectory 'NexaConnect.CustomerBff.dll'
        }
        [Environment]::SetEnvironmentVariable('ASPNETCORE_CONTENTROOT', $workingDirectory, 'Process')
        if (-not (Test-Path -LiteralPath $assembly)) {
            throw "$assembly is missing. Run the launcher with -Build first."
        }
        $log = Join-Path $logRoot "$($Service.Name).log"
        $arguments = "`"$assembly`" --urls $($Service.Url)"
        $process = Start-Process dotnet -WindowStyle Hidden -ArgumentList $arguments -PassThru `
            -RedirectStandardOutput $log -RedirectStandardError "$log.error"
        return [pscustomobject]@{
            Name = $Service.Name
            Id = $process.Id
            StartTimeUtc = $process.StartTime.ToUniversalTime().ToString('O')
            Log = $log
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('ASPNETCORE_CONTENTROOT', $previousContentRoot, 'Process')
        foreach ($entry in $Service.Environment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $previous[$entry.Key], 'Process')
        }
    }
}

function Test-Ready($Service, $State) {
    $process = Get-Process -Id $State.Id -ErrorAction SilentlyContinue
    if ($null -eq $process) { throw "$($Service.Name) exited during startup. Inspect $($State.Log).error" }
    $uri = [Uri]$Service.ReadinessUrl
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $attempt = $client.BeginConnect($uri.Host, $uri.Port, $null, $null)
        if (-not $attempt.AsyncWaitHandle.WaitOne(2000)) { return $false }
        $client.EndConnect($attempt)
        return $true
    }
    catch { return $false }
    finally { $client.Dispose() }
}

Import-DotEnv (Join-Path $root '.env')
# Some Windows development shells expose both PATH and Path. Start-Process
# treats them as duplicate dictionary keys, so preserve the value under the
# canonical Windows casing before launching children.
$processPath = [Environment]::GetEnvironmentVariable('Path')
[Environment]::SetEnvironmentVariable('PATH', $null, 'Process')
[Environment]::SetEnvironmentVariable('Path', $null, 'Process')
[Environment]::SetEnvironmentVariable('Path', $processPath, 'Process')
Set-FromEnvironment 'ConnectionStrings__Restaurant' 'NEXACONNECT_RESTAURANT_IMPORT_DB'
Set-FromEnvironment 'ConnectionStrings__Reporting' 'NEXACONNECT_REPORTING_IMPORT_DB'
Set-FromEnvironment 'ConnectionStrings__Media' 'NEXACONNECT_MEDIA_IMPORT_DB'
Set-FromEnvironment 'KeycloakAdmin__ClientSecret' 'PLATFORM_DIRECTORY_ADMIN_CLIENT_SECRET'
Set-FromEnvironment 'Bff__ClientSecret' 'NEXACONNECT_WEB_BFF_CLIENT_SECRET'
Set-FromEnvironment 'MediaStorage__AccessKey' 'MINIO_ROOT_USER'
Set-FromEnvironment 'MediaStorage__SecretKey' 'MINIO_ROOT_PASSWORD'

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Authentication__Authority = 'http://localhost:8080/realms/nexa-dev'
$env:Authentication__RequireHttpsMetadata = 'false'
$env:Bff__Authority = 'http://localhost:8080/realms/nexa-dev'
$env:Bff__RequireHttpsMetadata = 'false'
$env:Bff__ClientId = 'nexaconnect-web-bff'
$env:KeycloakAdmin__BaseUrl = 'http://localhost:8080/'
$env:KeycloakAdmin__Realm = 'nexa-dev'
$env:KeycloakAdmin__ClientId = 'platform-directory-admin'
$env:Services__PlatformDirectory = 'http://localhost:53357/'
$env:Services__Authorization = 'http://localhost:51223/'
$env:Services__Restaurant = 'http://localhost:51225/'
$env:Services__Reporting = 'http://localhost:51227/'
$env:Services__Media = 'http://localhost:51229/'
$env:Services__Catalog = 'http://localhost:5268/'
$env:Services__Inventory = 'http://localhost:5270/'
$env:Services__Order = 'http://localhost:5272/'

Assert-Settings @(
    'ConnectionStrings__PlatformDirectory', 'ConnectionStrings__Authorization',
    'ConnectionStrings__Restaurant', 'ConnectionStrings__Reporting', 'ConnectionStrings__Media',
    'ConnectionStrings__Catalog', 'KeycloakAdmin__ClientSecret', 'Bff__ClientSecret',
    'NEXACONNECT_CATALOG_SERVICE_CLIENT_SECRET', 'NEXACONNECT_MEDIA_SERVICE_CLIENT_SECRET',
    'MediaStorage__ServiceUrl', 'MediaStorage__Bucket', 'MediaStorage__AccessKey',
    'MediaStorage__SecretKey', 'MediaSafety__ClamAvHost', 'MediaSafety__ClamAvPort',
    'Outbox__ConnectionString'
)

if (Test-Path -LiteralPath $stateFile) {
    $parsedState = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    $existing = @($parsedState | ForEach-Object { $_ })
    $live = @($existing | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue })
    if ($live.Count -gt 0) { throw 'The Phase 8 run-state contains live processes. Run scripts\stop-phase8-development.ps1 first.' }
    Remove-Item -LiteralPath $stateFile
}

$projects = @(
    'src\Services\NexaConnect.Services.PlatformDirectory', 'src\Services\NexaConnect.Services.Authorization',
    'src\Services\NexaConnect.Services.Restaurant', 'src\Services\NexaConnect.Services.Reporting',
    'src\Services\NexaConnect.Services.Media', 'src\Services\NexaConnect.Services.Catalog',
    'src\Gateway\NexaConnect.CustomerBff'
)
if ($Build) {
    foreach ($project in $projects[0..5]) {
        dotnet build (Join-Path $root $project) --no-restore --verbosity minimal -m:1
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $project" }
    }
    $bffArtifact = Join-Path $root '.runartifacts\phase8\customer-bff'
    dotnet publish (Join-Path $root $projects[6]) --no-restore --output $bffArtifact --verbosity minimal -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed for Customer BFF and Customer Portal.' }
}

New-Item -ItemType Directory -Force -Path $stateRoot, $logRoot | Out-Null
$rabbit = [Environment]::GetEnvironmentVariable('Outbox__ConnectionString')
$services = @(
    @{ Name='platform-directory'; Project=$projects[0]; Url='http://localhost:53357'; ReadinessUrl='http://localhost:53357/health'; Environment=@{ Outbox__Enabled='true'; Outbox__ConnectionString=$rabbit } },
    @{ Name='authorization'; Project=$projects[1]; Url='http://localhost:51223'; ReadinessUrl='http://localhost:51223/openapi/v1.json'; Environment=@{} },
    @{ Name='restaurant'; Project=$projects[2]; Url='http://localhost:51225'; ReadinessUrl='http://localhost:51225/openapi/v1.json'; Environment=@{ Outbox__Enabled='true'; Outbox__ConnectionString=$rabbit } },
    @{ Name='reporting'; Project=$projects[3]; Url='http://localhost:51227'; ReadinessUrl='http://localhost:51227/openapi/v1.json'; Environment=@{ ActivityConsumer__Enabled='true'; ActivityConsumer__ConnectionString=$rabbit } },
    @{ Name='media'; Project=$projects[4]; Url='http://localhost:51229'; ReadinessUrl='http://localhost:51229/openapi/v1.json'; Environment=@{ Outbox__Enabled='true'; Outbox__ConnectionString=$rabbit; MediaSafety__MalwareScanEnabled='true'; WorkloadIdentity__Authority='http://localhost:8080/realms/nexa-dev'; WorkloadIdentity__ClientId='nexaconnect-media-service'; WorkloadIdentity__ClientSecret=[Environment]::GetEnvironmentVariable('NEXACONNECT_MEDIA_SERVICE_CLIENT_SECRET') } },
    @{ Name='catalog'; Project=$projects[5]; Url='http://localhost:5268'; ReadinessUrl='http://localhost:5268/openapi/v1.json'; Environment=@{ Persistence__Provider='PostgreSQL'; WorkloadIdentity__Authority='http://localhost:8080/realms/nexa-dev'; WorkloadIdentity__ClientId='nexaconnect-catalog-service'; WorkloadIdentity__ClientSecret=[Environment]::GetEnvironmentVariable('NEXACONNECT_CATALOG_SERVICE_CLIENT_SECRET') } },
    @{ Name='customer-bff'; Project=$projects[6]; Url='https://localhost:51829'; ReadinessUrl='https://localhost:51829/health/live'; Environment=@{} }
)

$states = @()
$oldCertificateCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
try {
    # Local development certificate only; this callback is restored before the script exits.
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    foreach ($service in $services) {
        try { $states += Start-Phase8Process $service }
        catch { throw "Failed to launch $($service.Name). Inspect $logRoot\$($service.Name).log.error. $($_.Exception.Message)" }
        $states | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    foreach ($service in $services) {
        $state = $states | Where-Object Name -eq $service.Name
        while (-not (Test-Ready $service $state)) {
            if ([DateTimeOffset]::UtcNow -ge $deadline) {
                throw "$($service.Name) was not ready within $StartupTimeoutSeconds seconds. Inspect $($state.Log) and $($state.Log).error"
            }
            Start-Sleep -Seconds 1
        }
    }
}
catch {
    & (Join-Path $PSScriptRoot 'stop-phase8-development.ps1')
    throw
}
finally {
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $oldCertificateCallback
}

Write-Host 'Phase 8 service HTTP listeners are responding:'
$services | ForEach-Object { Write-Host "  $($_.Name): $($_.Url)" }
Write-Host "Logs: $logRoot"
Write-Host 'Open https://localhost:51829 and complete the documented authenticated functional smoke test.'
