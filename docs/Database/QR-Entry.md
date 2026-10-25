# Restaurant QR1 persistence

QR3 separately adds Order-owned quote/request storage; Restaurant admission remains
unchanged. QR4 adds Order acceptance storage and a scoped Restaurant admission read; no
Restaurant migration. The disposable launcher requires four cases; earlier QR1/QR2 receipts
retain their historical counts. See [Order storage](QR-Requests.md).


QR2 alone adds no tables/migrations: its cart/quotes are transient and use existing Catalog
organization/branch menu reads and Restaurant pricing policy. Catalog's guest projection
adds a parameterized organization+branch query with LIMIT 201 to detect its 200-item cap;
see [QR2](../Deployment/QR2-Menu.md). The shared disposable persistence launcher now requires
QR1 admission, QR2 Catalog isolation/bounds, QR3 Order request and QR4 Inventory/Kitchen
recovery cases; prior QR1 receipts
retain their historical one-case meaning. QR3 adds pending Order request storage; no
executable Order/Inventory/Kitchen/payment state is written by QR3 submission alone.
See [QR4 storage](QR-Acceptance.md) for the separate accepted handoff.

Apply Restaurant migration 4 (`0004_qr_entry`) with the migration owner's credentials
before enabling QR entry. Minimum migration application version stays 0.38.0; this is
an additive Restaurant capability, with no coordinated financial schema/version change.
Existing branch configuration, floor records, Order/POS/Payment and settlement history
remain untouched. Restaurant now has eleven catalog tables (seven initial, branch audit,
three QR tables); at QR1 introduction the full catalog added three tables to the prior
201 = 204. QR3 increased the catalog to207, QR4 to208 and QR5 to the current209 tables.

- `restaurant_qr_entries`: organization/restaurant/branch, staff table code, SHA-256 entry
  hash, version, active state, absolute expiry. Unique branch/code and hash, branch/restaurant
  composite FK; active ownership is independently validated by Application and live joins.
- `restaurant_qr_sessions`: visitor UUID, exact entry/version, SHA-256 session hash,
  created/expiry timestamps and ended state. No username, IP, profile, order, bearer token or cookie data.
- `restaurant_qr_audit`: immutable administration UUID/owner/branch/entry/version/action,
  actor subject and time. No QR/session secret, label or financial body. Mutation trigger
  rejects UPDATE/DELETE; audit/outbox append in the same transaction as the entry mutation.

Infrastructure owns all parameterized SQL. Administrative code normalizes/validates
scope and evaluates current `restaurant.configuration.manage`; database predicates are
support, not the only authorization. Domain `QrEntry` protects version transitions and
`QrGuestAdmission` protects budgets. Branch-row locking serializes entry changes and
admission counts across instances; a second versioned writer loses without side effects.
Context reads compare exact hash and entry version in one current statement, including
active Restaurant/branch ownership and both expiries. A committed rotation/revocation is
effective on the next read, without a stale grant cache.

Admission deletes expired/obsolete-version hashes only for its current branch, then
checks Domain limits inside the same transaction and inserts one independent visitor.
An explicit logout marks only its supplied session hash ended, immediately removing its
context authority. Ended hashes preserve the rolling one-minute admission budget; later
admission prunes them once older than that window. Active-session counts exclude ended
visits. Inactive branches receive no
new admissions; their expired hashes may remain until an operator's authorized retention
cleanup or later active admission. Entry and administration audit history is retained;
there is no runtime destructive entry delete or audit purge API.

Grant the existing least-privilege Restaurant runtime role SELECT/INSERT/UPDATE on entries,
SELECT/INSERT/UPDATE/DELETE on sessions, INSERT/SELECT on QR audit, and its existing audit-outbox
capabilities. It needs SELECT/row-lock access to owning branches/restaurants and no access
to other service databases. Migration ownership must remain separate. Do not grant audit
mutation privileges or use owner/postgres credentials in production runtime.

Down SQL explicitly destroys QR audit/sessions/entries and requires the migration tool's
destructive/backup confirmations. Disable guest traffic first. Other branch/floor/financial
records survive; re-upgrade starts empty. A restored backup can revive old entry secrets:
delete restored session hashes under authorized maintenance and rotate entries before
reopening admission. This is not a protected Windows POS recovery migration.

Run [the disposable persistence gate](../Deployment/QR1-Entry.md#verification) to verify
tenant/visitor isolation, restart reads, expiry, hierarchy denial, version contention,
concurrent quota admission, audit/outbox rollback and empty downgrade/re-upgrade.
