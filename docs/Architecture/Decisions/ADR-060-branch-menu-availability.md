# ADR-060: versioned branch menu availability

Status: Accepted — UX13 source implementation.

## Context

Catalog already supplies branch selling availability to POS, barcode lookup, QR quotes and
staff acceptance. Managers lack a safe name-based editor. Metadata upsert previously set the
flag true, which would silently undo a sold-out decision. Inventory remains stock authority;
accepted Order and Payment history must not follow later menu edits.

## Decision

Keep manual availability on the Catalog organization/branch/product row with its own version
and small domain transition model. Use per-item CAS with an owning row lock and atomic
append-only audit/outbox, not a second availability projection or whole-menu replacement.
Preserve the flag/version on metadata edits. Reuse live `catalog.menu.read/write` and the
protected correlated BFF, with explicit manager confirmation and refresh-driven readers.
Keep existing Order validation/replay checkpoints: pending requests can fail acceptance;
already accepted commands retain their original snapshots and recovery identities.

## Consequences

Managers can restore availability without changing stock, prices or layout. Concurrent/stale
edits require reload, and lost-response retries do not silently advance audit. Migration8
requires0.43.0 and retained history blocks downgrade. Existing per-service boundaries remain:
a concurrently admitted order is not retrospectively cancelled. No automatic stock-based
sold-out logic, scheduled reopening, distributed selling lock, bulk edit, new role or live
push notification is introduced. See [persistence](../../Database/Menu-Availability.md) and
[rollout](../../Deployment/POS-UX13-Menu-Availability.md).
