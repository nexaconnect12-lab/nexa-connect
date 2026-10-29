# ADR-054: Branch-approved static PNG for manually verified PromptPay

Status: Accepted

## Context

Cashiers need a centrally approved merchant QR without editing local deployment files.
Screensaver/photo delivery can crop/rescale images and has separate locked-reader authority.
Neither image readiness nor displaying a QR proves payment receipt.

## Decision

Restaurant owns one versioned branch selection in existing JSON configuration and commits
audit/outbox with its CAS. Media owns bounded original PNG storage, safety and lossless
delivery. Configuration managers use live branch authority throughout upload, preview,
completion and approval; cashiers use live branch menu-read. No broad Media grant, gallery
capability or secret is added. The BFF derives protected workspace/subject and requires CSRF.
Existing Media workload credentials validate the owning active THB branch through Restaurant.

Use purpose-separated static PNG, at most 2 MiB and 64–2048 pixels each side, rather than
SVG or lossy/resized photo variants. Approval checks ready exact ownership and decodability;
human managers independently verify bank/payee/currency. Delivery rereads selection after
storage and issues a maximum 60-second in-memory lease. Native POS opts in explicitly,
refreshes every 30 seconds in the payment context and before a new confirmation, and fails
closed when image/authority is unavailable. Legacy local-file profiles remain opt-out.

Fresh receipt proof belongs to the current cashier/order/tender and resets on context or
image changes. Original uncertain settlement reuses its entire persisted request and key
without requiring an image or another transfer. Existing backend money/shift/idempotency
authority is unchanged; the QR feature never confirms receipt automatically.

## Consequences

No new schema or grants; shared branch versions can invalidate quotes/configuration edits.
New PromptPay confirmation requires online Media/Restaurant availability; original financial
recovery remains possible. Unselected uploads consume existing Media quota until removed
through its lifecycle. Real merchant image, bank receipt and physical terminal UAT remain
separate from synthetic tests. Future hosted rollout must supply matching service routes,
Media origin/CSP and client flag; local activation is explicit and source-bound.

See [API](../../API/Branch-PromptPay-QR.md) and
[operator/activation guide](../../Deployment/POS-Branch-PromptPay-QR.md).
