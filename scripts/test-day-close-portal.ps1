#requires -Version 7.0
[CmdletBinding()]
param([switch]$ConfirmDisposableInfrastructure,[switch]$NoBuild,[string]$DockerExecutable='docker')
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'test-financial-completeness-portal.ps1') -DayClose -ConfirmDisposableInfrastructure:$ConfirmDisposableInfrastructure -NoBuild:$NoBuild -DockerExecutable $DockerExecutable
