[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $EnvironmentFile
)

$ErrorActionPreference = 'Stop'

$resolvedEnvironmentFile = (Resolve-Path -LiteralPath $EnvironmentFile).Path
$values = @{}

foreach ($line in Get-Content -LiteralPath $resolvedEnvironmentFile) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
        continue
    }

    $parts = $trimmed.Split('=', 2)
    if ($parts.Count -ne 2 -or [string]::IsNullOrWhiteSpace($parts[0])) {
        throw "Invalid environment-file entry."
    }

    $values[$parts[0].Trim()] = $parts[1].Trim()
}

$required = @(
    'KEYCLOAK_DB_URL',
    'KEYCLOAK_DB_USERNAME',
    'KEYCLOAK_DB_PASSWORD',
    'KEYCLOAK_NETWORK',
    'KEYCLOAK_PUBLIC_URL',
    'KEYCLOAK_ADMIN_URL',
    'KEYCLOAK_PROXY_TRUSTED_ADDRESSES',
    'KEYCLOAK_TEMP_ADMIN_USERNAME',
    'KEYCLOAK_TEMP_ADMIN_PASSWORD',
    'KEYCLOAK_REALM',
    'KEYCLOAK_SMTP_HOST',
    'KEYCLOAK_SMTP_FROM',
    'KEYCLOAK_SMTP_USER',
    'KEYCLOAK_SMTP_PASSWORD',
    'KEYCLOAK_TEMP_ADMIN_PASSWORD',
    'NEXACONNECT_WEB_BFF_CLIENT_SECRET',
    'NEXACONNECT_ADMIN_BFF_CLIENT_SECRET',
    'NEXACONNECT_WEB_BFF_REDIRECT_URI',
    'NEXACONNECT_WEB_BFF_POST_LOGOUT_REDIRECT_URI',
    'NEXACONNECT_WEB_ORIGIN',
    'NEXACONNECT_ADMIN_BFF_REDIRECT_URI',
    'NEXACONNECT_ADMIN_BFF_POST_LOGOUT_REDIRECT_URI',
    'NEXACONNECT_ADMIN_ORIGIN',
    'NEXACONNECT_MOBILE_REDIRECT_URI',
    'NEXACONNECT_POS_REDIRECT_URI'
)

$missing = @($required | Where-Object {
    -not $values.ContainsKey($_) -or [string]::IsNullOrWhiteSpace($values[$_])
})
if ($missing.Count -gt 0) {
    throw "Missing production settings: $($missing -join ', ')"
}

$placeholderKeys = @($required | Where-Object {
    $values[$_] -match 'ReplaceWith|InjectFrom|example\.(com|test|invalid)'
})
if ($placeholderKeys.Count -gt 0) {
    throw "Placeholder production values remain for: $($placeholderKeys -join ', ')"
}

$secretKeys = @(
    'KEYCLOAK_DB_PASSWORD',
    'KEYCLOAK_SMTP_PASSWORD',
    'NEXACONNECT_WEB_BFF_CLIENT_SECRET',
    'NEXACONNECT_ADMIN_BFF_CLIENT_SECRET'
)
foreach ($key in $secretKeys) {
    if ($values[$key].Length -lt 32) {
        throw "$key must contain at least 32 characters."
    }
}

if ($values.NEXACONNECT_WEB_BFF_CLIENT_SECRET -eq $values.NEXACONNECT_ADMIN_BFF_CLIENT_SECRET) {
    throw 'Every confidential client must use a distinct secret.'
}

$httpsKeys = @(
    'KEYCLOAK_PUBLIC_URL',
    'KEYCLOAK_ADMIN_URL',
    'NEXACONNECT_WEB_BFF_REDIRECT_URI',
    'NEXACONNECT_WEB_BFF_POST_LOGOUT_REDIRECT_URI',
    'NEXACONNECT_WEB_ORIGIN',
    'NEXACONNECT_ADMIN_BFF_REDIRECT_URI',
    'NEXACONNECT_ADMIN_BFF_POST_LOGOUT_REDIRECT_URI',
    'NEXACONNECT_ADMIN_ORIGIN'
)
foreach ($key in $httpsKeys) {
    $uri = $null
    if (-not [Uri]::TryCreate($values[$key], [UriKind]::Absolute, [ref] $uri) -or $uri.Scheme -ne 'https') {
        throw "$key must be an absolute HTTPS URI."
    }
}

if ($values.KEYCLOAK_PUBLIC_URL -eq $values.KEYCLOAK_ADMIN_URL) {
    throw 'Public and administrative Keycloak URLs must be different.'
}

if ($values.KEYCLOAK_DB_URL -notmatch '(?i)(sslmode=verify-(full|ca)|ssl=true)') {
    throw 'KEYCLOAK_DB_URL must explicitly require verified TLS.'
}

if ($values.KEYCLOAK_PROXY_TRUSTED_ADDRESSES -match '(^|,)\s*(0\.0\.0\.0/0|::/0)\s*(,|$)') {
    throw 'KEYCLOAK_PROXY_TRUSTED_ADDRESSES must not trust every address.'
}

& (Join-Path $PSScriptRoot 'test-keycloak-realm.ps1')

Write-Output 'Production Keycloak configuration passed static validation.'
