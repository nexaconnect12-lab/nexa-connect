#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidatePattern('^\d{1,4}\.\d{1,4}\.\d{1,4}$')][string]$PackageVersion='0.7.0',
    [switch]$PrivateUnsigned,
    [string]$SignToolPath,
    [ValidatePattern('^[a-fA-F0-9]{40}$')][string]$CertificateThumbprint,
    [string]$TimestampUrl='https://timestamp.digicert.com'
)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'POS installer packaging requires Windows.'}
$repoRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'first-draft-terminal-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
$output=Assert-TerminalPath $OutputDirectory
$compiler=Assert-TerminalPath $CompilerPath
if(Test-Path -LiteralPath $output){throw 'Use a fresh output directory; existing candidates are never overwritten.'}
if(-not (Test-Path -LiteralPath $compiler -PathType Leaf)){throw 'Provide the approved Inno Setup compiler.'}
if(-not $PrivateUnsigned){
    if(-not $SignToolPath -or -not $CertificateThumbprint){throw 'Release builds require SignTool and a trusted code-signing certificate. Use -PrivateUnsigned only for private testing.'}
    $SignToolPath=Assert-TerminalPath $SignToolPath
    $timestamp=[uri]$TimestampUrl
    if($timestamp.Scheme -ne 'https' -or $TimestampUrl -cnotmatch '^https://[a-zA-Z0-9.-]+(:[0-9]+)?(/[a-zA-Z0-9/_.-]*)?$'){throw 'Use a trusted HTTPS timestamp URL without shell syntax or extra parameters.'}
}
$before=Get-DraftCandidate $repoRoot
$template=Join-Path $repoRoot 'packaging/windows-pos/NexaConnect.POS.iss'
$templateHash=(Get-FileHash -LiteralPath $template -Algorithm SHA256).Hash
New-Item -ItemType Directory -Path $output | Out-Null
$payload=Join-Path $output 'payload'
& dotnet publish (Join-Path $repoRoot 'src/Clients/NexaConnect.POS') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$PackageVersion" --artifacts-path (Join-Path $output 'build') --output $payload --verbosity quiet -m:1
if($LASTEXITCODE -ne 0){throw 'Self-contained POS publication failed.'}
# Ship code only. Terminal identity/endpoints are operator-supplied public onboarding.
Get-ChildItem -LiteralPath $payload -File -Filter 'appsettings*.json' | Remove-Item
Get-ChildItem -LiteralPath $payload -File -Recurse -Filter '*.pdb' | Remove-Item
if(Test-Path -LiteralPath (Join-Path $payload '.runstate')){throw 'Unexpected local state in publication.'}
Set-Content -LiteralPath (Join-Path $payload 'installer-owner.txt') -Value 'nexaconnect-terminal-installer-v1' -NoNewline -Encoding ascii
$unsigned=0
foreach($file in Get-ChildItem -LiteralPath $payload -File -Recurse | Where-Object Extension -in @('.exe','.dll')){
    if((Get-AuthenticodeSignature -LiteralPath $file.FullName).Status -ne 'Valid'){
        if($PrivateUnsigned){$unsigned++;continue}
        & $SignToolPath sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $file.FullName | Out-Null
        if($LASTEXITCODE -ne 0 -or (Get-AuthenticodeSignature -LiteralPath $file.FullName).Status -ne 'Valid'){throw 'Payload code signing failed.'}
    }
}
$zip=Join-Path $output 'payload.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[IO.Compression.CompressionLevel]::Fastest,$false)
$installerOutput=Join-Path $output 'installer'
New-Item -ItemType Directory -Path $installerOutput | Out-Null
$parts=$PackageVersion.Split('.')
$arguments=@('/Qp',('/DPayloadZip='+$zip),('/DInstallerOutput='+$installerOutput),('/DPackageVersion='+$PackageVersion),('/DVersionMajor='+$parts[0]),('/DVersionMinor='+$parts[1]),('/DVersionPatch='+$parts[2]))
if(-not $PrivateUnsigned){
    $signCommand='"'+$SignToolPath+'" sign /sha1 '+$CertificateThumbprint+' /fd SHA256 /tr '+$TimestampUrl+' /td SHA256 $f'
    $arguments+=@('/DSignedBuild=1',('/Spublisher='+$signCommand))
}
$arguments+=$template
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$setup=Join-Path $installerOutput "NexaConnect-POS-$PackageVersion-win-x64-setup.exe"
if(-not $PrivateUnsigned -and (Get-AuthenticodeSignature -LiteralPath $setup).Status -ne 'Valid'){throw 'Installer signature validation failed.'}
Assert-TerminalCandidateUnchanged $before (Get-DraftCandidate $repoRoot)
if((Get-FileHash -LiteralPath $template -Algorithm SHA256).Hash -cne $templateHash){throw 'Installer template changed during packaging.'}
$hashes=[ordered]@{}
foreach($file in Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName){$hashes[[IO.Path]::GetRelativePath($payload,$file.FullName).Replace('\','/')]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
@{format=1;packageVersion=$PackageVersion;sourceRevision=$before.revision;sourceDirty=$before.dirty;sourceInputHash=$before.inputHash;installerTemplateHash=$templateHash;runtime='win-x64 self-contained .NET 10 Windows Desktop';privateUnsigned=[bool]$PrivateUnsigned;unsignedPayloadPeCount=$unsigned;setupSha256=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash;compilerSha256=(Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash;payloadFiles=$hashes;installed=$false;storeSubmitted=$false}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'installer-candidate.json')
Write-Output "Private installer candidate: $setup"
Write-Output 'No installation, account registration, publication or recovery migration was performed.'
