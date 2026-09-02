[CmdletBinding()]
param(
    [string] $RealmFile
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RealmFile)) {
    $RealmFile = Join-Path $PSScriptRoot '..\docker\keycloak\realm\nexa-dev-realm.json'
}

$values = [ordered]@{
    NEXA_REALM_NAME = 'nexa-validation'
    NEXA_REALM_DISPLAY_NAME = 'Nexa Validation'
    NEXA_VERIFY_EMAIL = 'true'
    NEXA_REQUIRE_MFA = 'true'
    NEXA_SMTP_HOST = 'smtp.example.test'
    NEXA_SMTP_PORT = '587'
    NEXA_SMTP_FROM = 'noreply@example.test'
    NEXA_SMTP_FROM_DISPLAY_NAME = 'Nexa Validation'
    NEXA_SMTP_AUTH = 'true'
    NEXA_SMTP_USER = 'validation-user'
    NEXA_SMTP_PASSWORD = 'validation-password'
    NEXA_SMTP_STARTTLS = 'true'
    NEXA_SMTP_SSL = 'false'
    NEXACONNECT_WEB_BFF_CLIENT_SECRET = 'validation-web-secret'
    NEXACONNECT_ADMIN_BFF_CLIENT_SECRET = 'validation-admin-secret'
    NEXACONNECT_POS_SERVICE_CLIENT_SECRET = 'validation-pos-workload-secret'
    NEXACONNECT_CATALOG_SERVICE_CLIENT_SECRET = 'validation-catalog-workload-secret'
    NEXACONNECT_ORDER_SERVICE_CLIENT_SECRET = 'validation-order-workload-secret'
    NEXACONNECT_INVENTORY_SERVICE_CLIENT_SECRET = 'validation-inventory-workload-secret'
    NEXACONNECT_KITCHEN_SERVICE_CLIENT_SECRET = 'validation-kitchen-workload-secret'
    NEXACONNECT_PAYMENT_SERVICE_CLIENT_SECRET = 'validation-payment-workload-secret'
    NEXACONNECT_MEDIA_SERVICE_CLIENT_SECRET = 'validation-media-workload-secret'
    PLATFORM_DIRECTORY_ADMIN_CLIENT_SECRET = 'validation-platform-directory-admin-secret'
    PLATFORM_ADMIN_BFF_CLIENT_SECRET = 'validation-platform-admin-secret'
    PLATFORM_ADMIN_BFF_REDIRECT_URI = 'https://platform.example.test/signin-oidc'
    PLATFORM_ADMIN_BFF_ORIGIN = 'https://platform.example.test'
    NEXACONNECT_WEB_BFF_REDIRECT_URI = 'https://app.example.test/signin-oidc'
    NEXACONNECT_WEB_BFF_POST_LOGOUT_REDIRECT_URI = 'https://app.example.test/signout-callback-oidc'
    NEXACONNECT_WEB_ORIGIN = 'https://app.example.test'
    NEXACONNECT_ADMIN_BFF_REDIRECT_URI = 'https://admin.example.test/signin-oidc'
    NEXACONNECT_ADMIN_BFF_POST_LOGOUT_REDIRECT_URI = 'https://admin.example.test/signout-callback-oidc'
    NEXACONNECT_ADMIN_ORIGIN = 'https://admin.example.test'
    NEXACONNECT_MOBILE_REDIRECT_URI = 'nexaconnect://oauth/callback'
    NEXACONNECT_POS_REDIRECT_URI = 'nexaconnect-pos://oauth/callback'
}

$rendered = Get-Content -LiteralPath $RealmFile -Raw
foreach ($entry in $values.GetEnumerator()) {
    $rendered = $rendered.Replace(('${' + $entry.Key + '}'), $entry.Value)
}

$unresolved = [regex]::Matches($rendered, '\$\{[A-Z0-9_]+\}').Value | Sort-Object -Unique
if ($unresolved.Count -gt 0) {
    throw "Unresolved realm placeholders: $($unresolved -join ', ')"
}

$realm = $rendered | ConvertFrom-Json

