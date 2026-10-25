# Windows POS order list

QR8 adds active-window current-page refresh and a specific collection-blocker footer. Rows clear on deactivation/tab exit as well as lock/sign-out; refresh does not claim/pay or count as cashier activity. See [QR8](QR8-Usability.md).

Windows POS includes **Orders** for all persisted Orders in its configured organization,
restaurant and branch. It removes the need to copy an accepted QR Order UUID from a phone.
This page uses the staff [branch order-history API](../API/Branch-Order-List.md); guest
requests remain in the manager's QR request queue until staff accepts them.

## Cashier steps

1. Sign in with a branch-authorized account. Open **Orders**; it loads automatically.
2. Use **All sources**, **QR orders**, or **POS orders**, and **All statuses**, **Open
   orders**, or **Paid orders**. Filters reload the first page. **Refresh orders** starts
   from the newest rows; **Next page** reads the next 25. These are pages, not a silently
   truncated complete history or a combined table bill.
3. Review the original order number, source, table, lifecycle/collection status, total,
   currency and creation time. **Created (local)** uses the Windows PC's timezone; it is
   not a Restaurant business-day calculation or settlement report.
4. To collect an awaiting-cashier QR order, open your cashier shift, keep the
   cart empty and resolve any existing checkout. Select the row and choose **Load selected
   QR order**. Cash settlement also needs the cashier's open THB cash session.
5. The existing read-only preview, persisted original operation, claim and Payment flow
   run. Review the original items/pricing and confirm only the actual received amount.
   See [QR collection and recovery](QR5-Collection.md).

The list is readable without an open shift when the account has `order.read`. Collection
also requires live `order.manual-payment.confirm`, enabled collection, the current open
shift, manual tender, matching currency and no competing cart/checkout. The button stays
disabled for paid, claimed, unready or otherwise ineligible rows. Non-QR history is
read-only here; it does not adopt another terminal's counter sale. Existing **QR
collection** reference entry remains available for original-reference and retained recovery.

Viewing, filtering or selecting rows creates no custody/payment identity. **Load selected
QR order** revalidates current state through preview/claim; a changed or concurrently
claimed row can fail even when the displayed hint was eligible. Refresh and reconcile
the original Order; do not create a replacement or transfer custody. After an uncertain
claim/payment, use **Verify original QR collection** or **Verify payment** with the same
cashier/terminal/shift and retained operation.

Lock and sign-out clear in-memory rows, selection and cursor and invalidate in-flight
responses. List data is not persisted in SQLite and never populates the customer display.
No protected checkout, receipt, outbox or token data is erased by list refresh or lock.

## Setup and failure behavior

Deploy matching Order and Windows POS builds; no BFF/gateway guest route or new environment
variable is needed. Durable history requires PostgreSQL and current Order migration22,
including its QR acceptance/collection read tables, even if collection is disabled.
`QrCollection:Enabled=false` keeps history readable but offers no new QR collection.
Existing live branch permissions and POS admission still apply; no roles are broadened.

An empty page can mean no matching persisted Orders or guest requests awaiting acceptance.
403 means missing branch read authority. 503 includes unavailable owning dependencies or
the in-memory provider. The UI clears the old page on failure and offers a bounded retry
message; it does not show raw diagnostics. A 401 locks the workspace for sign-in. The
client bounds each response to 256 KiB and validates page scope, identifiers, cursor,
statuses and collection hints before display.

For the retained local pilot, finish any current action and close POS before updating.
Preserve the same branch/store/terminal/realm and protected storage selection. Use the
existing [Windows package/install](First-Draft-Windows-POS.md) and
[same-scope QR source refresh](QR7-Pilot.md) procedures; a new source candidate needs a
new immutable activation record. Do not copy old acceptance passes onto the new candidate.
An unchanged exact-origin certificate can be retained after its protected hashes/identity
are verified; source refresh does not fix a phone's certificate trust warning.

## Verification

