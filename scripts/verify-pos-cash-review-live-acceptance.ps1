[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [Guid] $CashSessionId,
    [switch] $ConfirmAccountantReadOnly,
    [switch] $ConfirmManagerWorkflow,
    [switch] $ConfirmRestartRecovery,
    [switch] $ConfirmConcurrencyConflict,
    [switch] $ConfirmLateSettlementInvalidation,
    [switch] $ConfirmSignedOut,
    [ValidateRange(3, 20)]
    [int] $MinimumDecisionCount = 3
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $ConfirmAccountantReadOnly -or -not $ConfirmManagerWorkflow -or
    -not $ConfirmRestartRecovery -or -not $ConfirmConcurrencyConflict -or
    -not $ConfirmLateSettlementInvalidation -or -not $ConfirmSignedOut) {
    throw 'Accountant read-only, manager workflow, restart recovery, concurrency conflict, late-settlement invalidation, and final sign-out must all be explicitly confirmed.'
}
if ($CashSessionId -eq [Guid]::Empty) { throw 'CashSessionId must be a non-empty UUID.' }

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$composeFile = Join-Path $root 'docker-compose.yml'
if (-not (Test-Path -LiteralPath $composeFile -PathType Leaf)) {
    throw "The repository Compose file was not found at '$composeFile'."
}
if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('DOCKER_HOST'))) {
    throw 'Refusing acceptance verification while DOCKER_HOST is set. Use local Docker Desktop explicitly.'
}

$docker = Get-Command docker -ErrorAction SilentlyContinue
$dockerPath = if ($null -ne $docker) { $docker.Source } else {
    Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\resources\bin\docker.exe'
}
if (-not (Test-Path -LiteralPath $dockerPath -PathType Leaf)) {
    throw 'Docker CLI was not found on PATH or in the standard Docker Desktop installation directory.'
}
$context = (& $dockerPath context show 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $context -notin @('default', 'desktop-linux')) {
    throw "Refusing Docker context '$context'; expected local Docker Desktop."
}
$dockerEndpoint = (& $dockerPath context inspect $context --format '{{.Endpoints.docker.Host}}' 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $dockerEndpoint -notin @(
    'npipe:////./pipe/dockerDesktopLinuxEngine', 'npipe:////./pipe/docker_engine')) {
    throw "Refusing Docker endpoint '$dockerEndpoint'; expected a local Docker Desktop named pipe."
}

$sessionText = $CashSessionId.ToString('D')
$sql = @"
WITH review AS (
  SELECT session.id, session.concurrency_version AS session_version, session.variance_amount,
         state.status, state.reviewed_session_version, state.concurrency_version AS review_version
  FROM cash_sessions session
  LEFT JOIN cash_session_review_states state ON state.cash_session_id = session.id
  WHERE session.id = '$sessionText'::uuid AND session.status = 'closed'
), history AS (
  SELECT * FROM cash_session_review_history WHERE cash_session_id = '$sessionText'::uuid
)
SELECT json_build_object(
  'sessionCount', (SELECT count(*) FROM review),
  'nonzeroVarianceCount', (SELECT count(*) FROM review WHERE variance_amount <> 0),
  'currentApprovedCount', (
    SELECT count(*) FROM review
    WHERE status = 'approved' AND reviewed_session_version = session_version
  ),
  'historyCount', (SELECT count(*) FROM history),
  'investigateCount', (SELECT count(*) FROM history WHERE decision = 'investigate'),
  'approveCount', (SELECT count(*) FROM history WHERE decision = 'approve'),
  'financialVersionCount', (SELECT count(DISTINCT session_version) FROM history),
  'invalidDecisionIdentityCount', (
    SELECT count(*) FROM history
    WHERE id = '00000000-0000-0000-0000-000000000000'::uuid
       OR authorization_decision_id = '00000000-0000-0000-0000-000000000000'::uuid
  ),
  'appendOnlyTriggerCount', (
    SELECT count(*) FROM pg_trigger
    WHERE tgname = 'cash_session_review_history_append_only'
      AND tgrelid = 'public.cash_session_review_history'::regclass
      AND tgenabled IN ('O', 'A') AND NOT tgisinternal
  )
)::text;
"@
$output = & $dockerPath compose --project-name nexa-connect --project-directory $root `
    -f $composeFile exec -T postgres psql -X -q -A -t -v ON_ERROR_STOP=1 `
    -U postgres -d NexaConnect_POS -c $sql 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($output -join ''))) {
    throw 'POS cash-review database verification failed. Confirm local Docker is running and POS migration 5 is applied to NexaConnect_POS.'
}
try { $review = (($output -join '') | ConvertFrom-Json) }
catch { throw 'POS cash-review database verification returned an invalid result.' }

