# ADR-031: Verified POS late-cash reconciliation corrections

Status: Accepted and implemented; local execution is recorded in [acceptance evidence](../Evidence/Late-Cash-Correction-Acceptance.md).

## Context

ADR-030 records source-owned review without applying late deliveries. A missing cash projection can leave an approved closed drawer's variance overstated. Releasing the old message would mutate permanent ADR-029 evidence. POS needs a separate correction whose amount and provenance can be verified and whose posting cannot duplicate a delivery linked to several settlements.

## Decision

This first correction capability supports retained POS `order.manual-tender-settled.v1` **cash** deliveries only. The owning POS case must be linked to a committed settlement and its latest review must be `correction_required` at the explicitly reviewed version. POS resolves exactly one original closed drawer, requires its close inside the original settlement window and rejects any existing projection of the event, tender or Order.

Order provides a customer-authorized narrow original-tender evidence endpoint. Its own Infrastructure compares the retained original outbox message against its immutable manual-tender row and ordinary paid receipt, including exact scope, terminal, method, currency, amount and paid time. PostgreSQL timestamp comparisons account for microsecond storage precision while the original event/receipt JSON remains unchanged. Missing/ambiguous/inconsistent history blocks posting. POS translates this contract into its own Domain proof and requires equality with retained normalized custody, including its canonical fingerprint. No browser amount, arbitrary adjustment or provider reference is accepted.

The correction is a **negative cash variance adjustment equal to the missing cash tender**, not another sale, receipt payment, physical movement or change to the old drawer. The manager loads a read-only preview and explicitly confirms its amount and posting date. Posting uses the current branch-local calendar day, with stored timezone and half-open UTC boundaries; ambiguous/invalid midnight histories are unsupported. It must follow the original financial window. Preview fingerprint binds case/review, original proof, amount, drawer, scope and posting day. New posting rechecks original proof, calendar, live hierarchy/read authority and amount-aware `pos.day-close.late-cash.post`; changing day, proof or review requires a new preview. Authorization 15 defaults posting to tenant-admin/store-manager, with accountants read-only. An applicable configured action/currency limit is mandatory; a missing limit denies posting. Limits apply to the positive tender magnitude, including exact retries.

POS owns immutable `late_cash_corrections` and `late_cash_correction_audit`. One local transaction serializes operation → branch financial revision → review case, checks the current version, and commits ledger/receipt, audit, financial revision/journal and original `pos.late-cash-correction-posted.v1` outbox event together. Database insert guards use the same lock order and backstop committed membership, current review, actual drawer/scope, original amount and current posting interval. The existing permanent/temporary guards evaluate the new posting-time descriptor. Independent uniqueness on organization/work, original event, tender and Order prevents duplicate correction across settlement links or operations.

Exact replay binds the original actor, complete command and settlement scope and rechecks live authority and amount limits. It returns the original immutable receipt without requiring original Order/calendar availability again. A response loss or restart cannot append another correction. Changed actor/body/scope conflicts. Source outage before a new posting cannot invent evidence. Review history and original custody disposition remain unchanged; subsequent review decisions do not reverse a posted ledger entry. Reading a later linked review returns the already-posted receipt with its original settlement reference.

POS day evidence adds the adjustment to cash variance **only in the UTC posting window**, exposes its amount/count separately and includes immutable correction identities in retained evidence hashes. Gross sales and tenders do not change. Older snapshots/receipts and old drawer journals remain unchanged; owning revisions advance, with Domain/database attribution excluding unrelated historical windows. Reports continue to use the established current branch-calendar policy; stored correction posting metadata remains fixed. Decimal preview/receipt amounts serialize as exact strings so explicit review does not round them in JavaScript.

## Consequences

Targets are Order 19 / Payment 18 / POS 20 / Authorization 15, application compatibility 0.34.0, Reporting 20 unchanged. The Order route needs matching binaries but no new Order table. POS adds two tables; source downgrade refuses retained correction history. BFF remains cookie/tenant/membership/antiforgery protected, forwards customer bearer with validated correlation and returns bounded no-store responses. The existing POS workload identity reads Restaurant calendar only; it cannot admit a customer posting. No transaction spans HTTP.

Reporting projection/export, fiscal documents, Payment/provider corrections, reversal/compensation, arbitrary/backdated adjustments, physical drawer transfers and offline posting remain separate work. The new event is durable and can use the existing outbox dispatcher, but this slice adds no Reporting consumer.

See [API](../../API/Late-Cash-Corrections.md), [deployment](../../Deployment/Late-Cash-Corrections.md) and [verification](../Evidence/Late-Cash-Correction-Acceptance.md).
