# POS UX8C — drawer history and read-only recovery review

## Status and staff workflow

Implemented in source. This adds authorized server history, protected lookup metadata for
future local attempts and staff review without UUID entry. It never opens a drawer, renews,
resets or retries an intent, reports an outcome or changes payment. Production still uses
DisabledDrawerDriver. Subsequent [UX8D](POS-UX8D-Pilot-Acceptance.md) installed the matching
history-only pilot and POS25 / Authorization16 on 30 October 2026. Signed-in operator,
financial/device and physical acceptance remain pending.

1. Sign in as the original cashier. Open **Terminal & sync → Cash drawer → Drawer attempt
   history — read only**. An open shift/cash session or physical driver is not required.
2. Select last24hours,7days or30days and **Load / refresh history**. Each load requires live
   `pos.drawer.read`, active Restaurant hierarchy and the exact configured POS store/terminal
   relationship. Managers read their own attempts, not other cashiers.
3. Select a row and **Check selected original record**. The row carries its original operation
   internally; no UUID input is required. Both list and selected review are GETs.
4. Compare local and server observations. Reserved is consumed; Submitted is a reported
   submission, not proof of physical opening; Unknown is uncertain; Cancelled remains consumed.
   A missing server record is not proof no attempt occurred. Preserve evidence and investigate;
   do not repeat a device command or collect payment again to repair it.

Review never overwrites either outcome or marks Unknown resolved. Distinct, deliberate manual
opening policy from UX8B is unchanged; history is not a global lockout of all future operations.

## Bounds, authorization and privacy

POS rechecks live hierarchy/permission and terminal relationship before and after database
I/O, including missing selected records. Queries always bind organization/restaurant/branch/
store/terminal and authenticated subject. Historical reads do not require open custody or an
active terminal while its original relationship, active Restaurant hierarchy and live read
permission still match. External authorization is check-time, not atomic distributed revocation.

Server windows are at most31days, page sizes1–50(native25), ordered by reserved timestamp/UUID.
The context-bound opaque cursor is not a credential/signature; every page reauthorizes. Native
display caps at200server plus50local rows. UI provides recent1/7/30day windows; older retained
history can be queried through an authorized bounded API window. Displayed timestamps are UTC;
local attempt timestamps use the device clock, server reservations use database time.

Native HTTP denies cookies/redirects, bounds complete operations to10seconds and successful
list/selected bodies to64KiB/4KiB. Current subject/issuer/unlocked session, response scope,
window and original facts are checked. Only an authorized server list permits local reads;
there is no offline cached fallback on outage/denial. Local corruption can leave authorized
server rows available with a warning, without changing an intent.

Lock/sign-out, cashier/privacy generation, period changes and authorization failures clear
rows/selection/cursor. Cancellation and generation checks suppress late responses. Loading
more preserves an already checked server observation when a page repeats local-only data.
Every displayed status remains an observation, never physical proof or an opening grant.

## Persistence and rollout

Apply **POS25**, minimum application compatibility **0.40.0**, with matching service/native
binaries. The migration launcher still defaults to0.38.0; explicitly select0.40.0. POS25 adds
one index, no server table/permission. Authorization16 remains applicable. Subsequent UX8D
permits list/selected review under default-off `DrawerIntents:HistoryEnabled` or existing
`DrawerIntents:Enabled`; only the latter enables command/legacy-read routes. Use the
[history-only pilot rollout](POS-UX8D-Pilot-Acceptance.md) to retain disabled opening authority.
Populated25→24→25 index-only rollback preserves all intent/outcome/audit.

The native lazy `drawer-intents.db` preserves existing hash-based deduplication. Future factory
reservations atomically add `drawer_history_context`: hashed issuer/full-scope/actor lookup plus
current-user Windows-DPAPI protected original request/timestamp(version1). No tokens, amounts,
bank/card/provider data or device commands are added. Lookup identities are protected, not
plaintext. Bounds are8KiB plaintext/16KiB protected, with issuer/scope/actor/key/fingerprint/
timestamp validation on read. This is not tamper-proof against the Windows user or server
authority. Preserve journal/context and Windows-user protection keys with recovery.

History opens existing SQLite read-only and never creates directories/files/tables or backfills
opaque rows. Missing/legacy context cannot yield an invented operation ID; old rows stay consumed.
Legacy constructors without issuer still deduplicate without context capture; production
supplies issuer/DPAPI. With DisabledDrawerDriver no attempt/context is written by default.
A protection failure rolls back local reservation before HTTP/device submission. Never remove
the journal to repeat an action. See [local storage](../Database/POS-Drawer-Intents.md).

## Verification and diagnostics

The complete Windows QR6 gate requires new history unit/HTTP/native coverage and four owning
drawer PostgreSQL cases with cleanup. Cases cover context-bound cursors/window limits, actor/
scope/issuer isolation, revocation during I/O, DPAPI restart recovery, no-create legacy reads,
GET-only transport, non-mutating review and delayed responses after lock/cashier change.
No retained database, real drawer or payment is used. Installation and physical/operator
acceptance remain separate from source tests.

Candidate `851f87f5-ce98-4156-ac50-cbc10223ef02` completed the full source gate on
30 October 2026 at 06:20 Singapore time: 1,332 unit tests, six architecture checks,
90 HTTP cases, 90 frontend tests, 66 QR browser cases and the required Windows, hosting,
security/profile and financial browser checks passed. Nine QR owning cases verified cleanup
and broker recovery; four separately hashed drawer owning cases verified cleanup, scoped
history pagination and index downgrade/reapply. The verification receipt records POS25 and
`productionVerified=false`; the candidate remains `local-test-candidate` with
`releaseReady=false`. Remote CI, hosting, installer, device and operator acceptance remain
separate. This source-only result did not install or migrate the retained pilot, enable a
physical driver, or exercise a real drawer/payment.

Use service `nexaconnect-pos` Drawer history authorization/unavailable events and
`nexaconnect-pos-client` Drawer client/history response/local-read categories with validated
correlation IDs. Never log bodies, subjects, tokens, arbitrary headers, cursors or protected
context. For unavailable history check POS25/matching services, feature enablement and live
read permission; preserve the journal and original Windows-user keys on local warnings.

See [API](../API/Drawer-Intents.md), [UX8B](POS-UX8B-Native-Drawer.md) and
[ADR-057](../Architecture/Decisions/ADR-057-drawer-history-review.md).
