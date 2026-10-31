# ADR-061: safe branch menu details editing

Status: Accepted — UX14 source implementation.

## Context

Managers can manage menu photos, organization and availability but need an existing-item
name/price/station editor. The legacy upsert has no expected-version contract. Blind writes
must not let an old reviewed draft overwrite a later metadata update or reset sold-out state.
Accepted Order/receipt/payment data belongs to its original immutable commercial snapshot.

## Decision

Keep details/version on the scoped Catalog menu row, independent of availability and related
selection versions. Use a small framework-free domain model, live authorized application
use case and infrastructure row-lock/CAS transaction with detailed audit and versioned outbox.
Keep currency immutable in the THB editor and reuse existing priced-checkout range/precision.
The protected BFF and name-based portal require before/after review then explicit confirmation.
Keep legacy callers compatible, but increment details version on their actual metadata edits
and preserve availability. Their existing audit/event behavior remains; detailed history is
owned by the new editor rather than inferred retrospectively.

## Consequences

Stale/uncertain saves require reload. Concurrent availability changes remain independent;
photo/category/barcode references retain the same product identity. Existing quote fingerprint
and guest price-consent checks protect new orders, while accepted snapshots and recovery are
never repriced. This does not introduce a distributed admission lock, new-item creation,
currency conversion, modifiers, combos, discounts or inventory changes. Migration9 requires
0.44.0 and history blocks downgrade. Stop older writers before rollout. See
[persistence](../../Database/Menu-Details.md) and [rollout](../../Deployment/POS-UX14-Menu-Details.md).
