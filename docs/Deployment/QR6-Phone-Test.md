# QR6 actual-phone and cashier checklist

All actual results start pending. Use the generated `phone-test.json` for the exact local
candidate as an operator-attestation template, then record the matching hosted source,
configuration/installer binding and environment ID before testing. Record both phone
models, OS/browser versions, observation UTC time and opaque evidence IDs. Do not store
QR secrets, cookies, tokens, bank references, unrestricted screenshots or financial bodies
in candidate reports. No script marks these cases passed automatically.

## Network and staff prerequisites

1. Confirm the dedicated-server OS/capacity, domain/DNS, backup and recovery facts first.
   Follow [Phase6 hosting](Next-Iteration-Phase-6.md); keep prior Phase6–9 gates pending.
2. Publish the Customer Portal/BFF on a hostname with HTTPS trusted by both phones.
   Set its exact `QrGuest__PublicOrigin` including a nonstandard port if used. The QR
   destination is that origin's `/order#<opaque-entry-token>`. Print/handle it as a
   capability; do not place it in query parameters, access logs or public documentation.
3. Phones need DNS/routing to that hostname (same LAN or explicitly configured public
   ingress). `localhost` on a phone points to the phone, not the server. A static public IP
   alone does not supply a trusted certificate/domain, firewall or HTTPS routing. Do not
   bypass TLS errors, expose private services/databases/management routes or disable Origin
   checks. Keep the existing edge denial of direct guest-capability/internal service APIs.
4. Deploy current owning migrations/binaries, explicit QR flags and exact workload admission
   configuration; verify healthy consumers, actual least-privilege grants and broker bindings.
   Open manager/staff QR review, `/kitchen`, and the matching Windows POS on separate authorized
   contexts. Cashier requires its original registered terminal/store/open shift and THB drawer
   for cash; manual PromptPay needs its configured local image and explicit verification.
5. Use an approved test branch/menu/stock, THB and the intended Asia/Singapore business timezone.
   Prepare Windows PDF printing and preserve DPAPI/SQLite original recovery. Never reset a
   retained draft to prepare tests. QR requests are separate Orders; no combined table bill,
   guest payment/cancel command, provider transfer or custody reassignment is implemented.

## Execute on iOS Safari and Android Chrome

Run these separately on each phone, with two independent devices/browser profiles where
the case calls for multiple guests. Tabs in one browser may share its visit cookie.

| Case ID | Action and expected result |
| --- | --- |
| camera-scan-https | Scan the current table QR with the camera and open trusted HTTPS. Correct restaurant/branch/table appears; fragment disappears; no staff sign-in or secret in visible URL/history navigation. |
| independent-guests-same-table | Scan on the other independent phone/profile. Same table, separate visitor/request history. Neither guest can see the other's requests/receipts. |
| menu-cart-price-review | Add/edit quantities, rotate portrait/landscape and review server totals. Change price/policy before acceptance: guest must explicitly confirm the exact changed breakdown before staff can accept. |
| submit-refresh-original | Submit once, refresh and switch away/back. Original request returns; pending blocks duplicate submission. Lost response retries the original identity. |
| staff-accept-kitchen-once | Before acceptance, no stock/Kitchen effects. Staff accepts once; concurrent retry gives the same Order, one reservation and one Kitchen ticket. Observe actual Kitchen transition; acceptance alone is not ticket completion. |
| cash-original-order | Cashier loads the reference/original UUID, original items/prices/tax/total and exact collecting custody. Receive exact cash, then confirm. One original Paid Order/cash movement; no second stock/Kitchen effect. |
| manual-promptpay-original-order | Use a separate accepted Order. Check the configured payment image, verify actual test payment/receipt/reference and confirm once. One original settlement/receipt; no cash movement. No real transfer is authorized by this checklist. |
| switch-app-lock-resume | Switch apps on phone and idle-lock Windows POS with retained work. Original cashier authenticates/resumes; customer display uses its safe paused view. No automatic logout loses operational recovery. |
| network-loss-original-recovery | Interrupt phone/network and cashier response separately, including after commit. Reconnect/restart and verify the original request/payment. Do not rescan/recreate/collect again to clear uncertainty. |
| qr-revocation-scope-denial | Revoke/rotate a test entry and try old/guessed/wrong-scope work. Guest/pending acceptance fails closed; another table remains usable. Already accepted original Order recovery continues. |
| paid-receipt-pdf-reprint | Original live visit shows safe Paid receipt; cashier loads/prints/reprints the same receipt to PDF. Verify actual saved PDF. Cancelled/failed print does not change Paid or authorize another collection. Ended/revoked visits cannot retrieve by guessing the reference. |
| cash-reporting-reconciliation | Observe actual broker delivery, exactly one cash/reporting effect and original financial breakdown/receipt identity. Gaps remain gaps, not readiness. Complete the separately authorized cash/shift reconciliation; next-day/day-close gates remain distinct. |

Stop a failed case, retain its original identifiers privately and record the blocker/retest
against this same candidate and environment. Ask staff to reconcile inaccessible immutable
collection custody; do not delete local state or invent a replacement Order. A later fix
requires a new candidate and fresh affected tests. Final user acceptance and release freeze
require actual evidence, not completed source checks or a manually edited template.
