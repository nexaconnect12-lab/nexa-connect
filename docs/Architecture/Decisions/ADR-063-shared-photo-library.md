# ADR-063: Purpose-scoped shared photo library and explicit selection

- Status: Accepted
- Date: 2026-11-03

## Context

Managers need one iOS Photos-inspired browsing surface and contextual selection from menu/gallery editors. Enumerating every organization asset or embedding signed object URLs in thumbnails would mix bank QR/private purposes, weaken lifecycle revocation and couple each editor to storage details. Upload and approval already have distinct owners and concurrency contracts.

## Decision

The 6 November 2026 follow-up puts batch uploads in Media management. The existing upload
pipeline accepts an organization-owned `media` / `photo_library` purpose with the owner
equal to the authorized organization. Browsing and photo delivery accept that purpose
alongside legacy `catalog` / `product` photos. Menu and gallery editors select ready photos
and keep their separate approval and save operations.

Reuse Media's PostgreSQL metadata and safe processed-photo pipeline. Add a bounded, live-authorized `catalog`/`product` library query and same-origin private PNG thumbnails behind Customer BFF. The protected workspace/current Directory membership and Media's repeated live read grant are both required. Dedicated correlated, redirect-disabled adapters suppress URI logging so search terms do not enter telemetry.

Reuse a React library (thumbnail grid by default, details optional, server search/pagination) inside a temporary popup picker. Confirming returns a candidate to the editor; it does not upload, approve, publish or save. Catalog keeps menu approval and Restaurant keeps staff/customer gallery approval, with their existing preview, CSRF, CAS and audit/outbox boundaries. PromptPay QR retains its separate purpose-specific workflow.

## Consequences and alternatives

No replacement store, framework, schema or generic media type is introduced. A private thumbnail request costs live authorization/storage work, trading cache efficiency for existing revocation/safety rules. Live offset pages can shift during concurrent changes; they are not immutable snapshots. Persisted view preferences, metadata editing, other formats and native/other-portal consumers are future slices. The existing signed original download remains an explicit authorized action, not grid delivery. An independent per-editor asset dropdown and a public/presigned grid were rejected because they duplicate selection UX and expose broader storage capabilities.

See [API](../../API/Media-Library.md), [persistence](../../Database/Media-Library.md) and [workflow](../../Deployment/Media-Library.md).
