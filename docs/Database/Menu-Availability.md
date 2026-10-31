# Catalog8 — branch menu availability

Catalog8 (`0008_menu_availability`, minimum application compatibility **0.43.0**) adds
`catalog_menu_items.availability_version bigint NOT NULL DEFAULT0 CHECK>=0` and the
append-only `catalog_menu_availability_audit` table. Existing availability values, prices,
photos, layout and product membership are preserved. Catalog now owns30 tables; the full
catalog has220. The standalone runner default remains0.38.0: select0.43.0 explicitly.

The domain `MenuAvailabilityState` owns expected-version equality, Boolean transitions,
no-op and overflow rules. Application owns live branch read/write authorization and actor
validation. Infrastructure locks the exact tenant/branch/product row `FOR UPDATE`, checks
the domain transition and commits flag/version, audit and `catalog.audit.v1` outbox in one
transaction. A stale writer loses without altering state. Missing product cannot be created
by an availability command. All runtime SQL values are parameters. Product metadata upsert
preserves both availability and its version, and returns/publishes the actual retained flag.

Audit stores previous/current Boolean, unique scoped product/version, actor subject,
correlation and occurrence time; UPDATE/DELETE triggers reject mutation. Generic audit
publication contains no menu payload or payment data. This slice adds no dedicated audit
history UI or availability-specific integration event; POS/Order read current Catalog state.
Development in-memory storage is volatile and does not provide durable audit/outbox.

Use existing Catalog runtime provisioning: menu reads/updates and audit INSERT, without
auditor mutation authority. No cross-database FK/transaction or Inventory quantity write is
added. Owning tests cover concurrent writers, no-op, scope isolation, rollback when outbox
fails, metadata upsert preservation, audit tamper refusal and empty8→7→8. Downgrade refuses
any availability history/nonzero version; retain data and use forward recovery. Do not delete
audit or reset versions to force rollback. See [API](../API/Menu-Availability.md) and
[deployment](../Deployment/POS-UX13-Menu-Availability.md).
