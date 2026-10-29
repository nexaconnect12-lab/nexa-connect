# ADR-052: Catalog-owned product barcodes and explicit scanner entry

Status: Accepted, 28 October 2026.

## Decision

Catalog owns an organization-wide barcode→product registry and versioned branch/product
bindings. A branch manager with existing live `catalog.menu.write` may change only that
branch's binding. The same product can use its code in multiple branches; the code cannot
identify another product in the same organization. Other organizations have independent
namespaces. One primary code per branch product is supported. Code case and leading zeroes
are significant; supported codes are 1–64 ASCII letters/digits/dots/underscores/hyphens.
These are identifiers, not executable URLs, payment tokens, weighted GS1 data or card logins.

Application authorizes active branch membership and menu-read/write through existing
Directory/Restaurant/Authorization adapters. Domain `BarcodeAssignment` owns version,
collision and unchanged-value decisions. Infrastructure serializes an organization's writes
with a transaction advisory lock, supplies the state to the Application/Domain decision,
and commits binding, registry, append-only detailed audit and generic Catalog audit outbox
together. Composite keys/FKs support the invariant. Removing a branch binding retains its
version tombstone; the registry code is released only after its last branch reference is
removed. A stale version is rejected, including an uncertain repeat; clients reload first.
No financial integration event or shared product domain entity is added.

Catalog migration 5 adds three owned tables. Downgrade refuses any binding/registry/audit
history; use forward recovery after use. Other owners' schemas and financial compatibility
remain unchanged. In-memory development mode uses the same Domain decisions but is not
durable and does not supply PostgreSQL audit/outbox guarantees.

Native POS scan mode is an explicit session-only opt-in, independent of touch and density.
Its lifetime is the running POS window, not one cashier authentication: lock/sign-out clears
input and pending lookup but can retain the checkbox. Restart defaults off; nothing is saved.
Only the focused barcode field accepts supported keyboard-emulating input ending in Enter,
or a manually entered code plus Add barcode. No global scanner hooks or timing-based device
identity are inferred. Each lookup POST uses the current authenticated configured branch
and returns a live menu snapshot; no barcode appears in a URL/log. Availability/currency
and original cart guards apply. Pricing/placement/payment remain the existing authoritative
checkout path. Late responses cannot add after deactivation, context change or lock.
Successful identical scans have a 750 ms repeat guard; deliberate later scans add one unit.

Keyboard-emulating devices cannot be distinguished from ordinary keyboard input. While
scan mode is enabled, POS-owned order/payment/cancellation/cash-review/display-role dialogs
use a protected WPF confirmation: text/mnemonics and Enter are consumed; deliberate click/
tap or Tab then Space remains available. Lock/session invalidation cancels it. This prevents
the supported code alphabet and Enter suffix from accepting a financial/privacy decision.
Outside the explicit field, scanner text is not captured globally. External Windows print
or browser dialogs and other applications retain their own keyboard handling. Configure
the scanner for supported codes and Enter-only suffix; no Tab/Space/control prefix/suffix.

## Consequences and alternatives

A globally mutable product barcode would require broader organization administration;
branch bindings retain existing manager authority without changing other branches' rows.
Branch-only uniqueness would allow one code to identify different products across an
organization. A server lookup avoids stale downloaded barcode maps and preserves live
authorization. Dedicated input avoids treating every rapid keyboard burst as a scanner.

Hardware model/driver/interface, suffix configuration and actual touchscreen/device tests
remain prerequisites. No drawer, scanner SDK/serial/Bluetooth protocol, offline cart lookup
or card authentication is delivered. See [API](../../API/Product-Barcodes.md),
[storage](../../Database/Product-Barcodes.md) and [UX6](../../Deployment/POS-UX6-Barcode-Scanning.md).
