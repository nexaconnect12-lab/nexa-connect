#requires -Version 7.0
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure,[switch]$NoBuild,[string]$DockerExecutable='docker')
& (Join-Path $PSScriptRoot 'test-financial-completeness-portal.ps1') -CashierDayClose -ConfirmDisposableInfrastructure:$ConfirmDisposableInfrastructure -NoBuild:$NoBuild -DockerExecutable $DockerExecutable
