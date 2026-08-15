[CmdletBinding()]
param(
    [switch]$Confirm,
    [string]$ApplicationVersion = '0.5.0',
    [string]$EnvironmentFile
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$migrationProject = Join-Path $repositoryRoot 'src/Tools/NexaConnect.DataMigration'
$scriptsRoot = Join-Path $migrationProject 'Scripts'

if ([string]::IsNullOrWhiteSpace($EnvironmentFile)) {
    $EnvironmentFile = Join-Path $repositoryRoot '.env'
}

if (Test-Path -LiteralPath $EnvironmentFile) {
    foreach ($line in Get-Content -LiteralPath $EnvironmentFile) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
        }
    }
}

$services = @(
    'PlatformDirectory',
    'Authorization',
    'Restaurant',
    'Catalog',
    'Inventory',
    'Order',
    'Kitchen',
    'Customer',
    'Payment',
    'Notification',
    'POS',
    'Media',
    'Reporting'
)

foreach ($service in $services) {
    $serviceRoot = Join-Path $scriptsRoot $service
    $versions = Get-ChildItem -LiteralPath $serviceRoot -Directory |
        ForEach-Object {
            $metadataPath = Join-Path $_.FullName 'migration.json'
            (Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json).version
        }
    $target = ($versions | Measure-Object -Maximum).Maximum

    if ($null -eq $target) {
        throw "No migrations found for $service."
    }

    $command = if ($Confirm) { '--confirm' } else { '--plan' }
    Write-Host "`n=== $service -> version $target ($($command.TrimStart('-'))) ==="

    & dotnet run --project $migrationProject -- `
        --service $service `
        --scripts-root $scriptsRoot `
        --target $target `
        $command `
        --application-version $ApplicationVersion

    if ($LASTEXITCODE -ne 0) {
        throw "Migration command failed for $service with exit code $LASTEXITCODE."
    }
}
