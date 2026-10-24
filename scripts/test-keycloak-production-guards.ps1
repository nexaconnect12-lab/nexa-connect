[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repoRoot ('.runstate/phase6-identity-guards/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$values = [ordered]@{}
foreach ($line in Get-Content -LiteralPath (Join-Path $repoRoot '.env.production.example')) {
    if ($line -match '^([A-Z0-9_]+)=(.*)$') { $values[$Matches[1]] = $Matches[2].Replace('example.com','nexa.test') }
}
foreach ($key in @($values.Keys)) {
    if ($key -match '(PASSWORD|CLIENT_SECRET)$') { $values[$key] = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
    if ($values[$key] -eq 'InjectFromSecretManager') { $values[$key] = 'synthetic-fixture' }
}
$cases = @(
    @{ Name = 'complete'; Pass = $true; Change = { param($v) } },
    @{ Name = 'missing-media'; Pass = $false; Change = { param($v) $v.Remove('NEXACONNECT_MEDIA_SERVICE_CLIENT_SECRET') } },
    @{ Name = 'missing-owner'; Pass = $false; Change = { param($v) $v.Remove('PLATFORM_ADMIN_BFF_CLIENT_SECRET') } },
    @{ Name = 'missing-directory'; Pass = $false; Change = { param($v) $v.Remove('PLATFORM_DIRECTORY_ADMIN_CLIENT_SECRET') } },
    @{ Name = 'shared-workload'; Pass = $false; Change = { param($v) $v.NEXACONNECT_ORDER_SERVICE_CLIENT_SECRET = $v.NEXACONNECT_POS_SERVICE_CLIENT_SECRET } },
    @{ Name = 'short-media'; Pass = $false; Change = { param($v) $v.NEXACONNECT_MEDIA_SERVICE_CLIENT_SECRET = 'short' } },
    @{ Name = 'owner-http'; Pass = $false; Change = { param($v) $v.PLATFORM_ADMIN_BFF_ORIGIN = 'http://owner.nexa.test' } },
    @{ Name = 'owner-shared-origin'; Pass = $false; Change = { param($v) $v.PLATFORM_ADMIN_BFF_ORIGIN = $v.NEXACONNECT_WEB_ORIGIN } },
    @{ Name = 'owner-wildcard'; Pass = $false; Change = { param($v) $v.PLATFORM_ADMIN_BFF_REDIRECT_URI = 'https://owner.nexa.test/*' } }
)
$validator = Join-Path $PSScriptRoot 'validate-keycloak-production.ps1'
foreach ($case in $cases) {
    $copy = [ordered]@{}; foreach ($key in $values.Keys) { $copy[$key] = $values[$key] }
    & $case.Change $copy
    $envPath = Join-Path $testRoot ($case.Name + '.env')
    $copy.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value } | Set-Content -LiteralPath $envPath
    $passed = $false
    try { & $validator -EnvironmentFile $envPath *> $null; $passed = $true } catch { $passed = $false }
    if ($passed -ne $case.Pass) { throw "Identity guard failed: $($case.Name)" }
}
$realmText = Get-Content -LiteralPath (Join-Path $repoRoot 'docker/keycloak/realm/nexa-dev-realm.json') -Raw
$realm = $realmText.Replace('${NEXA_VERIFY_EMAIL}', 'true').Replace('${NEXA_REQUIRE_MFA}', 'true') | ConvertFrom-Json
$owner = @($realm.clients | Where-Object clientId -eq 'platform-admin-bff')[0]
if ($owner.attributes.'post.logout.redirect.uris' -cne '${PLATFORM_ADMIN_BFF_ORIGIN}/signout-callback-oidc') { throw 'Owner logout callback is not exact.' }
Write-Output 'Production identity guards: 10 passed. Synthetic credentials only; no identity deployment or login performed.'
