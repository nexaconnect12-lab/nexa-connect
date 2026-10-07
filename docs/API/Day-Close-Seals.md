# Source day seals and late-change tracking

The online, single-branch THB workflow retains three immutable source seals against a reviewed cutoff and separately reports later source revisions. It does not approve/finalize settlement. [ADR-024](../Architecture/Decisions/ADR-024-source-day-seals-and-late-change-journals.md) defines atomicity, affected dates and recovery.

## Customer coordination

GET `/bff/customer/day-close-seals?branchId={uuid}&businessDate=yyyy-MM-dd` loads saved state; GET `/bff/customer/day-close-seals/csrf` obtains the antiforgery token. POST accepts `{branchId,businessDate,operationId,expectedVersion,reasonCode,reviewedCutoffVersion}` with `X-Nexa-CSRF`, maximum 4096 bytes. Reason is `routine_close` or `recheck`; reviewed cutoff version must be positive. Tenant, actor and bearer derive from the protected session and current membership. Unknown actor/organization fields confer no authority.

Owning POS GET/POST is `/api/pos/v1/customer/organizations/{organizationId}/day-close-seals`. Existing `pos.day-close.read` and `pos.day-close.prepare` separate retained reads from manager coordination. Source financial-read permissions and Reporting sales permission remain necessary for live validation. No financial approval authority is introduced.

The response retains preparation's `identity`, `version`, `status`, `snapshot`, `blockers`, `validatedAtUtc`, `canPrepare`, `pendingCommand`, and adds nullable `pendingSealChanges`. API statuses remain `not_prepared`, `preparing`, `blocked`, `ready_for_review`; the portal labels a valid Ready seal as **sealed evidence**. `snapshot.seals` contains `order`, `payment`, `pos` references `{sealId,manifestId,revisionEpoch,sourceRevision}`, plus `pendingChanges`, `journalComplete`, `deliveryComplete`. References must match the reviewed protocol-two cutoff. No private actor/Authorization/claim/audit details are exposed.

Begin checks the exact Ready cutoff version and pins its snapshot locally. A 30-second lease and expected coordinator version fence contention. Original-actor resume retains the command, including reviewed version and manifest references. Another manager may replace expired work with a new operation/current coordinator version and currently reviewed cutoff. Source operation replay preserves original seals; a changed actor/window/expected revision conflicts. Partial source commits remain durable even if the coordinator finishes Blocked or is interrupted.

Existing preparation/cutoff routes preserve their original five-field `pendingCommand` (`branchId`, `businessDate`, `operationId`, `expectedVersion`, `reasonCode`). Their stored operation fingerprint explicitly serializes those original fields, preserving byte-compatible replay of pre-seal operations. Only the separate seal route requires and returns `reviewedCutoffVersion`; adding it does not rebind existing preparation/cutoff operation identities.

Ready requires all three valid seals, intact journal coverage, no pending changes, complete Reporting selection and ordinary operational/evidence readiness. `sealed_changes_pending`, `seal_journal_unavailable` and `seal_delivery_unproven` block. Fresh Ready reads/replays can atomically invalidate coordinator readiness; the old snapshot stays unchanged. `pendingSealChanges` is the separately saved count from that last check. Blocked reads do not poll sources or imply a fresh count. Refresh ordinary cutoff evidence and explicitly reseal with a new operation after resolving changes. No automatic retries or acknowledgment that hides pending changes is supplied.

## Owning source routes

For Order, Payment and POS, POST `/api/{owner}/v1/customer/day-cutoffs/seals` accepts `{operationId,window,manifestId,expectedRevision:{epoch,revision}}`. Window uses the existing five scoped UTC fields. GET `/api/{owner}/v1/customer/day-cutoffs/seals/{sealId}` requires the same exact window as query parameters. Existing live source financial-read authorization applies: these endpoints retain evidence and do not authorize POS coordination or approve financial amounts.

Response is `{seal,manifest,pendingChanges,journalComplete,changes,changesTruncated}`. Seal contains `{sealId,operationId,window,manifestId,sourceRevision,sealedAtUtc}`. Manifest is the immutable original protocol-two source selection. Change entries contain `{revision,recordedAtUtc}`, at most 256 in ascending revision order. Count and integrity cover the full suffix after the seal's revision; truncating the returned page does not truncate the journal. Epoch mismatch, counter rewind or missing journal coverage cannot establish readiness.

Retention locks source revision advancement inside the owning database and checks the expected revision in its snapshot; earlier competing commits cause 409. Later commits remain legal and append the change journal atomically. All sealed dates in the branch are conservatively affected; journal recording time is not a financial effective date. Other branches remain independent. Source replays return the immutable original seal and a fresh journal observation.

## Reporting

POST `/api/reporting/v1/customer/day-seal-reconciliation` accepts `{window,orderSealId,paymentSealId}`. Response contains check/window/seal/manifest identities, `deliveryComplete`, check time and separate sale/payment/refund `CutoffFactCounts`. Live Reporting permission precedes source calls. Existing source read authority is revalidated through owning APIs. Reporting verifies original values/identities/hash receipts, never reads source databases, writes source state, repairs events or publishes settlement.

Delivery proof covers the original sealed selection. Later journal entries independently block POS readiness; unexpected facts in the same Reporting window can also produce gaps. The protocol remains sequential observations; it does not certify a global financial cut.

All success/error/authentication responses are no-store. Invalid input is 400; live authentication/access failures 401/403; unknown scoped reads 404; source expectation/operation and POS version/lease/review conflicts 409; sanitized dependency/storage errors 503. Source and coordinator Application deadlines are 25 seconds, existing HTTP clients 15 seconds, BFF 30 seconds and browser 40 seconds; dependency bodies remain bounded to 16 MiB. Cancellation can leave recoverable Preparing state.

Query `nexaconnect-order`, `nexaconnect-payment`, `nexaconnect-pos`, `nexaconnect-reporting`, `nexaconnect-customer-bff` for `Day-cutoff` or `Day-close`/`Day-seal`, narrowed by validated `CorrelationId`. Structured logs contain safe status/category only, never financial/journal bodies, subjects or credentials.
