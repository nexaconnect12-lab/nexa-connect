# ADR-058: Catalog-approved menu photo references with private Media delivery

- **Status:** Accepted
- **Date:** 2026-10-30
- **Scope:** Catalog, Media, Order, Customer BFF, guest menu and Windows POS

## Decision

Catalog owns one versioned primary photo reference per organization/branch/product. Media continues to own file bytes, original owner, safety processing and deletion. Managers select products by name, preview a ready product asset and explicitly approve its public menu use. Reuse within an organization does not transfer the Media owner. Catalog commits the domain version decision, selection, immutable local audit and generic audit outbox atomically under the owning menu row lock.

Selected-photo delivery is a narrow, bounded processed-PNG path. Exact Order workload can read Catalog selected photos; exact Catalog workload can read ready Catalog/product thumbnails from Media. User delivery retains live branch menu authorization; candidate preview also requires Media read. Guest Order validates the live visit and menu membership around delivery. BFF derives cookie scope and forwards only the guest capability for phone reads. Private storage URLs and originals are not public menu contracts.

Client images are optional presentation data with short in-memory visibility. Financial menu DTOs, pricing fingerprints and recovery remain unchanged. Image failure leaves text menus usable; deletion/revocation is rechecked on future reads and bounded by client refresh. Separate photo budgets protect ordering from normal photo fanout.

## Alternatives and consequences

Putting a product UUID field in Media upload leaves managers without a usable assignment workflow. Mutating Media ownership to reassign a file would destroy provenance and couple menu changes to storage. Adding public object URLs would weaken revocation and expose private storage. Embedding bytes in menu/quote contracts would increase financial payloads and couple pricing to image availability. The chosen explicit Catalog reference adds migration6 and a private cross-service read dependency, with fallbacks rather than distributed write transactions. An asset may become unavailable after approval; Catalog retains the auditable reference and clients show a placeholder until replaced/cleared. Populated downgrade is refused; forward recovery preserves history.

See [API](../../API/Menu-Photos.md), [schema](../../Database/Menu-Photos.md) and [operations](../../Deployment/Menu-Photos.md).
