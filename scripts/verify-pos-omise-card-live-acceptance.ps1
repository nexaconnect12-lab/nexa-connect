[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [Guid] $OrderId,
    [Parameter(Mandatory)] [Guid] $ClosedShiftId,
    [switch] $ConfirmInteractiveOidc,
    [switch] $ConfirmWpfCardCheckout,
    [switch] $ConfirmSignedOut
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $ConfirmInteractiveOidc -or -not $ConfirmWpfCardCheckout -or -not $ConfirmSignedOut) {
    throw 'Interactive OIDC, the WPF test-card checkout sequence, and final sign-out must all be explicitly confirmed.'
}
if ($OrderId -eq [Guid]::Empty -or $ClosedShiftId -eq [Guid]::Empty) { throw 'OrderId and ClosedShiftId must be non-empty UUIDs.' }

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$composeFile = Join-Path $root 'docker-compose.yml'
if (-not (Test-Path -LiteralPath $composeFile -PathType Leaf)) {
    throw "The repository Compose file was not found at '$composeFile'."
}

$dockerHost = [Environment]::GetEnvironmentVariable('DOCKER_HOST')
if (-not [string]::IsNullOrWhiteSpace($dockerHost)) {
    throw 'Refusing acceptance verification while DOCKER_HOST is set. Use the local Docker Desktop context explicitly.'
}

$docker = Get-Command docker -ErrorAction SilentlyContinue
if ($null -ne $docker) {
    $dockerPath = $docker.Source
}
else {
    $dockerPath = Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\resources\bin\docker.exe'
    if (-not (Test-Path -LiteralPath $dockerPath -PathType Leaf)) {
        throw 'Docker CLI was not found on PATH or in the standard Docker Desktop installation directory.'
    }
}

$context = (& $dockerPath context show 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $context -notin @('default', 'desktop-linux')) {
    throw "Refusing Docker context '$context'; expected local Docker Desktop context 'default' or 'desktop-linux'."
}
$dockerEndpoint = (& $dockerPath context inspect $context --format '{{.Endpoints.docker.Host}}' 2>$null).Trim()
$allowedDockerEndpoints = @(
    'npipe:////./pipe/dockerDesktopLinuxEngine',
    'npipe:////./pipe/docker_engine'
)
if ($LASTEXITCODE -ne 0 -or $dockerEndpoint -notin $allowedDockerEndpoints) {
    throw "Refusing Docker endpoint '$dockerEndpoint'; expected a local Docker Desktop named pipe."
}

$orderText = $OrderId.ToString('D')

function Invoke-LocalJsonQuery([string] $Database, [string] $Sql) {
    $output = & $dockerPath compose --project-name nexa-connect --project-directory $root `
        -f $composeFile exec -T postgres psql -X -q -A -t `
        -v ON_ERROR_STOP=1 -U postgres -d $Database -c $Sql 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($output -join ''))) {
        throw "Local database verification failed for $Database."
    }
    try { return (($output -join '') | ConvertFrom-Json) }
    catch { throw "Local database verification returned an invalid result for $Database." }
}

