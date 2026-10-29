# Catalog barcode persistence

Catalog migration **5**, compatibility **0.38.0**, adds three tables (Catalog 25 tables;
current full service catalog 212). Apply before exposing the new routes/POS scan mode.
Existing simplified branch menus remain the product source; no other owner's database is
read directly and no existing menu/QR/financial rows are rewritten.

- `catalog_barcode_registry`: primary key organization+case-sensitive C-collated barcode;
  retains its product ID. A composite unique key supports the binding FK.
- `catalog_branch_barcodes`: primary key organization+branch+product, nullable barcode,
  monotonic version; FK to the exact Catalog menu row and organization/code/product registry.
  Organization+branch+barcode is unique. Null retains a cleared binding's version.
- `catalog_barcode_audit`: immutable before/after code, version, exact scope, actor,
  correlation and occurrence. Organization+branch+product+version is unique; a trigger
  rejects UPDATE/DELETE.

Application/Domain validates current versions and product ownership. Infrastructure opens
one transaction and takes a tenant-local `pg_advisory_xact_lock` on a stable parameterized
organization hash before reading current binding/registry. The transaction invokes the
Application-provided Domain decision under that lock, mutates binding and registry, releases
only unreferenced registry codes and appends detailed audit plus `catalog.audit.v1` outbox.
All runtime SQL values are parameterized. Readers join exact organization/branch/product
to the current menu. Key-share locking protects the menu FK during mutation. The tenant
lock covers inserts, cross-branch claims and release; hash collisions only serialize extra
organizations, without changing authorization or keys.

Provision only the Catalog runtime in this owning database. It needs menu SELECT, registry
SELECT/INSERT/DELETE, binding SELECT/INSERT/UPDATE, detailed audit INSERT (optional SELECT
for authorized operations) and existing outbox privileges. Never grant DDL/trigger-disable,
cross-database access or audit mutation. Existing local draft default grants are broader
within each isolated service database; append-only triggers remain authoritative there.

Identical values with current version do not mutate or emit audit. Uncertain save outcomes
require reload; optimistic versions reject stale commands. Clearing one branch does not
release a code used elsewhere. Historical audit remains when a code is fully released.
5→4 refuses any barcode binding/registry/audit history, including cleared tombstones;
empty downgrade/reapply is tested. Disable new routes/clients first; use forward recovery
after real use. Migration history checksum rules remain unchanged.

`BarcodePersistenceTests` runs in an owned disposable schema through the guarded QR
persistence runner: concurrent claims have one winner, same-product cross-branch sharing,
tenant isolation, stale conflicts, partial release, outbox rollback, append-only audit and
history-blocked downgrade. In-memory development mode has equivalent assignment rules
under a lock but no durable audit/outbox. See [UX6](../Deployment/POS-UX6-Barcode-Scanning.md).
