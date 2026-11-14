# POS manual refund storage

POS 30 (`manual_refund_transfer_claims`, compatibility 0.50.0) adds two POS-owned
tables. `manual_refund_reference_keys` holds a random organization key encrypted
with the POS Data Protection key ring. `manual_refund_reference_claims` uniquely
binds the organization's HMAC-SHA-256 digest to a completed operation. POS inserts
the claim, completion, financial journal entry and outbox event in one transaction.
The database rejects a new PromptPay completion without a claim. The runtime needs
SELECT and INSERT on both tables.

On first use, POS claims historical references using retained encrypted proof. When
historical completions share a reference, the earliest completion supplies the claim.
All original receipts remain readable. Preserve the POS key ring across deployments
and recovery. Downgrade requires empty claim and key tables.

POS migration 27 (`manual_refunds`) requires application compatibility 0.46.0.
It adds four POS-owned tables:

- `manual_refund_sales` stores the trusted original Order settlement identity,
  tenant scope, tender and paid amount. The Order-owned sale and receipt stay intact.
- `manual_refund_approvals` stores the unique operation, reserved amount, reason,
  manager and decision identifiers, and designated cashier.
- `manual_refund_payouts` stores the first terminal, shift, and cash session when
  applicable. A started operation keeps its reservation until reconciliation.
- `manual_refund_completions` stores either a unique linked POS cash movement or
  PromptPay proof protected by the POS Data Protection key ring. Back up that key
  ring with the database.

The store locks the original sale row before restoring and applying domain rules.
Operations on the same settlement serialize. Matching retries preserve the original
approval, custody and completion. New payouts lock the exact active cashier shift and,
for cash, the paying drawer. Shift/drawer close triggers return SQLSTATE `PMR01` while
an original payout is unresolved. The POS HTTP middleware maps that conflict to
HTTP 409 with `manual_refund_payout_pending`. Cash completion inserts the linked
`refund` movement, advances the drawer version and records completion in one transaction.
PromptPay completion records protected evidence without a cash movement. The transaction
store restores up to 1,000 operations per original order.

The refund tables reject updates, deletes, and truncation. The down script permits an
empty rollback and rejects removal after a sale enters this ledger. Use the normal
versioned migration procedure and existing POS role provisioning. The owning service
needs SELECT and INSERT on refund tables plus row-lock privileges on the sale table.
The store uses the existing shift, terminal, store, drawer and movement privileges.

POS 28 (`manual_refund_management`, compatibility 0.47.0) adds the immutable
`manual_refund_releases` table and the short-lived `manual_refund_cashier_readiness`
table. A release restores available balance without deleting its approval or writing
to the drawer. Release and payout start take the same original-sale lock. Rollback
from POS 28 requires an empty release table and removes readiness registrations.
The POS runtime needs SELECT and INSERT on both tables and UPDATE for readiness renewal.
Readiness retains one row per registered shift. After five minutes the row is
ineligible until renewed. This slice adds no cleanup worker.

The gated customer POS API uses POS 28 with Authorization 18 grants.
Recovery reads the exact scoped operation without decrypting PromptPay proof.
Completion atomically adds a POS outbox row for the Reporting 23 projection.
Permanent day-close evidence remains required before activation.
[API and rollout](../API/POS-Manual-Refunds.md)

Windows POS stores a `pending-manual-refund` key in its existing protected
`local_state` table. The payload holds the order ID, operation ID and original
cashier subject. It uses the existing terminal scope and current-user protection.
The setup preflight treats this key as retained work. No local schema migration
is required. Transfer proof is excluded from the local payload.
The manager uses `pending-manual-refund-approval` in the same table to retain the
original request and identity for safe verification or exact retry.
