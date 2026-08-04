$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Backend projects
dotnet new webapi -n NexaConnect.Gateway -o src/Gateway/NexaConnect.Gateway --use-controllers
dotnet new classlib -n NexaConnect.BuildingBlocks -o src/BuildingBlocks/NexaConnect.BuildingBlocks
dotnet new classlib -n NexaConnect.Contracts -o src/BuildingBlocks/NexaConnect.Contracts
dotnet new classlib -n NexaConnect.Infrastructure -o src/BuildingBlocks/NexaConnect.Infrastructure
dotnet new classlib -n NexaConnect.Shared -o src/BuildingBlocks/NexaConnect.Shared

$services = @('Catalog', 'Inventory', 'Order', 'Customer', 'Payment', 'Notification', 'POS')
foreach ($service in $services) {
    dotnet new webapi -n "NexaConnect.Services.$service" -o "src/Services/NexaConnect.Services.$service" --use-controllers
}

# Operational data tools
dotnet new console -n NexaConnect.DataMigration -o src/Tools/NexaConnect.DataMigration
dotnet new console -n NexaConnect.DataGeneration -o src/Tools/NexaConnect.DataGeneration

# Aspire
dotnet new aspire-apphost -n NexaConnect.AppHost -o src/Aspire/NexaConnect.AppHost
dotnet new aspire-servicedefaults -n NexaConnect.ServiceDefaults -o src/Aspire/NexaConnect.ServiceDefaults

# Tests
dotnet new xunit -n NexaConnect.UnitTests -o tests/Unit/NexaConnect.UnitTests
dotnet new xunit -n NexaConnect.IntegrationTests -o tests/Integration/NexaConnect.IntegrationTests
dotnet new xunit -n NexaConnect.ArchitectureTests -o tests/Architecture/NexaConnect.ArchitectureTests

# Add generated .NET projects to the solution
Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object {
    dotnet sln NexaConnect.sln add $_.FullName
}

Write-Host 'Backend solution projects created successfully.'
Write-Host 'Create React applications separately with Vite inside src/Clients.'
