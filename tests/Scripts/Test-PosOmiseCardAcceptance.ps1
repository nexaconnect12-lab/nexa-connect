#Requires -Version 7.0
param([switch]$VerifyLocalSql)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$path = Join-Path $root 'scripts/verify-pos-omise-card-live-acceptance.ps1'
$source = Get-Content $path -Raw
$errors = $null; $tokens = $null
[void][Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Verifier syntax invalid.' }
# Exercise the production acceptance assertions with isolated database-result fixtures.
# This does not certify SQL execution, live OIDC, UI interaction or provider status.
$start = $source.IndexOf('$orderSql =')
$end = $source.IndexOf('$localStateDirectory =')
$checks = [scriptblock]::Create($source.Substring($start,$end-$start))
$orderText = [Guid]::NewGuid().ToString('D')
$ClosedShiftId = [Guid]::NewGuid()
$owner = [Guid]::NewGuid().ToString('D')
if ($VerifyLocalSql) {
 if ($env:DOCKER_HOST) { throw 'DOCKER_HOST must be unset.' }
 $dockerCommand=Get-Command docker -ErrorAction SilentlyContinue
 $script:dockerPath=if ($dockerCommand) {$dockerCommand.Source} else {Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'}
 $context=(& $script:dockerPath context show).Trim()
 $endpoint=(& $script:dockerPath context inspect $context --format '{{.Endpoints.docker.Host}}').Trim()
 if ($LASTEXITCODE -ne 0 -or $context -notin @('default','desktop-linux') -or $endpoint -notin @('npipe:////./pipe/dockerDesktopLinuxEngine','npipe:////./pipe/docker_engine')) { throw 'Require local Docker Desktop.' }
}
function Invoke-LocalJsonQuery([string]$Database,[string]$Sql) {
 if ($Sql -notmatch 'BEGIN READ ONLY;' -or $Sql -notmatch 'COMMIT;') { throw 'Expected a read-only transaction.' }
 if ($VerifyLocalSql) {
  # These session-only tables shadow every queried relation. No product data is read or written.
  $orderStatus=if ($script:orderFixture.completedOrderCount -eq 1) {'completed'} else {'payment_pending'}
  $paymentStatus=if ($script:paymentFixture.capturedCount -eq 1) {'captured'} else {'authorized'}
  $shiftStatus=if ($script:shiftFixture.closedShiftCount -eq 1) {'closed'} else {'open'}
  $fixtureSql=@"
SET search_path TO pg_temp;
CREATE TEMP TABLE orders(id uuid,status text,workflow_payment_method text,currency text,payment_intent_id uuid,organization_id uuid,restaurant_id uuid,branch_id uuid,total_amount numeric);
INSERT INTO orders VALUES('$orderText','$orderStatus','card_omise_test','THB','$($script:orderFixture.intentId)','$owner','$owner','$owner',50);
CREATE TEMP TABLE order_manual_tender_settlements(order_id uuid);
INSERT INTO order_manual_tender_settlements SELECT '$orderText'::uuid FROM generate_series(1,$($script:orderFixture.manualSettlementCount));
CREATE TEMP TABLE payment_intents(id uuid,order_id uuid,status text,payment_method text,currency text,captured_at_utc timestamptz,provider_void_id text,provider_authorization_id text,provider_capture_id text,organization_id uuid,restaurant_id uuid,branch_id uuid,amount numeric);
INSERT INTO payment_intents SELECT '$($script:orderFixture.intentId)'::uuid,'$orderText'::uuid,'$paymentStatus','card','THB',now(),NULL,'chrg_test_aaaaaaaaaaaa','chrg_test_aaaaaaaaaaaa','$($script:paymentFixture.organizationId)'::uuid,'$owner'::uuid,'$owner'::uuid,$($script:paymentFixture.amount) FROM generate_series(1,$($script:paymentFixture.intentCount));
CREATE TEMP TABLE payment_audit_records(payment_intent_id uuid,action text);
INSERT INTO payment_audit_records SELECT '$($script:orderFixture.intentId)'::uuid,'payment.authorization.started' FROM generate_series(1,$($script:paymentFixture.authorizationStarts));
INSERT INTO payment_audit_records SELECT '$($script:orderFixture.intentId)'::uuid,'payment.capture.started' FROM generate_series(1,$($script:paymentFixture.captureStarts));
CREATE TEMP TABLE stores(id uuid,restaurant_id uuid,branch_id uuid);
INSERT INTO stores VALUES('$owner','$owner','$owner');
CREATE TEMP TABLE shifts(id uuid,store_id uuid,status text);
INSERT INTO shifts VALUES('$ClosedShiftId','$owner','$shiftStatus');
"@
  $output=($fixtureSql+[Environment]::NewLine+$Sql) | & $script:dockerPath compose --project-name nexa-connect --project-directory $root -f (Join-Path $root 'docker-compose.yml') exec -T postgres psql -X -q -A -t -v ON_ERROR_STOP=1 -U postgres -d postgres
  if ($LASTEXITCODE -ne 0) { throw 'Temporary SQL fixture failed.' }
  return (($output -join '') | ConvertFrom-Json)
 }
 switch ($Database) {
  'NexaConnect_Order' { return $script:orderFixture }
  'NexaConnect_Payment' { return $script:paymentFixture }
  'NexaConnect_POS' { return $script:shiftFixture }
  default { throw 'Unexpected database.' }
 }
}
$cases = @('success','not_paid','manual_tender','duplicate_intent','not_captured','amount_mismatch','tenant_mismatch','duplicate_authorize','duplicate_capture','shift_open')
foreach ($case in $cases) {
 $script:orderFixture = [pscustomobject]@{orderCount=1;completedOrderCount=1;manualSettlementCount=0;intentId=[Guid]::NewGuid().ToString('D');organizationId=$owner;restaurantId=$owner;branchId=$owner;amount=50}
 $script:paymentFixture = [pscustomobject]@{intentCount=1;capturedCount=1;authorizationStarts=1;captureStarts=1;organizationId=$owner;restaurantId=$owner;branchId=$owner;amount=50}
 $script:shiftFixture = [pscustomobject]@{closedShiftCount=1}
 switch ($case) {
  'not_paid' {$script:orderFixture.completedOrderCount=0}
  'manual_tender' {$script:orderFixture.manualSettlementCount=1}
  'duplicate_intent' {$script:paymentFixture.intentCount=2}
  'not_captured' {$script:paymentFixture.capturedCount=0}
  'amount_mismatch' {$script:paymentFixture.amount=51}
  'tenant_mismatch' {$script:paymentFixture.organizationId=[Guid]::NewGuid().ToString('D')}
  'duplicate_authorize' {$script:paymentFixture.authorizationStarts=2}
  'duplicate_capture' {$script:paymentFixture.captureStarts=2}
  'shift_open' {$script:shiftFixture.closedShiftCount=0}
 }
 $failure = $null
 try { & $checks } catch { $failure=$_.Exception.Message }
 if ($case -eq 'success' -and $null -ne $failure) { throw "Positive fixture rejected: $failure" }
 if ($case -ne 'success' -and $null -eq $failure) { throw "Unsafe fixture accepted: $case" }
 if ($case -ne 'success' -and $failure -notmatch 'Card acceptance failed:|Order/Payment ownership mismatch|Require the operator-supplied closed shift') { throw "Unexpected fixture failure: $failure" }
}
# Early guards must reject before Docker, persistence or provider access.
foreach ($case in @('missing_confirmation','empty_order','empty_shift')) {
 $orderId = [Guid]::NewGuid(); $shiftId=[Guid]::NewGuid()
 if ($case -eq 'empty_order') {$orderId=[Guid]::Empty}
 if ($case -eq 'empty_shift') {$shiftId=[Guid]::Empty}
 $failed=$false
 try {
  if ($case -eq 'missing_confirmation') { & $path -OrderId $orderId -ClosedShiftId $shiftId }
  else { & $path -OrderId $orderId -ClosedShiftId $shiftId -ConfirmInteractiveOidc -ConfirmWpfCardCheckout -ConfirmSignedOut }
 } catch {
  $failed=$true
  if ($_.Exception.Message -notmatch 'explicitly confirmed|non-empty UUIDs') { throw }
 }
 if (-not $failed) { throw "Early guard accepted: $case" }
}
if ($VerifyLocalSql) { Write-Output 'POS Omise verifier: 10 PostgreSQL temporary-fixture cases and 3 early guards passed. No product data or provider access.' }
else { Write-Output 'POS Omise verifier: 10 isolated financial/ownership fixtures and 3 early guards passed. No live database or provider access.' }
