# Windows POS QR request review and acceptance

The subsequent [unified QR orders workspace](POS-QR-Orders-Workspace.md) supersedes this
release's separate QR requests/collection navigation in current source. The original review
API/security semantics and historical activation evidence below remain valid. The combined
workspace is now installed; its current bindings and fresh observations are tracked separately.

The initial review release added **QR requests** for the configured branch. Cashiers can review and decide
guest requests without opening the Customer Portal. Manager review remains available in
the portal. Both clients use the same Order-owned [request](../API/QR-Requests.md) and
[acceptance](../API/QR-Acceptance.md) workflow; this is not another order or payment engine.

## Scope and implementation

The implementation reuses `PosApiClient`, tenant/bearer/correlation handling, native shell
controls and session privacy, with a bounded transport adapter and memory-only review state.
WPF presentation orchestrates the existing versioned use cases; Order retains pricing,
authorization, transactions, audit/outbox, Inventory/Kitchen recovery and idempotency.
There is no schema, migration, API route, role/grant or storage-provider change.
No hardware, vendor SDK, provider key or additional dependency is required.

The full regression gate exposed a refresh/shutdown race in the existing identity adapter.
Identity sends now check cancellation before HTTP and normalize disposal during cancellation/
shutdown to cancellation. This preserves the existing no-save shutdown invariant; no session
timeout, identity authority, credentials or original-cashier policy changes.

Relevant components are `PosApiClient.QrRequests.cs`, `Presentation/PosQrReviewState.cs`,
`MainWindow.QrRequests.cs`, the main XAML and existing Orders/session lifecycle. Focused
client/state tests and native fixtures cover request boundaries, uncertain outcomes,
confirmation, price review, original-order navigation and privacy. Existing owning-service
fixtures remain responsible for concurrent acceptance and transactional delivery invariants.

## Historical cashier steps — initial review release

