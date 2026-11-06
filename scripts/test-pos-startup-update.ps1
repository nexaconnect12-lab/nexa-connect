#requires -Version 5.1
param([Parameter(Mandatory)][string]$UnsignedSetup)

$ErrorActionPreference = 'Stop'
$worker = Join-Path $PSScriptRoot '../src/Clients/NexaConnect.POS/Updates/ApplyPosUpdate.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($worker, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors) { throw 'Startup update worker syntax failed.' }
foreach ($name in @('Get-FileVersion', 'Test-PosUpdateInstaller')) {
    $definition = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $false)
    if ($null -eq $definition) { throw 'Missing update verification function.' }
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Rejected([string]$path, [string]$hash, [string]$publisher, [version]$version) {
    $lock = [IO.File]::Open($path, 'Open', 'Read', 'Read')
    try {
        if (Test-PosUpdateInstaller $path $lock $hash $publisher $version) { throw 'Unsafe installer was accepted.' }
    }
    finally { $lock.Dispose() }
}

$fixture = (Resolve-Path -LiteralPath $UnsignedSetup).Path
if ((Get-AuthenticodeSignature -LiteralPath $fixture).Status -ne 'NotSigned') { throw 'Expected an unsigned installer fixture.' }
$version = Get-FileVersion $fixture
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($fixture).ProductName.TrimEnd() -cne 'NexaConnect POS') { throw 'Invalid fixture product metadata.' }
Assert-Rejected $fixture (Get-FileHash -LiteralPath $fixture).Hash ('0' * 64) $version
Assert-Rejected $fixture ('0' * 64) ('0' * 64) $version

$windowsBinary = Join-Path $PSHOME 'powershell.exe'
$signature = Get-AuthenticodeSignature -LiteralPath $windowsBinary
if ($signature.Status -ne 'Valid') { throw 'Expected a valid Windows-signed binary fixture.' }
$sha = [Security.Cryptography.SHA256]::Create()
try { $fingerprint = [BitConverter]::ToString($sha.ComputeHash($signature.SignerCertificate.RawData)).Replace('-', '') }
finally { $sha.Dispose() }
$binaryHash = (Get-FileHash -LiteralPath $windowsBinary).Hash
Assert-Rejected $windowsBinary $binaryHash ('0' * 64) (Get-FileVersion $windowsBinary)
Assert-Rejected $windowsBinary $binaryHash $fingerprint (Get-FileVersion $windowsBinary)
Write-Output 'Startup update verifier rejected unsigned, wrong-hash, wrong-publisher and wrong-product artifacts.'