$orderSql = @"
BEGIN READ ONLY;
SELECT json_build_object(
 'orderCount',count(*),'completedOrderCount',count(*) FILTER(WHERE status='completed' AND workflow_payment_method='card_omise_test' AND btrim(currency)='THB'),
 'intentId',max(payment_intent_id::text),'organizationId',max(organization_id::text),
 'restaurantId',max(restaurant_id::text),'branchId',max(branch_id::text),'amount',max(total_amount),
 'manualSettlementCount',(SELECT count(*) FROM order_manual_tender_settlements WHERE order_id='$orderText'::uuid)
) FROM orders WHERE id='$orderText'::uuid;
COMMIT;
"@
$order = Invoke-LocalJsonQuery 'NexaConnect_Order' $orderSql
if ([int]$order.orderCount -ne 1 -or [int]$order.completedOrderCount -ne 1 -or [int]$order.manualSettlementCount -ne 0) {
 throw 'Card acceptance failed: require one completed test-card Order without manual settlement.'
}
$intentId = [Guid]::Parse($order.intentId).ToString('D')
if ([Guid]$intentId -eq [Guid]::Empty) { throw 'A linked Payment intent is required.' }
$paymentSql = @"
BEGIN READ ONLY;
SELECT json_build_object(
 'intentCount',count(*),
 'capturedCount',count(*) FILTER(WHERE id='$intentId'::uuid AND status='captured' AND payment_method='card' AND btrim(currency)='THB' AND captured_at_utc IS NOT NULL
 AND provider_void_id IS NULL AND provider_authorization_id ~ '^chrg_(test_)?[a-z0-9]{10,64}$' AND provider_capture_id=provider_authorization_id),
 'organizationId',max(organization_id::text),'restaurantId',max(restaurant_id::text),'branchId',max(branch_id::text),'amount',max(amount),
 'authorizationStarts',(SELECT count(*) FROM payment_audit_records WHERE payment_intent_id='$intentId'::uuid AND action='payment.authorization.started'),
 'captureStarts',(SELECT count(*) FROM payment_audit_records WHERE payment_intent_id='$intentId'::uuid AND action='payment.capture.started')
) FROM payment_intents WHERE order_id='$orderText'::uuid;
COMMIT;
"@
$payment = Invoke-LocalJsonQuery 'NexaConnect_Payment' $paymentSql
if ([int]$payment.intentCount -ne 1 -or [int]$payment.capturedCount -ne 1 -or [int]$payment.authorizationStarts -ne 1 -or [int]$payment.captureStarts -ne 1 -or [decimal]$payment.amount -ne [decimal]$order.amount) {
 throw 'Card acceptance failed: require one exact-total captured intent with one authorization and capture start.'
}
foreach ($field in @('organizationId','restaurantId','branchId')) {
 if ([string]$order.$field -ne [string]$payment.$field) { throw 'Order/Payment ownership mismatch.' }
}
$shiftText = $ClosedShiftId.ToString('D')
$restaurantText = [Guid]::Parse($order.restaurantId).ToString('D')
$branchText = [Guid]::Parse($order.branchId).ToString('D')
$shiftSql = @"
BEGIN READ ONLY;
SELECT json_build_object('closedShiftCount',count(*))
FROM shifts s JOIN stores store ON store.id=s.store_id
WHERE s.id='$shiftText'::uuid AND s.status='closed'
AND store.restaurant_id='$restaurantText'::uuid AND store.branch_id='$branchText'::uuid;
COMMIT;
"@
$shift = Invoke-LocalJsonQuery 'NexaConnect_POS' $shiftSql
if ([int]$shift.closedShiftCount -ne 1) { throw 'Require the operator-supplied closed shift in the matching restaurant/branch.' }
$localStateDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'NexaConnect\POS'
$requiredAbsent = @('tokens.bin', 'state.json', 'cash-session.json', 'pending-checkout.bin', 'pending-settlement.bin', 'outbox.json')
$remaining = @($requiredAbsent | Where-Object { Test-Path -LiteralPath (Join-Path $localStateDirectory $_) })
if ($remaining.Count -ne 0) {
    throw "Local POS cleanup or SQLite migration is incomplete. Resolve these legacy/current files through the WPF flow: $($remaining -join ', ')."
}
$localDatabase = Join-Path $localStateDirectory 'pos-state.db'
$inspectorProject = Join-Path $root 'src\Tools\NexaConnect.PosLocalStateInspector\NexaConnect.PosLocalStateInspector.csproj'
$inspectionOutput = & dotnet run --project $inspectorProject --configuration Release `
    --no-launch-profile --verbosity quiet -- --database $localDatabase 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($inspectionOutput -join ''))) {
    throw 'The local POS SQLite database could not be inspected. Build dependencies and complete migration before acceptance.'
}
$inspectionJson = @($inspectionOutput | Where-Object { $_ -match '^\s*\{' } | Select-Object -Last 1)
try { $inspection = (($inspectionJson -join '') | ConvertFrom-Json) }
catch { throw 'The local POS SQLite inspector returned an invalid result.' }
if (-not $inspection.integrityOk -or [int]$inspection.schemaVersion -ne 2 -or
    [int]$inspection.operationalStateCount -ne 0 -or [int]$inspection.unresolvedOutboxCount -ne 0 -or
    [int]$inspection.pendingCashReviewCount -ne 0 -or [int]$inspection.interruptedSendCount -ne 0) {
    throw 'Local POS SQLite acceptance failed: require schema 2, integrity success, and no active operational, cash-review recovery, or unresolved outbox rows.'
}

$run = Join-Path $root ('.runstate/pos-omise-card-live/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
[ordered]@{
 completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
 orderId=$orderText
 closedShiftId=$shiftText
 interactiveOidcConfirmed=$true
 wpfCardCheckoutConfirmed=$true
 signedOutConfirmed=$true
 completedOrderCount=1
 capturedIntentCount=1
 amountAndOwnershipMatched=$true
 authorizationStarts=1
 captureStarts=1
 suppliedShiftClosed=$true
 orderShiftLinkVerified=$false
 localCredentialsAndRecoveryCleared=$true
 readOnly=$true
 financialCommandsSent=0
 remoteProviderStatusVerified=$false
 hostedInterruptionVerified=$false
 secretsPrinted=$false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'evidence.json') -Encoding utf8
Write-Output "POS Omise card acceptance passed. Sanitized local evidence retained at '$run'."

