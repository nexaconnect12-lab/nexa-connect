param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../first-draft-helpers.ps1')
Protect-DraftDirectory $Directory
