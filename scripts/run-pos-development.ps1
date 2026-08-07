[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Load simple KEY=VALUE entries from the developer-only .env file without printing values.
$envFile = Join-Path $root '.env'
if (Test-Path -LiteralPath $envFile) {
    foreach ($line in Get-Content -LiteralPath $envFile) {
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

Get-Process NexaConnect.Services.Authorization,NexaConnect.Services.Restaurant,NexaConnect.Services.POS -ErrorAction SilentlyContinue |
    Stop-Process -Force

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$requiredSecrets = @(
    'ConnectionStrings__Authorization',
    'ConnectionStrings__Restaurant',
    'ConnectionStrings__POS',
    'WorkloadIdentity__ClientSecret'
)
$missingSecrets = @($requiredSecrets | Where-Object {
    [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))
})
if ($missingSecrets.Count -gt 0) {
    throw 'Set ConnectionStrings__Authorization, ConnectionStrings__Restaurant, ConnectionStrings__POS, and WorkloadIdentity__ClientSecret from your local secret store before starting POS development services.'
}

$logRoot = Join-Path $root '.runlogs'
New-Item -ItemType Directory -Force $logRoot | Out-Null

# The child processes inherit the validated environment. Do not put secret values
# in command-line arguments, where they may be visible to process inspection tools.
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c', "set ASPNETCORE_ENVIRONMENT=Development&& dotnet run --no-launch-profile --project `"$root\src\Services\NexaConnect.Services.Authorization`" --no-build --urls http://localhost:51223 > `"$logRoot\authorization.log`" 2>&1"
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c', "set ASPNETCORE_ENVIRONMENT=Development&& dotnet run --no-launch-profile --project `"$root\src\Services\NexaConnect.Services.Restaurant`" --no-build --urls http://localhost:51225 > `"$logRoot\restaurant.log`" 2>&1"
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c', "set ASPNETCORE_ENVIRONMENT=Development&& dotnet run --no-launch-profile --project `"$root\src\Services\NexaConnect.Services.POS`" --no-build --urls http://localhost:5225 > `"$logRoot\pos.log`" 2>&1"

Write-Host 'Started Authorization (51223), Restaurant (51225), and POS (5225).'
Write-Host 'Logs: .runlogs\authorization.log, .runlogs\restaurant.log, .runlogs\pos.log'
