[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [Guid] $OrderId,
    [switch] $ConfirmInteractiveOidc,
    [switch] $ConfirmWpfCashCheckout,
    [switch] $ConfirmSignedOut,
    [ValidateRange(1, 120)]
    [int] $ProjectionWaitSeconds = 30
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $ConfirmInteractiveOidc -or -not $ConfirmWpfCashCheckout -or -not $ConfirmSignedOut) {
    throw 'Interactive OIDC, the WPF cash-checkout sequence, and final sign-out must all be explicitly confirmed.'
}
if ($OrderId -eq [Guid]::Empty) { throw 'OrderId must be a non-empty UUID.' }

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
SELECT json_build_object(
  'orderCount', (SELECT count(*) FROM orders WHERE id = '$orderText'::uuid),
  'completedOrderCount', (SELECT count(*) FROM orders WHERE id = '$orderText'::uuid AND status = 'completed'),
  'cashSettlementCount', (
    SELECT count(*)
    FROM order_manual_tender_settlements settlement
    JOIN orders customer_order ON customer_order.id = settlement.order_id
    WHERE settlement.order_id = '$orderText'::uuid
      AND settlement.method = 'cash'
      AND btrim(settlement.currency) = 'THB'
      AND settlement.amount = customer_order.total_amount
      AND settlement.receipt_confirmed = false
      AND settlement.bank_reference IS NULL
  ),
  'settlementAmount', (
    SELECT max(settlement.amount)
    FROM order_manual_tender_settlements settlement
    JOIN orders customer_order ON customer_order.id = settlement.order_id
    WHERE settlement.order_id = '$orderText'::uuid
      AND settlement.method = 'cash'
      AND btrim(settlement.currency) = 'THB'
      AND settlement.amount = customer_order.total_amount
      AND settlement.receipt_confirmed = false
      AND settlement.bank_reference IS NULL
  ),
  'settlementId', (SELECT id::text FROM order_manual_tender_settlements WHERE order_id = '$orderText'::uuid),
  'organizationId', (SELECT organization_id::text FROM order_manual_tender_settlements WHERE order_id = '$orderText'::uuid),
  'branchId', (SELECT branch_id::text FROM order_manual_tender_settlements WHERE order_id = '$orderText'::uuid),
  'terminalId', (SELECT terminal_id::text FROM order_manual_tender_settlements WHERE order_id = '$orderText'::uuid)
)::text;
"@
$order = Invoke-LocalJsonQuery 'NexaConnect_Order' $orderSql
if ([int]$order.orderCount -ne 1 -or [int]$order.completedOrderCount -ne 1 -or
    [int]$order.cashSettlementCount -ne 1) {
    throw 'Order acceptance failed: expected one completed order with one exact-total THB cash settlement.'
}

$posSql = @"
WITH projection AS (
  SELECT settlement_id, organization_id, branch_id, terminal_id, cash_session_id, amount
  FROM pos_order_settlements
  WHERE order_id = '$orderText'::uuid AND method = 'cash' AND btrim(currency) = 'THB'
), matched_lifecycle AS (
  SELECT projection.cash_session_id
  FROM projection
  JOIN cash_movements movement
    ON movement.order_id = '$orderText'::uuid
   AND movement.cash_session_id = projection.cash_session_id
   AND movement.payment_id = projection.settlement_id
   AND movement.movement_type = 'sale'
   AND movement.reason_code = 'ORDER_MANUAL_TENDER'
   AND movement.amount = projection.amount
  JOIN cash_sessions session
    ON session.id = projection.cash_session_id
   AND session.status = 'closed'
  JOIN shifts shift
    ON shift.id = session.shift_id
   AND shift.store_id = session.store_id
   AND shift.status = 'closed'
)
SELECT json_build_object(
  'projectionCount', (SELECT count(*) FROM projection),
  'projectionAmount', (SELECT max(amount) FROM projection),
  'settlementId', (SELECT settlement_id::text FROM projection),
  'organizationId', (SELECT organization_id::text FROM projection),
  'branchId', (SELECT branch_id::text FROM projection),
  'terminalId', (SELECT terminal_id::text FROM projection),
  'cashSaleCount', (
    SELECT count(*) FROM cash_movements
    WHERE order_id = '$orderText'::uuid
      AND movement_type = 'sale'
      AND reason_code = 'ORDER_MANUAL_TENDER'
  ),
  'matchedLifecycleCount', (SELECT count(*) FROM matched_lifecycle),
  'closedCashSessionCount', (
    SELECT count(*) FROM projection item
    JOIN cash_sessions session ON session.id = item.cash_session_id
    WHERE session.status = 'closed'
  ),
  'closedShiftCount', (
    SELECT count(*) FROM projection item
    JOIN cash_sessions session ON session.id = item.cash_session_id
    JOIN shifts shift ON shift.id = session.shift_id AND shift.store_id = session.store_id
    WHERE shift.status = 'closed'
  )
)::text;
"@

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($ProjectionWaitSeconds)
do {
    $pos = Invoke-LocalJsonQuery 'NexaConnect_POS' $posSql
    if ([int]$pos.projectionCount -eq 1 -and [int]$pos.cashSaleCount -eq 1 -and
        [int]$pos.matchedLifecycleCount -eq 1) { break }
    Start-Sleep -Seconds 1
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if ([int]$pos.projectionCount -ne 1 -or [int]$pos.cashSaleCount -ne 1 -or
    [int]$pos.matchedLifecycleCount -ne 1 -or [int]$pos.closedCashSessionCount -ne 1 -or
    [int]$pos.closedShiftCount -ne 1) {
    throw 'POS acceptance failed: expected one amount-matched projected cash sale linked to its closed cash session and shift.'
}
if ([decimal]$order.settlementAmount -ne [decimal]$pos.projectionAmount) {
    throw 'Financial acceptance failed: the Order settlement and POS projection amounts do not match.'
}
foreach ($field in @('settlementId', 'organizationId', 'branchId', 'terminalId')) {
    if ([string]$order.$field -ne [string]$pos.$field) {
        throw "Projection acceptance failed: Order and POS disagree on $field."
    }
}

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
if (-not $inspection.integrityOk -or [int]$inspection.schemaVersion -ne 1 -or
    [int]$inspection.operationalStateCount -ne 0 -or [int]$inspection.unresolvedOutboxCount -ne 0 -or
    [int]$inspection.interruptedSendCount -ne 0) {
    throw 'Local POS SQLite acceptance failed: require schema 1, integrity success, and no active operational or unresolved outbox rows.'
}

$run = Join-Path $root ('.runstate/pos-cashier-live/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$evidence = [ordered]@{
    runId = Split-Path $run -Leaf
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    orderId = $orderText
    interactiveOidcConfirmed = $true
    wpfCashCheckoutConfirmed = $true
    signedOutConfirmed = $true
    completedOrderCount = 1
    cashSettlementCount = 1
    posProjectionCount = 1
    cashSaleCount = 1
    matchedCashLifecycleCount = 1
    cashSessionClosed = $true
    shiftClosed = $true
    localCredentialsAndRecoveryCleared = $true
    localSqliteSchemaVersion = 1
    localSqliteIntegrityVerified = $true
    localSqliteOperationalStateCount = 0
    localSqliteUnresolvedOutboxCount = 0
    dockerContext = $context
    secretsPrinted = $false
}
$evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'evidence.json') -Encoding utf8
Write-Output "POS cashier live acceptance passed. Sanitized evidence retained at '$run'."