These navigation steps describe the superseded release. For the installed workspace, use
[QR orders and its three views](POS-QR-Orders-Workspace.md#cashier-workflow). The owning
request, authorization and guest-consent rules below still apply.

1. Sign in as an authorized cashier. Open **QR requests**. The branch comes from the
   terminal configuration; no editable organization, branch or table UUID is needed.
2. Choose Pending, Guest price review, Accepted, Rejected or Expired, then **Refresh
   requests**. Up to ten requests appear. Pending work is oldest first; refresh after
   decisions to review the next batch. Other statuses retain the service's existing bounded
   queue semantics; the Orders list remains the paginated accepted-order history.
3. Select a row. Review the table, request identity/deadline, names, quantities, unit prices,
   subtotal, service charge, tax and server total. Select a predefined reason before rejection.
4. Check **I reviewed this request and the selected decision**. Choose **Accept original
   request** or **Reject request**. Changing selection/reason or reloading clears confirmation.
   Decisions are explicit; polling never accepts, rejects, claims or collects payment.
5. A changed-price result is **not accepted**. The original guest must confirm the updated
   quote on their phone. Staff then refreshes Pending and accepts the new version. **Refresh
   guest price review** only renews the guest review through the existing owning workflow;
   it cannot bypass guest consent or extend the original request/session deadline.
6. After acceptance, **View original order** opens Orders with QR/All statuses filters and
   selects the original UUID if present on the newest page. Otherwise refresh/use Next page.
   Acceptance and navigation create no collection custody or payment. Wait for preparation
   readiness and use the existing **Load selected QR order** and cash/manual PromptPay flow.
   Collection still requires the cashier's shift, matching currency, empty cart, no competing
   checkout and live payment authority. Do not recreate a guest request as a counter sale.

## Authorization, failure recovery and privacy

The native client uses existing customer-role Order routes directly with its bearer and exact
configured tenant headers. Queue reads require live branch `order.read`; accept/reject require
live `order.place`. A visible button or checked review box grants no server authority. Existing
`QrRequests:Enabled` and `QrAcceptance:Enabled` govern the service; there is no new native flag.
The portal retains its existing membership/CSRF protections; native bearer calls do not reuse
browser cookies or bypass the owning authorization decision. No additional cashier grant is added.

The response limit is 2 MiB and the native deadline is twenty seconds; Order retains its
twelve-second request deadline. Incoming DTOs translate into bounded native view data with
validated IDs, versions, state, currency, quote arithmetic and original accepted-order identity.
No client-supplied prices or totals are sent. Decisions send only the original request UUID,
reviewed version and, for rejection, an allowlisted reason.

401 locks for sign-in; 403 reports denied authority; 404 reports unavailable capability or
request. Conflicts, malformed responses, timeouts and transport failures clear stale rows and
confirmation. **Refresh original requests before another decision.** There is no automatic
command retry. A committed acceptance with a lost response is recovered through Accepted and
the original Order, using the server's existing replay/version rules; never create a replacement.

Unselected queues refresh every fifteen seconds only while the window and tab are active.
Selecting a row pauses polling to preserve the reviewed snapshot; the server rechecks its version.
Lock/sign-out, tab exit, deactivation and shutdown cancel reads/decisions, clear the queue and
invalidate late responses. Request data is memory-only, excluded from protected checkout storage,
support exports, logs and customer-display projections. Existing original financial recovery is
unchanged. Cancelling a client request does not prove the server rolled back a committed decision.

## Observability and verification

Service `nexaconnect-pos-client` reuses shared structured JSON/optional OTLP. The existing
`NexaConnect.POS.Checkout` boundary logger records only operation/status/correlation for
`order.qr.review.list` and `order.qr.review.decision`. Correlate its validated `X-Correlation-ID`
with `nexaconnect-order` QR review/acceptance events. Never log bodies, tokens, guest capabilities,
table labels, quote/payment values or unrestricted actor data.

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --filter 'FullyQualifiedName~PosQrReviewTests|FullyQualifiedName~QrRequestTests|FullyQualifiedName~QrAcceptanceTests|FullyQualifiedName~PosOrderListTests|FullyQualifiedName~QrCollectionTests'
dotnet run --project tests/Windows/NexaConnect.PosTouchTests -- --qr-review-preview
./scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure
```

Native tests use synthetic identity/HTTP and temporary recovery only. They do not accept retained
requests, make payments or verify physical devices. Real portal/POS competing staff decisions,
phone price confirmation, Kitchen delivery and original cash/PromptPay collection require a
matching installed candidate and explicit operator observations.

On 2 November 2026, the corrected complete source-bound gate passes: 1,421 unit tests,
7 architecture tests, 136 HTTP integration tests, 90 frontend tests and 154 browser cases,
plus native printing/input/session/shutdown fixtures, build/type/dependency checks and
13 owning workflow plus 4 drawer-authority cases with verified cleanup. Focused verification
also passes 68 QR/client tests and 50 authentication/new-client tests. Documentation audit
is clear. The prepared 23-file Windows package and gate each match their respective current
source inventories; those inventories intentionally use different hash calculations. These
results certify local source verification, not installed operator or production acceptance.

## Rollout and acceptance provenance

Source implementation is separate from pilot activation. Use the existing guarded
[UX22 refresh](POS-UX22-Local-Pilot-Startup.md) and [UX23 host maintenance](POS-UX23-Background-Hosting.md)
after the full source-bound gate and POS closure. Preserve scopes, orders, shifts, settings,
media/payment configuration and protected recovery. No migration is required for this slice.
Do not stop an active sale to install it. Prepare a fresh QR candidate/host binding and acceptance
pack for the new installed target; the eight passes on the preceding UX24 target remain historical
and do not transfer automatically. [UX24](POS-UX24-Guided-Acceptance.md) remains incomplete.

### Retained local pilot activation — 2 November 2026

After the operator confirmed POS closure, the tested package was installed in the existing
Windows account and scope. Twelve services, the guest-only QR edge and the current-user
scheduled host are running; automatic Windows sign-in startup remains off. The host worker's
parent is `svchost`, independent of the maintenance command. POS was reopened through the
Windows desktop shell and remained responsive after the launch command exited.

| Binding | Activated value |
| --- | --- |
| Run | `c30b628ab23141f389119daa565f894f` |
| Release | `9a7d99df6e2d4cd5b0614c08e1e54743` |
| QR pilot | `21952e2cf146451197b0edec0e34a290` |
| Background host | `b39e88e903944bdca2aba2adbfa364e7` |
| Fresh acceptance pack | `a356089b155247a58941694e678ae176` |

Before reopening, comparison confirmed nine checked protected local files and 159 checked
business tables unchanged, along with reference data, credential values, POS configuration,
addresses and certificates. The owned databases were snapshotted while application services
were stopped. Initial backup preparation stopped because host shutdown had also stopped
PostgreSQL; only that owned container was resumed to complete the snapshot before rebuilding.
No reset or replacement provisioning occurred.

Real cashier PKCE menu and QR queue reads returned 200; anonymous reads returned 401.
Portal `https://127.0.0.1:63417/` and guest `https://192.168.0.191:8443/order` returned 200
over trusted Windows HTTPS; guest-edge `/kitchen` returned 404. No guest request was accepted
or rejected and no payment was submitted by these checks. Readiness checks do not prove
phone trust, cashier decision workflows, physical display/printer behavior or production readiness.

The fresh pack has **all seventeen cases pending**, with valid installed-target binding and
fresh candidate proof. The earlier UX24 pack retains its eight passes on its historical target.
Resume as the original cashier, open **QR requests**, and test a new synthetic guest request:
review, confirm and accept it once; then use **View original order** to verify preparation
and existing payment collection. Also test rejection, guest price confirmation and a competing
portal decision without duplicate Kitchen work. Record actual observations against this new pack.
