# Order QR4 acceptance storage

Apply Order migration21 `0021_qr_acceptance`, minimum catalog compatibility0.38.0, before
deploying current QR repository readers even when acceptance is disabled. Financial
schema/permission versions remain unchanged; this optional capability adds one table to
the prior207=208 catalog at QR4 introduction. QR5 adds one for the current209;
current manual-settlement binaries also require [Order22](QR-Collection.md).
See [contract](../API/QR-Acceptance.md).

`order_qr_acceptances` is immutable request/Order binding, one row per original request
UUID=Order UUID. It retains organization/restaurant/branch, logical label/entry/visitor,
actor, actual Authorization decision, request version, accepted commercial fingerprint,
correlation and database acceptance timestamp. FKs stay within Order's database. No FK or
implicit mapping to Restaurant dining_tables; channel=qr/service_type=dine_in plus this
explicit provenance preserve the logical-label meaning. Existing POS history is untouched.

Migration21 adds review_snapshot/consented_snapshot to order_qr_requests, each bounded
131072bytes. Original submitted quote remains immutable in order_qr_quotes for exact
submission replay. Current consent is separate; pending/review/accepted transitions audit
their versions. Active pending uniqueness/counts now include price_review. Deadline and
rejection-category constraints remain; new statuses/actions are allow-listed.

Acceptance uses the existing Order aggregate/pricing snapshot and shared Order persistence
in the same transaction as request mutation, provenance and audit/outbox. Recovery can
only see the Submitted Order after that transaction commits. Same scoped advisory lock
as QR3 orders competitors; financial source triggers retain their own guard semantics.
No SQL/network/broker transaction spans services. Existing recovery claim/lease and
Inventory/Kitchen idempotency remain authoritative after handoff.

Runtime needs SELECT/INSERT only on acceptance provenance, existing request UPDATE rights
on new columns and existing Order/line/outbox workflow privileges. No provenance or audit
UPDATE/DELETE/TRUNCATE; migration owner alone controls schema. Verify actual least-privilege
roles before deployment; disposable tests use generated administrator credentials.

Accepted requests and referenced quotes are never pruned by QR3's24hour terminal cleanup;
rejected/expired proposals retain that policy. Durable acceptance metadata must survive
restarts/restores with original Orders and outbox. No archival tool is added. Downgrade
refuses any acceptance, review/consent state or corresponding audit history; forward
recovery after use. Empty downgrade/reapply remains available while features are disabled.
Restaurant adds a scoped Infrastructure read, no migration/secret recovery. Public guest
views omit actor/decision/internal owner IDs and raw session capabilities.
