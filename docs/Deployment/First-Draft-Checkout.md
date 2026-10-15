# Joined first-draft checkout (slice 3)

The guarded runner uses the [owned draft stack](First-Draft-Test-Stack.md) and
[reference provisioner](First-Draft-Provisioning.md), ordinary clocks, real Keycloak
POS PKCE identities, service-owned PostgreSQL databases and RabbitMQ. Reference data
is fictional; every shift, drawer, quote, placement, settlement, receipt and cancellation
is produced by its owning HTTP use case. No financial transitions are inserted by SQL.

```powershell
pwsh -File scripts/test-first-draft-checkout.ps1 -ConfirmDisposableInfrastructure
```

Requires .NET 10, local Docker Desktop, Node and Playwright Chromium with locked frontend
dependencies installed. It always generates its own run, applies current migrations,
builds the current services and resets only that generated project afterward. Credentials
are DPAPI protected and transiently supplied to the child process; bearer tokens remain
in memory. No screenshots, videos, traces, external provider connection or money transfer.
The private journal retains exact commands and safe failure stages; do not share raw run
directories. Bounded results are in `checkout-verification.json`.

Coverage implemented by the runner:

- Real cashier shift/drawer, price-change reconfirmation before any Order is created,
  exact/concurrent placement retries and one Inventory reservation/ticket per station.
- Actual Kitchen Start/Ready/Complete with competing expected versions, a temporary
  read-only observer override on the accountant fixture and denied mutations. That explicit
  owning Authorization fixture is isolated to this disposable run; normal accountant
  provisioning is unchanged. Kitchen completion never marks Order Paid.
- THB cash and fictional manually confirmed PromptPay, rejection without confirmation,
  exact/concurrent manual settlement retries and stable server receipts. Bank verification
  is simulated; no real PromptPay transfer or physical receipt printing is claimed.
- Insufficient stock, cancellation during preparation, exact cancellation replay and
  completed-food conflict with stock retained and `CancellationReview`. Kitchen validates
  all station tickets atomically so a completed station cannot partially cancel another.
- A separately traced ordinary decline using the **Disabled** payment adapter. It makes
  a real failed Payment intent but cannot contact a provider. No Paid receipt is created.
- A local forwarding proxy discards an actual committed placement response. The client
  restores/replays its original command; it does not create a replacement Order.
- Kitchen is stopped before placement; Order retains its durable unfinished stage. Order
  is then stopped and both services restarted; recovery/replay must preserve one reservation
  and station ticket. These are specified interruption boundaries, not every instruction
  or transaction window and not WPF/DPAPI restart acceptance.
- Broker outage permits durable checkout/manual settlement and retains unpublished outbox
  evidence. After broker recovery, exact retry, POS cash projection and owning-database
  effect counts prove no duplicate settlement, stock release, ticket or sales projection.
  The shared publisher owns its lazy reconnectable connection independently of consumer
  connections. Publisher confirms are preserved; uncertain publication may redeliver,
  while owning receipt/idempotency boundaries prevent duplicate business effects.
- Live branch/tenant denial and accountant payment denial; owning runtime queries inspect
  effect counts only. The sole policy fixture mutation is inside Authorization with quoted
  psql data parameters. No cross-service persistence mutation or time override is used.

`first-draft-stack.ps1 -Action StopService -RunId ... -Service Kitchen|Order` supports
owned interruption tests and verifies executable/PID/start identity before stopping.
Child hosts explicitly receive both ASPNETCORE_ENVIRONMENT and DOTNET_ENVIRONMENT as
Development through Start-Process environment configuration; inherited host environment
does not choose Production during restart. Generated infrastructure ports are retained
and explicitly bound on loopback, including RabbitMQ. The runner verifies the same broker
port after restart; it never hides a changed address by restarting its consumers/publishers.
Restart uses the same published artifact, retained scope and secrets. Status lists missing
expected processes as false; its retained Ready flag is a lifecycle marker, not current
workflow readiness. Restore Order's consumer before restarting Kitchen when using the
financial binding gate. Broker control is restricted to the owned RabbitMQ container.

Order's provider-failure compensation now cancels Kitchen before releasing Inventory in
placement, workflow recovery, reconciliation and manager-confirmed void paths. Both owning
operations are idempotent. An unconfirmed/blocked Kitchen cancellation retains stock and
the unpaid workflow; Inventory failure after confirmed cancellation can retry safely.
No distributed transaction is introduced. Terminal preparation returns a direct placement
409 `preparation_requires_review`; recovery records that category and leaves the unpaid
stage for investigation. Do not collect again, report cancellation success, release stock
manually or use a cash correction as a refund. Existing Payment Review remains unresolved
when its compensation fails. There is no new automated completed-food override.

The legacy provider-failure persistence mapping stores failed and rejected Orders under
the same `cancelled` status and may reload a provider failure as `Rejected`. The runner
requires the immediate `PaymentFailed` result, compensated effects and absent Paid receipt;
it does not call that legacy alias an explicit operator cancellation. Manual-tender
cancellation has its own retained operation and reloads as `Cancelled` or `CancellationReview`.
Historical unsafe releases made before this fix are not repaired or rewritten; investigate
their original stock/audit history separately.

New cashier/manager/tenant-admin assignments now include the already established
`order.cancel` permission, matching migration-8 backfill. No new schema or default financial
limit is added. Existing nexaconnect-order/kitchen/inventory/POS JSON and correlation telemetry
applies. Query `Order payment compensation blocked`, `preparation_requires_review` and
`CorrelationId`; logs exclude tokens, request bodies, bank references and monetary values.

Terminal-admission coverage is limited to the correctly provisioned active terminal and
drawer. Order checks a nonempty caller-supplied terminal identity on manual settlement;
it does not preflight terminal registration through POS. POS separately validates
terminal/store/branch/shift ownership when projecting the settlement. Forged terminal
admission and its recovery outcome remain a follow-up boundary, not verified coverage.

See [slice 3 evidence](../Architecture/Evidence/First-Draft-Slice-3.md) for executed results.
Windows/PDF printing, actual next-day reconciliation and release/UAT gates remain slices 4–7.