if ([int]$review.sessionCount -ne 1 -or [int]$review.nonzeroVarianceCount -ne 1 -or
    [int]$review.currentApprovedCount -ne 1 -or [int]$review.historyCount -lt $MinimumDecisionCount -or
    [int]$review.investigateCount -lt 1 -or [int]$review.approveCount -lt 2 -or
    [int]$review.financialVersionCount -lt 2 -or [int]$review.invalidDecisionIdentityCount -ne 0 -or
    [int]$review.appendOnlyTriggerCount -ne 1) {
    throw 'POS Cash Review acceptance failed: expected current approval, immutable investigate/approve history, and decisions spanning the late financial version.'
}

$localStateDirectory = Join-Path ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)) 'NexaConnect\POS'
$requiredAbsent = @('tokens.bin', 'state.json', 'cash-session.json', 'pending-checkout.bin', 'pending-settlement.bin', 'outbox.json')
$remaining = @($requiredAbsent | Where-Object { Test-Path -LiteralPath (Join-Path $localStateDirectory $_) })
if ($remaining.Count -ne 0) {
    throw "Local POS cleanup or SQLite migration is incomplete. Resolve these files through the WPF flow: $($remaining -join ', ')."
}
$localDatabase = Join-Path $localStateDirectory 'pos-state.db'
$inspectorProject = Join-Path $root 'src\Tools\NexaConnect.PosLocalStateInspector\NexaConnect.PosLocalStateInspector.csproj'
$inspectionOutput = & dotnet run --project $inspectorProject --configuration Release `
    --no-launch-profile --verbosity quiet -- --database $localDatabase 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($inspectionOutput -join ''))) {
    throw 'The local POS SQLite database could not be inspected.'
}
$inspectionJson = @($inspectionOutput | Where-Object { $_ -match '^\s*\{' } | Select-Object -Last 1)
try { $inspection = (($inspectionJson -join '') | ConvertFrom-Json) }
catch { throw 'The local POS SQLite inspector returned an invalid result.' }
if (-not $inspection.integrityOk -or [int]$inspection.schemaVersion -ne 2 -or
    [int]$inspection.operationalStateCount -ne 0 -or [int]$inspection.pendingCashReviewCount -ne 0 -or
    [int]$inspection.unresolvedOutboxCount -ne 0 -or [int]$inspection.interruptedSendCount -ne 0) {
    throw 'Local recovery acceptance failed: require schema 2, integrity success, and no operational, cash-review, or unresolved outbox state.'
}

$run = Join-Path $root ('.runstate/pos-cash-review-live/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$evidence = [ordered]@{
    runId = Split-Path $run -Leaf
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    cashSessionId = $sessionText
    accountantReadOnlyConfirmed = $true
    managerWorkflowConfirmed = $true
    restartRecoveryConfirmed = $true
    concurrencyConflictConfirmed = $true
    lateSettlementInvalidationConfirmed = $true
    signedOutConfirmed = $true
    currentApprovalVerified = $true
    decisionCount = [int]$review.historyCount
    financialVersionCount = [int]$review.financialVersionCount
    immutableHistoryTriggerVerified = $true
    localSqliteSchemaVersion = 2
    localSqliteIntegrityVerified = $true
    localSqliteOperationalStateCount = 0
    localPendingCashReviewCount = 0
    localSqliteUnresolvedOutboxCount = 0
    dockerContext = $context
    secretsPrinted = $false
}
$evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'evidence.json') -Encoding utf8
Write-Output "POS Cash Review live acceptance passed. Sanitized evidence retained at '$run'."
