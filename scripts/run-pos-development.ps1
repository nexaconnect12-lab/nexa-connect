[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Get-Process NexaConnect.Services.Authorization,NexaConnect.Services.Restaurant,NexaConnect.Services.POS -ErrorAction SilentlyContinue |
    Stop-Process -Force

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$authConnection = 'Host=127.0.0.1;Port=5432;Database=NexaConnect_Authorization;Username=nexaconnect_authorization_app;Password=psAQXFh4L0mU2ewI5JOYcw'
$restaurantConnection = 'Host=127.0.0.1;Port=5432;Database=NexaConnect_Restaurant;Username=nexaconnect_restaurant_app;Password=Passw0rd'
$posConnection = 'Host=127.0.0.1;Port=5432;Database=NexaConnect_POS;Username=nexaconnect_pos_app;Password=Passw0rd'

$logRoot = Join-Path $root '.runlogs'
New-Item -ItemType Directory -Force $logRoot | Out-Null

Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c', "set ASPNETCORE_ENVIRONMENT=Development&& set ConnectionStrings__Authorization=$authConnection&& dotnet run --no-launch-profile --project `"$root\src\Services\NexaConnect.Services.Authorization`" --no-build --urls http://localhost:51223 > `"$logRoot\authorization.log`" 2>&1"
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c', "set ASPNETCORE_ENVIRONMENT=Development&& set ConnectionStrings__Restaurant=$restaurantConnection&& dotnet run --no-launch-profile --project `"$root\src\Services\NexaConnect.Services.Restaurant`" --no-build --urls http://localhost:51225 > `"$logRoot\restaurant.log`" 2>&1"
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c', "set ASPNETCORE_ENVIRONMENT=Development&& set ConnectionStrings__POS=$posConnection&& set WorkloadIdentity__ClientSecret=local-dev-only-change-me&& dotnet run --no-launch-profile --project `"$root\src\Services\NexaConnect.Services.POS`" --no-build --urls http://localhost:5225 > `"$logRoot\pos.log`" 2>&1"

Write-Host 'Started Authorization (51223), Restaurant (51225), and POS (5225).'
Write-Host 'Logs: .runlogs\authorization.log, .runlogs\restaurant.log, .runlogs\pos.log'
