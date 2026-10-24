param([Parameter(Mandatory)][string]$InstallerDirectory)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Signature validation requires Windows.'}
$receipt=Get-Content -LiteralPath (Join-Path $InstallerDirectory 'installer-candidate.json') -Raw | ConvertFrom-Json
if($receipt.privateUnsigned -isnot [bool] -or $receipt.privateUnsigned -or $receipt.unsignedPayloadPeCount -ne 0){throw 'A trusted signed candidate is required.'}
if($receipt.packageVersion -cnotmatch '^\d{1,4}\.\d{1,4}\.\d{1,4}$'){throw 'Invalid installer identity.'}
$setup=Join-Path $InstallerDirectory "installer/NexaConnect-POS-$($receipt.packageVersion)-win-x64-setup.exe"
$files=@(Get-Item -LiteralPath $setup)+@(Get-ChildItem -LiteralPath (Join-Path $InstallerDirectory 'payload') -File -Recurse | Where-Object Extension -in '.exe','.dll')
foreach($file in $files){if((Get-AuthenticodeSignature -LiteralPath $file.FullName).Status -ne 'Valid'){throw 'Trusted code-signature validation failed.'}}
Write-Output 'Trusted installer/payload signature validation passed.'