Run focused tests before the full source-bound gate:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --filter 'FullyQualifiedName~BranchOrderListTests|FullyQualifiedName~PosOrderListTests|FullyQualifiedName~QrCollectionTests'
dotnet test tests/Integration/NexaConnect.IntegrationTests --filter FullyQualifiedName~OrderListBoundaryTests
./scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure
./scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure
```

The owning fixture now requires six cases: stable tied-time pagination, exact tenant/
restaurant/branch isolation and parameterized filters join the existing QR lifecycle and
broker journey. The joined journey checks an original QR Order before claim, after claim
and after Paid, including no custody write from viewing. Unit/transport/session tests
cover read/payment separation, bounded responses, invalid scopes/cursors, stale-page
privacy and cart/shift/recovery guards. HTTP tests cover staff identity, tenant context,
live authority, no-store, correlation and safe failure responses.

The current QR6 minima are 1,043 unit, 27 QR/history HTTP, six architecture, six owning,
and the existing frontend/browser/security gates. Passing these is source/fixture evidence;
actual cashier sign-in, populated WPF interaction, phone scans, payment, display and PDF
acceptance must be observed separately. Operational logs use `nexaconnect-order` and POS
`order.list`; see [safe event queries](../API/Branch-Order-List.md).

## Prior order-list candidate evidence — 25 October 2026 (before QR8)

QR6 run `8a3905d6-f679-4924-a7a6-4548914fb03d` passed against revision
`d4e5ce88d77f053afab7d3bfd84fdcc633533bc4`, `dirty=true`, canonical non-Markdown
source hash `6b4adec3e21fc1c8ab92fce7b5528a2b923c8a8327bedb9b2c8f5031e8d8dabf`
(1,836 files). Its protected `verification.json` and `owning.json` record:

| Gate | Measured result |
| --- | --- |
| .NET unit | 1,036 passed; none skipped |
| Architecture / QR and history HTTP | 6 / 27 passed |
| PostgreSQL/RabbitMQ owning journeys | 6 passed; cleanup and broker recovery verified |
| Hosting / candidate guards | 47 / 20 passed |
| Frontend unit / QR browser emulation | 78 / 54 passed |
| Financial browser contracts | 8 + 8 + 22 + 7 passed |
| POS build, frontend type check/builds and both npm audits | Passed |

Installed POS release `3cab73bfec894625b5d72e3406faeede` uses the retained stack
`c30b628ab23141f389119daa565f894f` and passed executable preflight with exit 0.
Its `NexaConnect · Cashier` window responded. These bounded observations are recorded in
`order-list-release.json` and `order-list-ui.json` under the protected previous pilot
directory `.runstate/qr7/29b3db313be24d47a9a27722cc969d4e/`.

The terminal package's `sourceInputHash`
`23c914a4aaf1e0adc69e99e12be78a404a1368b639b57cc59d1ecff4c77ab4d0`
uses `Get-DraftCandidate` in `scripts/first-draft-release-helpers.ps1`: selected build
paths, raw file SHA-256 values and a line digest. QR6 uses `scripts/release/source.mjs`:
all inventoried non-Markdown paths, Git-normalized object hashes and a JSON digest.
These are different source identities; compare each with its own current algorithm,
revision and dirty state rather than comparing the two hash strings directly.

Fresh immutable pilot `7b257913f2d44748a23bd834fed7e1f7` binds this QR6 candidate and
the same retained stack/scope to `https://192.168.0.191:8443`, preserving the verified
exact-origin certificate, CA and key. It starts with all 24 device cases pending and
`releaseReady=false` / `productionVerified=false`. Operator-reported trust does not
resolve the previously reported Safari certificate warning. Actual cashier sign-in,
populated Orders filtering/selection, payment/recovery, customer display, PDF and phone
acceptance remain separate tests; a responsive window and fixture passes do not establish
those outcomes. Older pilot evidence remains attached to its original candidate.

The final bounded receipt
`.runstate/qr7/7b257913f2d44748a23bd834fed7e1f7/order-list-rollout-verification.json`
records trusted PC-side guest HTTPS and JavaScript responses of 200, a denied staff
edge route (404), and anonymous native history denial (401). It confirms the installed
package matches the current legacy source identity, the current canonical QR6 identity,
the responding cashier window, preserved scope/certificate and unchanged hashes for all
three original recovery files. This checks local startup, transport boundaries and
preservation; the actual-device and cashier outcomes above remain pending.
