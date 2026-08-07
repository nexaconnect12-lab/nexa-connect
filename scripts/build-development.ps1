[CmdletBinding()]
param(
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# A running ASP.NET executable locks its apphost and referenced assemblies on Windows.
# Stop only NexaConnect executables; do not terminate unrelated dotnet apps.
Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -like 'NexaConnect.*' } |
    Stop-Process -Force

Start-Sleep -Milliseconds 500

Push-Location $root
try {
    dotnet build NexaConnect.sln --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if ($Test) {
        dotnet test NexaConnect.sln --no-build --verbosity minimal
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
