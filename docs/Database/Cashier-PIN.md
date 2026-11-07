# POS cashier PIN persistence

POS migration26 `cashier_pins` adds:

- `cashier_pin_credentials`: organization/configured issuer/subject primary key; state, credential version, Data-Protection-protected verifier, failure count and lockout deadline.
- `cashier_pin_audit`: immutable per-key operation ID, actor/action, live authorization decision, terminal scope, expected/result version, outcome and observation timestamp. No PIN, hash-derived request fingerprint or bearer is stored. UPDATE/DELETE/TRUNCATE are rejected.

Authorization17 backfills `pos.cashier-pin.use` for existing cashier/store-manager/tenant-admin roles, and organization-wide `pos.cashier-pin.manage` for tenant-admin only. New role defaults match the migration. No other identity role or financial permission changes.

Minimum application compatibility for these migrations is **0.45.0**. At introduction
the full migration catalog had 223 tables, including these two additions. The standalone
launcher defaults to 0.38.0, so explicitly select the compatibility required by the
target. First-draft tooling discovers it dynamically. Deploy matching service and
native binaries with the owning schema.

Infrastructure parameterizes all runtime data. An advisory transaction lock over the complete credential key serializes missing-row enrollment and subsequent transitions. Aggregate version checks, credential state/counters and audit insertion commit atomically. Concurrent initial enrollments have one winner; failed/stale transitions cannot overwrite it. Duplicate IDs never replay successful verification and do not consume another attempt. No financial table, event, SQLite recovery, credential custody or payment request changes.

Empty26→25→26 downgrade/reapply is supported. Any credential or audit row refuses downgrade: retain/restore the complete database and POS Data Protection key ring, use forward correction and explicit admin reset if required. Never silently delete security history to roll back. Authorization downgrade removes these grants/overrides only after dependent routes are disabled. Review audit-retention/export policy before production; automatic destructive retention is not implemented.

`CashierPinPersistenceTests`, included in `scripts/test-qr-entry-persistence.ps1`, uses a generated disposable loopback database/schema. It verifies concurrent enrollment, injection-shaped subjects, organization/issuer/subject isolation, duplicate failures, concurrent lockout, restart, reset, successful-attempt non-replay, immutable audit and downgrade guards. Real Data Protection identity/key-ring separation is tested separately in unit tests. See [identity](../Identity/POS-Session-PIN.md).
