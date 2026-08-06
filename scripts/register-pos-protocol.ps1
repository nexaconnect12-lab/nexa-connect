[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath -ErrorAction Stop).Path
$commandKey = 'HKCU:\Software\Classes\nexaconnect-pos\shell\open\command'

New-Item -Path $commandKey -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\Classes\nexaconnect-pos' -Name '(Default)' -Value 'URL:NexaConnect POS callback'
Set-ItemProperty -Path 'HKCU:\Software\Classes\nexaconnect-pos' -Name 'URL Protocol' -Value ''
Set-ItemProperty -Path $commandKey -Name '(Default)' -Value ('"{0}" "%1"' -f $resolvedExecutable)

Write-Output "Registered nexaconnect-pos:// callbacks for $resolvedExecutable"
