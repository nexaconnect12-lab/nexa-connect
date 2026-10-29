# POS UX8A — cash-drawer authorization and audit

## Status

UX8A implements the POS-owned server authority and durable intent/audit contracts in source.
Physical opening stays disabled. No native payment hook, manual-open button, driver, RAW
printer command or automatic retry is enabled. UX8B still requires the exact printer model,
drawer connection, Windows driver and vendor-supported electrical/command details recorded
in [UX8](POS-UX8-Cash-Drawer.md). The retained local pilot has not been migrated/restarted
or installed for UX8A; its existing payments and approved QR configuration are untouched.

## Implemented boundaries

Follow-up: [UX8B software preparation](POS-UX8B-Native-Drawer.md) now adds native consumption
of these unchanged contracts with disabled hardware. The source implementation/status above
describes UX8A's own delivery; no physical opening or retained-pilot activation is implied.

- Reservation derives the actor from authenticated bearer identity. Restaurant hierarchy
  and live Authorization decisions must match the requested organization/restaurant/branch.
- POS locks and validates exact active store/terminal, original cashier's open shift and
  open THB cash session before committing an intent. It does not create a shift or balance.
- Manual requests require `pos.drawer.manual-open` plus one fixed reason:
  `change_required`, `cash_check`, or `supervisor_access`. A reason is not an approval.
- A cash-sale request requires `pos.drawer.cash-sale-open` and the POS-owned immutable
  confirmed cash-settlement projection for that exact organization/branch/terminal/cash
  session/Order, settled within the preceding 60 seconds. Missing/delayed/old projection,
  PromptPay, card and a client-reported Paid flag cannot authorize it. No bulk replay.
- New reservation, append-only audit and existing transactional audit outbox commit together.
  One organization/operation identifies a manual request; one organization/original Order
  identifies a sale across operation IDs and terminals. Changed original facts conflict.
- Only the first committed response has `maySubmit=true`, expiring after 60 seconds using
  database time. Reads and successful retries have `maySubmit=false`; a lost response consumes the
  intent rather than granting another opening. Expiry never deletes or renews it.
  Reservation retries also recheck current open custody and cash-sale freshness, so a closed
  shift/cash session or stale projection can return 409. Inspect/report the original operation
  instead; those paths do not require open custody and never renew the grant.
- Original actors can read their intent with live `pos.drawer.read`; reporting requires
  current original-cause permission. One terminal client-reported outcome is append-only:
  `submitted`, `unknown`, or `cancelled`. Same-outcome retry is idempotent; changing it
  conflicts. Reporting after custody closes is allowed because it records a past attempt
  without granting a new opening. Revocation/outage leaves the reserved intent consumed.

`submitted` means client-reported command submission, never confirmed physical opening.
There is no distributed transaction between Authorization, Restaurant, PostgreSQL and a
future device. Authorization is checked before the database transaction; it can change
afterward. UX8B must check current session/lock, the original reservation and expiry before
one driver call, retain local deduplication and report uncertainty without retrying hardware.
Receipt reprint, PromptPay/card confirmation, replayed cash response and generic recovery
have no drawer hook in this implementation.

## Deployment prerequisites

Deploy matching POS/Authorization binaries and apply **POS 24 / Authorization 16**, minimum
application compatibility **0.39.0**, using the owning migration tool and normal backups.
The migration launcher still defaults to **0.38.0**; specify **0.39.0** explicitly for these
new targets rather than treating the unchanged launcher default as compatible approval.
POS 24 adds three tables; empty 24→23→24 is supported. Any retained intent refuses downgrade.
Keep services disabled before rollback; prefer forward recovery after history exists.
Authorization 16 backfills existing product roles and new assignments use matching defaults:
tenant-admin/store-manager receive manual, cash-sale and read permissions; cashier receives
cash-sale/read only. No cashier manual-open, accountant or Kitchen grant is added.

`DrawerIntents:Enabled` defaults to false in POS. Only explicitly enable
`DrawerIntents__Enabled=true` after migrations and matched dependencies are ready. Disabled
authenticated routes return 503 `drawer_authority_disabled`; this switch does not enable
hardware or give a user permission. Existing `Services:Restaurant`, `Services:Authorization`,
workload identity, database/runtime login and outbox configuration are reused. No public
guest/BFF route or new credential is introduced. Outbox dispatch/monitoring should remain
enabled for centralized audit delivery; local intent/audit commit remains atomic if broker
delivery is delayed. Restrict runtime DDL privileges and retain backups of all intent history.

## Verification and operations

Complete source-bound candidate `5ea5f4a1-dc38-48a6-8a9f-13408c3b8d52` passed 1,287 unit
tests, six architecture checks, 84 HTTP cases, 90 frontend tests and 66 QR browser cases,
with the required Windows, hosting, security and profile gates. The nine-case QR owning
fixture verified cleanup/broker recovery; the separately hashed three-case drawer owning
fixture verified cleanup (`e1dc425ee4ae4a53961836739ba744f7`). This is local implementation
evidence, not deployment or physical acceptance. No retained pilot migration, drawer-route
enablement, native adapter installation or hardware opening was performed for UX8A.

```powershell
pwsh -NoProfile -File scripts/test-pos-drawer-authority.ps1 -ConfirmDisposableInfrastructure
```

The runner uses generated local PostgreSQL names, runs three owning cases and removes only
its own fixture. Cases apply the full POS catalog and empty rollback/reapply, verify concurrent
single-winner reservations, immutable retries/reports, custody/projection rejection, independent
permissions, and reservation/outcome rollback when audit-outbox insertion fails. No physical
printer/drawer or retained business database is used. `DrawerAuthorityTests` and
`DrawerIntentBoundaryTests` cover domain invariants, live revocation, identity, default-off,
safe errors and no renewed grants. Existing drawer workflow tests cover lock/cancel/crash
and local duplicate suppression. The complete QR6 gate includes these unit/HTTP cases and
a separately hashed drawer owning receipt with verified cleanup.

Service `nexaconnect-pos` retains shared JSON/OTLP request correlation. Query validated
correlation ID and fixed `Drawer intent` authorization/conflict/unavailable messages. The
durable `drawer_intent_audit` and `pos.audit.v1` outbox retain operation/actor/decision/stage;
diagnostic logs exclude subjects, tokens, cookies, bank data, commands and arbitrary headers.
An unavailable/uncertain reserve or report must be inspected by original operation ID;
never clear an intent, invent another operation or replay a device command to repair it.

See [API](../API/Drawer-Intents.md), [server persistence](../Database/Drawer-Intents.md),
[local preparation](../Database/POS-Drawer-Intents.md) and
[ADR-055](../Architecture/Decisions/ADR-055-drawer-authority.md).
