# QR8 — workflow usability and status feedback

QR8 adds presentation and read-refresh behavior to the existing table QR journey.
No API, database, pricing, permission, custody, idempotency or settlement contract changes.

## Operator behavior

- Guest and staff requests display Waiting for staff, Review updated prices, Preparing,
  Awaiting cashier, At cashier and Paid, as supported by the original service response.
  These are presentation labels, not additional order or Kitchen states. Preparing does
  not claim that food is ready; Awaiting cashier does not claim payment has completed.
- The staff QR queue refreshes every 15 seconds while mounted, visible and focused.
  Reads do not overlap; acceptance/rejection pauses polling. Changing branch/state or
  leaving the panel fences late reads; requests have a 20-second timeout and unmount
  aborts outstanding work. Decisions still require explicit CSRF/versioned actions.
  Visibility/focus changes also trigger an eligible refresh. Losing focus suppresses
  new automatic reads; it does not cancel an already-started browser request or roll
  back a submitted decision. Reload the original request after an uncertain decision.
- Native POS Orders refreshes its current page every 15 seconds only while signed in,
  selected, active and not busy/loading. Refresh retains the selected UUID if still on
  that page, re-evaluates eligibility and never claims or pays automatically. Tab exit,
  deactivation, lock and sign-out clear rows and invalidate in-flight reads. Returning
  from deactivation starts again at the first page on the next timer tick; Refresh is
  available immediately. No polling counts as operator activity or extends idle time.
- The collection footer explains a closed shift, cart, pending checkout/payment,
  incompatible tender/currency, non-QR row, prior claim/payment or unavailable eligibility.
  Server preview, original cashier/shift custody and settlement remain authoritative.
- Guest status uses the same visible/focused refresh policy. A slow submission displays
  guidance, times out after 20 seconds and retains its original quote identity for
  explicit retry. Timeout/abort does not establish that the server rejected the request.
  Refreshing checks the original live visit; rescanning starts another visit and cannot
  recover it. Quote identity stays in memory; no tokens or request data enter browser storage.
  A full page reload loses the in-memory retry identity; it can recover a committed
  request through the same visit cookie, but does not itself prove that an absent or
  unavailable request failed. Keep the original page open when an outcome is uncertain.

## Verification and deployment

Run focused POS order-list tests, frontend type checks, QR status/refresh tests and the
Chromium/WebKit QR suite. `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` supplies
the integrated request/acceptance/Inventory/Kitchen/collection/payment/receipt and broker
recovery evidence with synthetic identity/transport boundaries explicitly retained.

Source changes invalidate the prior QR7 activation proof. Stop the owned edge, rebuild
the same retained QR-enabled stack, package/install POS after the operator closes it,
then create a fresh candidate and immutable QR7 activation using the same configured
scope/origin and exact certificate files. Preserve all business volumes and POS recovery.
See [QR7 activation](QR7-Pilot.md) and [Orders usage](POS-Order-List.md).

Actual iPhone/Android, populated WPF selection, cashier payment/PDF and customer-display
acceptance remain pending operator observation. Safari's certificate warning remains a
separate trust/prerequisite issue; browser emulation does not resolve it. Dedicated-server
activation, hardware, signing and production release remain deferred.

## Local verification — 25 October 2026

Candidate `0da26fb4-f842-4c83-a57d-8dca3ccad191` passed against revision
`d4e5ce88d77f053afab7d3bfd84fdcc633533bc4`, dirty source, canonical non-Markdown
hash `a7a3df3a48287168acabb4784b797661fe920551e7ead5c3f58e63b8b1ca09f7`
(1,839 files). Its protected QR6 verification records:

| Gate | Result |
| --- | --- |
| .NET unit / architecture / HTTP | 1,043 / 6 / 27 passed |
| Frontend unit / QR browser | 88 / 57 passed |
| Hosting / candidate guards | 47 / 20 passed |
| Financial browser suites | 8 + 8 + 22 + 7 passed |
| Owning PostgreSQL/RabbitMQ journeys | 6 passed; cleanup and broker recovery verified |
| POS and frontend builds, type checks, both dependency audits | Passed |

POS release `eaebcbf1db004e2bb2e0a9537231e572` is installed for retained stack
`c30b628ab23141f389119daa565f894f`. Closed-profile recovery hashes matched after
installation; executable preflight returned 0 and its Cashier window responded.
The original default profile's three recovery files also remained byte-identical.
The installed terminal's legacy input binding separately matched current package inputs;
its digest scheme differs from the canonical QR6 source inventory.

Fresh activation `9cf846aeed4c45b2890a05280bec5cdf` retains the same branch scope,
`https://192.168.0.191:8443` and exact installed certificate files. Trusted HTTPS page
and script reads returned 200; the guest edge denied a staff route with 404. Bounded
observations are in that activation's protected `qr8-rollout-verification.json`.
All 24 device cases remain pending, and `releaseReady`/`productionVerified` remain false.
The earlier order-list candidate is historical evidence for its own source inputs.
