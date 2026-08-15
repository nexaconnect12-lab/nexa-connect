[CmdletBinding()]
param(
    [ValidateSet('All', 'PlatformDirectory', 'Restaurant', 'Catalog', 'Customer',
        'Order', 'Inventory', 'Kitchen', 'Payment', 'POS', 'Media', 'Reporting')]
    [string]$Service = 'All',
    [switch]$Confirm,
    [string]$EnvironmentFile
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$generationProject = Join-Path $repositoryRoot 'src/Tools/NexaConnect.DataGeneration'
$importPackageRoot = Join-Path $generationProject 'ImportPackages'

if ([string]::IsNullOrWhiteSpace($EnvironmentFile)) {
    $EnvironmentFile = Join-Path $repositoryRoot '.env'
}

$command = if ($Confirm) { '--confirm' } else { '--plan' }
$dotnetArguments = @('run', '--project', $generationProject, '--no-restore', '--')
if ($Service -eq 'All') {
    $dotnetArguments += '--all'
} else {
    $dotnetArguments += @('--service', $Service)
    $importPackageRoot = Join-Path $importPackageRoot $Service
}
$dotnetArguments += @(
    '--import-package', $importPackageRoot,
    '--environment-file', $EnvironmentFile,
    $command
)

& dotnet @dotnetArguments

if ($LASTEXITCODE -ne 0) {
    throw "Data generation failed for $Service with exit code $LASTEXITCODE."
}
