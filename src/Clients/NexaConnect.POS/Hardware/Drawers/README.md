# Drawer preparation (UX8)

This component is **preparatory**, not an enabled drawer integration. The staff terminal
panel reports Disabled. No production composition constructs the workflow/journal or calls
a drawer after payment. There is no opening button, enable switch, guessed ESC/POS sequence,
Windows RAW spooler adapter or automatic retry.

`DrawerRequest` validates full terminal scope, original cash-sale versus manual shape and
fixed manual reasons. It is not proof of payment, authorization or operator identity.
`DrawerWorkflow` defines orchestration ports for a vendor-verified driver, live owning-service
authorization and durable intent evidence. Authorization must fail closed. The future adapter
must enforce original confirmed cash settlement/custody for sales and a separate live manual
opening permission plus reason; a local flag or request field cannot establish either.

The workflow checks session/cancellation before and after authorization and after reservation.
It reserves before the single driver call, never retries and never changes payment. A sale
uses its original Order in a full-scope key rather than a retry operation ID. Conflicting
actor/shift/session/reason fails closed. Driver failure is Unknown; driver success means
Submitted, not physically opened. Audit failure after a possible submission has a distinct
outcome. Reserved intents survive crashes and remain consumed even without completion.

`SqliteDrawerIntentStore` is an isolated Infrastructure adapter exercised by tests. The caller
must supply a path; no production file/location or migration is activated. Parameterized SQL
and a unique key/transaction fence concurrent reservations. Stored facts are hashes, fixed
cause/reason/outcome and UTC times. This is local attempt evidence, not an immutable server
audit or financial ledger. Do not delete it to retry a device action. There is no replay,
clear or repair UI. Replacing/deleting storage or moving between terminals is not a supported
deduplication guarantee; backend custody/intent authority remains required before activation.

See [UX8 status and completion gates](../../../../../docs/Deployment/POS-UX8-Cash-Drawer.md)
and [local intent format](../../../../../docs/Database/POS-Drawer-Intents.md).
