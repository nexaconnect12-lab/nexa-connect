# ADR-026: Exact historical-day attribution

Status: Accepted. Date: 2026-10-08. Supersedes ADR-025's conservative historical affected-date policy.

## Decision

Order, Payment and POS capture version-two immutable source-change descriptors in the financial mutation transaction. Descriptors contain changed-record and financial-parent identities, before/after status and financial versions, effective timestamps, receipt-presence signals, and explicit ownership uncertainty. POS additionally captures variance presence and the review status/reviewed financial version. No monetary amounts, receipts, provider identifiers, personal data or request bodies are retained in these descriptors. Raw source values are translated in Infrastructure; each owning Domain supplies its financial selection policy. Shared persistence performs bounded stream/transaction mechanics only.

Both before and after states participate. A selected historical change remains pending even after values are restored or the record moves out of the window. Unknown kinds/states, incomplete required historical evidence, changed ownership/identity, and legacy timestamp-only or unattributed rows fail closed. Existing history is not reconstructed from today's source rows.

## Owning selection rules

Order completed records select both the half-open creation-time sales window and the receipt Paid-time tender window. Older nonterminal Orders carry forward to every window ending after creation; cancelled records do not participate. Paid history remains immutable. A completed record without original receipt/Paid-time evidence cannot establish exact exclusion. Tender/publication children inherit the original Order selection and identify their parent separately.

Payment intents participate only while unresolved, using the existing terminal status set. Unresolved refunds carry forward from their requested time; completed refunds and publication children select the completion-time financial window, including the independently selected original event inventory. Completed refund/receipt history remains immutable. This change introduces no financial correction endpoint.

POS open/closing shifts and open/counting drawers carry forward from opening. Closed drawers select their closure window; earlier nonzero variance carries forward only when review is absent/unapproved or does not match the financial version. Movement and review children retain both parent states, including INSERT/DELETE. Movement before-state variance is reconstructed from the current sum by reversing only the captured row mutation inside the same transaction; only its nonzero signal is retained. A valid approved older drawer does not invalidate unrelated later windows. Store metadata UPDATE with stable ownership is excluded because it does not participate in financial selection. Store creation/deletion and reassignment remain uncertain.

Parent scope reads/capture lock owning parent rows to serialize child attribution with reassignment and financial mutation. Protected branch revision advancement and journal insertion still commit or roll back with the financial mutation. No lock or database transaction spans HTTP/provider calls. Runtime roles retain owning table/parent mutation privileges without schema ownership or trigger-disabling privileges.

## Contracts and manager detail

Source seal responses require attribution protocol two. Full suffix integrity is checked even for excluded rows. The existing 10,000-row/16-MiB evaluation bound, unknown unevaluated counts and 256-entry source display bound remain. Safe entries add record kind/ID, financial-parent ID, before/after status/version and the effective time responsible for the reason. Domain reasons distinguish sales date, tender date, refund date, cash closure, unresolved work, cash review and uncertainty.

POS translates source details into its own Domain view and retains at most 256 entries across sources, with explicit truncation. The manager view preserves the sealed baseline, shows current advisory totals, and lists records/reasons observed at the last check. Unknown/pending changes block readiness even when amounts are unchanged. Blocked reads expose saved detail; explicit cutoff refresh/reseal is required. Existing customer authorization, tenant boundaries, CSRF, no-store behavior, correlation-enabled clients and safe JSON/OTLP events remain. Bodies, descriptor/detail values, monetary data and tokens are never logged.

## Rollout and limits

Deploy Order 16 / Payment 15 / POS 13 with compatibility 0.29.0 before upgrading sources/POS/BFF/portal. Reporting 20 and Authorization 10 remain unchanged. New POS rejects older attribution response protocols. New migration downgrade refuses version-two descriptor history; empty/legacy-only downgrade restores prior capture functions without rewriting epochs or history. Forward recovery is required after exact attribution history.

These remain separate source-local observations, not a global cut or settlement approval. Sequential final checks can be followed by later writes. Historical Restaurant calendar ownership, production capacity/retention acceptance, fiscal adjustment workflows, manager approval and finalization remain follow-up work. Existing immutable paid/refund guards remain intact. See [executed verification](../Evidence/Exact-Day-Attribution.md).
