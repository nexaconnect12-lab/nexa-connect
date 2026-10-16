# First-draft slice 3 evidence

Implemented and locally verified against revision
`b0673512be85ed76bbaa01f772d87efcfc59582c` plus the uncommitted slice 3 working
tree on Windows, 14 October 2026. No clean-checkout CI or production acceptance claim.

The [joined runner](../../Deployment/First-Draft-Checkout.md) uses actual owning commands
and normal clocks. Added cancellation authority for new assignments and Kitchen-first
stock compensation across Order placement/recovery/reconciliation/manager review.
No new schema, provider integration, refund or completed-food override is introduced.

Unit verification after these changes: **818 passed, zero skipped**, including cancellation
role defaults, confirmed Kitchen-before-stock ordering, blocked preparation preserving
unpaid state/stock, direct placement conflict mapping, independent outbox publisher connection
ownership and updated reconciliation redelivery expectations. Architecture tests: **6 passed,
zero skipped**. Draft lifecycle safety checks: **32 passed**.

The final fresh joined run is
`.runstate/first-draft/778e8204f6184091b59396fbc492adbc/checkout-verification.json`.
All six recorded checks passed: baseline checkout, interrupted placement, service recovery,
broker-paused checkout, broker-resumed projection and owning-database effects. The runner
verified cleanup of its owned project. No verification environment remains running.

Coverage includes real PKCE cashier shift/drawer commands, stale-price reconfirmation,
lost-response/concurrent replay, Kitchen station transitions and version conflicts,
manual cash/fictional PromptPay, stock shortage, active/completed-food cancellation,
Disabled-adapter decline and process/broker recovery. Final owning effects were three
manual settlements/receipts, ten Kitchen tickets, two Inventory release audits, three POS
settlements, three Reporting sales and one failed Payment intent. Interrupted placement
retained one reservation. Tokens and passwords are excluded from the bounded receipt.

Earlier failed runs were reset and retain failed receipts. They exposed inherited host
environment selection, a publisher borrowing the consumer connection, Docker reassigning
ephemeral broker ports and a missing verification-helper import. Corrections explicitly
set both Development host environments, give the publisher its own reconnectable connection,
retain fifteen distinct loopback ports with explicit Compose bindings and import the helper.
The successful final run includes an intentionally inherited Production environment and
broker recovery on the same address without restarting publishers/consumers to mask it.

The required `documentation_maintainer` audit was successfully retried after the earlier
slice 2 usage-limit failure. Its final follow-up passed after corrections to role lists,
selector guidance, recovery boundaries, retained ports and host environment documentation.
The primary resolved the findings and reviewed the final implementation and evidence.
`git diff --check` passed.

Documentation updated: root README; project overview; Project-Architecture and
Restaurant-POS-Architecture; ADR-011 follow-up; slice 2/3 evidence; November first-draft
plan; API Authorization-Assignments, Kitchen-Queue, Manual-Tender-Settlement,
Order-Cancellation and Order-Pricing; Identity Client-Matrix; deployment First-Draft-Test-Stack
and First-Draft-Checkout; docker/first-draft README; shared Infrastructure and Authorization,
Inventory, Kitchen and Order service READMEs; first-draft browser verification README.
Both canonical architecture summaries changed to describe the finished behavior.
Database guidance was reviewed; no schema or persistence ownership change was required.

Slices 4–7 remain. Windows/PDF printing and actual bank transfers were not verified.
Terminal admission currently lacks an Order-to-POS preflight and needs hardening before
release; this run used a valid provisioned terminal/drawer. The existing failed/rejected
provider-status persistence alias and historical unsafe releases are not repaired by this
slice. See the checkout guide for these boundaries and completed-food recovery limitations.
