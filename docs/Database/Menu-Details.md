# Catalog9 — menu details versions and audit

Catalog9 (`0009_menu_details`, compatibility **0.44.0**) adds
`catalog_menu_items.menu_version bigint NOT NULL DEFAULT0 CHECK>=0` and append-only
`catalog_menu_details_audit`. Current Catalog31/full221 tables. Existing names, prices,
stations, currency, availability/version, photos, barcodes and layouts are preserved.
Standalone migration default remains0.38.0; explicitly select0.44.0 for the latest catalog.

Domain `MenuDetailsState` validates edited text, THB/two-decimal checkout-compatible price,
expected version, no-op and overflow without infrastructure dependencies. Application owns
live branch authorization and actor validation. Infrastructure locks the exact tenant/branch/
product row FOR UPDATE and atomically commits metadata/version, private before/after audit,
`catalog.audit.v1` and existing `catalog.menu-item.changed.v1` outbox events. Missing products
are not created. All runtime SQL values are parameters. A competing availability change uses
the same row lock while owning different columns/versions; neither command resets the other.
The changed-item event keeps aggregate type `catalog-menu-item`; the generic editor audit
uses `catalog-menu-details`. Existing event consumers retain their stable envelope.

Audit JSON contains only editor-owned metadata/currency/version, excluding availability.
Scoped product/version uniqueness and UPDATE/DELETE refusal protect history; no-op adds no
audit/outbox. Failed audit/outbox rolls back the metadata change. Existing product metadata
upsert increments menu_version only when name/price/currency/station actually differ and
preserves availability/version; it retains its existing generic audit/full-item event path.
Version gaps in detailed editor history may therefore represent legitimate legacy writes.
Development in-memory state has equivalent mutation locking/version rules but no durable audit.

Use existing Catalog runtime provisioning for menu SELECT/UPDATE and audit INSERT. There is
no new cross-database FK, service credential, stock mutation or financial schema change.
Owning tests cover concurrent CAS, scope isolation, exact no-op, failure rollback, stable
photo/layout/barcode references, legacy invalidation, audit tamper refusal and empty9→8→9.
Downgrade refuses any detailed audit or nonzero menu_version, including legacy writes. Preserve
history and forward-fix; never delete audit or reset versions to force rollback. A dedicated
audit-history UI, revisions/restoration and bulk edits are outside UX14.
See [API](../API/Menu-Details.md) and [operations](../Deployment/POS-UX14-Menu-Details.md).