if ($realm.realm -ne $values.NEXA_REALM_NAME) {
    throw 'Realm name was not rendered correctly.'
}

$clientIds = @($realm.clients.clientId)
$requiredClients = @(
    'nexaconnect-api',
    'nexaconnect-web-bff',
    'nexaconnect-admin-bff',
    'nexaconnect-mobile',
    'nexaconnect-pos'
    'platform-admin-bff'
    'nexaconnect-pos-service'
    'nexaconnect-catalog-service'
    'nexaconnect-order-service'
    'nexaconnect-inventory-service'
    'nexaconnect-kitchen-service'
    'nexaconnect-payment-service'
    'nexaconnect-media-service'
)

$missingClients = @($requiredClients | Where-Object { $_ -notin $clientIds })
if ($missingClients.Count -gt 0) {
    throw "Missing required clients: $($missingClients -join ', ')"
}

$requiredRoles = @(
    'platform-owner', 'platform-admin', 'platform-support', 'platform-auditor',
    'customer-owner', 'customer-admin', 'customer-manager', 'customer-user', 'customer-viewer'
)
$realmRoles = @($realm.roles.realm.name)
$missingRoles = @($requiredRoles | Where-Object { $_ -notin $realmRoles })
if ($missingRoles.Count -gt 0) {
    throw "Missing required Phase 2 roles: $($missingRoles -join ', ')"
}

$apiScope = @($realm.clientScopes | Where-Object name -eq 'nexaconnect-api')
$roleMapper = @($apiScope.protocolMappers | Where-Object name -eq 'realm-roles')
if ($roleMapper.Count -ne 1 -or $roleMapper[0].config.'access.token.claim' -ne 'true' -or
    $roleMapper[0].config.'id.token.claim' -ne 'true' -or $roleMapper[0].config.multivalued -ne 'true') {
    throw 'The API realm-role mapper must emit a multi-valued roles claim in access and ID tokens.'
}

$webBff = @($realm.clients | Where-Object clientId -eq 'nexaconnect-web-bff')
$webSubject = @($webBff.protocolMappers | Where-Object protocolMapper -eq 'oidc-sub-mapper')
$webAudience = @($webBff.protocolMappers | Where-Object {
    $_.protocolMapper -eq 'oidc-audience-mapper' -and $_.config.'included.custom.audience' -eq 'nexaconnect-api'
})
if ($webBff.Count -ne 1 -or 'basic' -notin @($webBff[0].defaultClientScopes) -or
    $webSubject.Count -ne 1 -or $webSubject[0].config.'access.token.claim' -ne 'true' -or
    $webAudience.Count -ne 1 -or $webAudience[0].config.'access.token.claim' -ne 'true') {
    throw 'The Customer BFF client must emit subject and nexaconnect-api audience access-token claims.'
}

$orderClient = @($realm.clients | Where-Object clientId -eq 'nexaconnect-order-service')
$orderAudience = @($orderClient.protocolMappers | Where-Object {
    $_.protocolMapper -eq 'oidc-audience-mapper' -and $_.config.'included.custom.audience' -eq 'nexaconnect-api'
})
if ($orderClient.Count -ne 1 -or $orderAudience.Count -ne 1 -or
    $orderAudience[0].config.'access.token.claim' -ne 'true') {
    throw 'The Order workload client must emit the nexaconnect-api audience in access tokens.'
}

$publicClients = @($realm.clients | Where-Object publicClient)
foreach ($client in $publicClients) {
    if ($client.attributes.'pkce.code.challenge.method' -ne 'S256') {
        throw "Public client '$($client.clientId)' does not require PKCE S256."
    }
}

$unsafeClients = @($realm.clients | Where-Object { $_.directAccessGrantsEnabled -eq $true -or $_.implicitFlowEnabled -eq $true })
if ($unsafeClients.Count -gt 0) {
    throw "Unsafe OAuth flows are enabled for: $(($unsafeClients.clientId) -join ', ')"
}

Write-Output "Realm template is valid: $($realm.realm)"
Write-Output "Clients: $($clientIds -join ', ')"
