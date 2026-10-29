# Drawer preparation (UX8)

UX8C supplies DrawerHistoryReader, narrow GET-only IDrawerHistoryPort/ILocalDrawerHistory, protected future reservation context and staff review. POS25/server read permission is required; driver configuration is not. Read-only SQLite never creates/backfills files, and authority must succeed before local loading. Local/server evidence is never reconciled by writes. See [history](../../../../../docs/Deployment/POS-UX8C-Drawer-History.md).

UX8A supplies [server reserve/read/outcome authority](../../../../../docs/API/Drawer-Intents.md), default-off under POS24 / Authorization16. UX8B software preparation now composes its native adapter/coordinator, lazy journal and guarded post-cash-payment hook with DisabledDrawerDriver. Actual verified hardware and physical activation remain pending.

The staff terminal panel reports Disabled, with predefined manual reasons and a disabled
opening action. Production constructs the coordinator/HTTP adapter and a lazy store object;
the disabled driver returns before journal I/O or HTTP. No enable switch, guessed ESC/POS
sequence, RAW spooler adapter or automatic retry is provided.

`DrawerRequest` validates full terminal scope, original cash-sale versus manual shape and
fixed manual reasons. It is not proof of payment, authorization or operator identity.
`HttpDrawerAuthority` implements fixed reserve/read/report contracts with original current
token subject/issuer/scope checks, no cookies/redirects, bounded JSON/complete-operation deadline
and safe correlated client diagnostics. `DrawerPermit` distinguishes a first expiring grant
from denied, conflicting, unavailable or replayed authority. Server live custody/permission
remains authoritative; no local request or preference can establish either.

The workflow checks session/cancellation before local reservation, after reservation and after
server authority, including driver availability/expiry before the call. Local consumption
precedes HTTP, so response loss cannot be replayed. It never changes payment or retries. A sale
uses its original Order in a full-scope key rather than a retry operation ID. Conflicting
actor/shift/session/reason fails closed. Driver failure is Unknown; driver success means
Submitted, not physically opened. Audit failure after a possible submission has a distinct
outcome. Reserved intents survive crashes and remain consumed even without completion.

`SqliteDrawerIntentStore` is an isolated Infrastructure adapter exercised by tests. The caller
must supply a path; the factory selects drawer-intents.db within existing scoped POS storage,
but default startup/opening workflows create or open no file while DisabledDrawerDriver is selected.
UX8C history can inspect an existing journal read-only after server authorization. Parameterized SQL
and a unique key/transaction fence concurrent reservations. Stored facts are hashes, fixed
cause/reason/outcome and UTC times in the original table; future reservations add DPAPI-protected
lookup context as described above. This is local attempt evidence, not an immutable server
audit or financial ledger. Do not delete it to retry a device action. There is no replay,
clear or repair UI. Replacing/deleting storage or moving between terminals is not a supported
deduplication guarantee; backend custody/intent authority remains required before activation.

See [UX8 status and completion gates](../../../../../docs/Deployment/POS-UX8-Cash-Drawer.md)
and [local intent format](../../../../../docs/Database/POS-Drawer-Intents.md).

See [UX8B software flow and remaining equipment gate](../../../../../docs/Deployment/POS-UX8B-Native-Drawer.md).
