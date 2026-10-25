# QR6 actual-phone and cashier checklist

[QR9](QR9-Acceptance.md) now generates a bounded checklist for each case/device, validates explicit observed facts and links immutable evidence to QR7 records with retry-safe recording. Use it for current-PC acceptance; no phone/payment/PDF pass is automatic.

For the current-PC test option selected in QR7, use [the isolated local profile/guest edge](QR7-Pilot.md) once LAN/certificate/device facts are supplied. Dedicated-server facts remain prerequisites for that separate hosted topology; they do not prevent preparing the local-PC plan. All actual cases below still start pending and require observation.

All actual results start pending. For QR7, use its protected plan and append-only `Record`
command, which bind the candidate, configured local environment and device facts. For the
dedicated-server topology, use the generated `phone-test.json` for the exact candidate as
an operator-attestation template and record the matching hosted source,
configuration/installer binding and environment ID before testing. Record both phone
models, OS/browser versions, observation UTC time and opaque evidence IDs. Do not store
QR secrets, cookies, tokens, bank references, unrestricted screenshots or financial bodies
in candidate reports. No script marks these cases passed automatically.

## Network and staff prerequisites

1. Select the prepared topology. For the local PC, follow [QR7](QR7-Pilot.md), including
   the exact LAN hostname, private address, trusted HTTPS and device facts. For the
   dedicated server, first confirm OS/capacity, domain/DNS, backup and recovery facts and
   follow [Phase6 hosting](Next-Iteration-Phase-6.md). Keep prior Phase6–9 gates pending.
2. Expose the QR7 guest-only edge, or publish the Customer Portal/BFF through the approved
   dedicated-server ingress, on HTTPS trusted by both phones. Set the BFF's exact
   `QrGuest__PublicOrigin` including a nonstandard port if used. The QR
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
| cash-original-order | In POS Orders, filter QR orders and select the eligible original row, or use the reference/UUID fallback. Verify original items/prices/tax/total and exact collecting custody. Receive exact cash, then confirm. Paid/claimed history cannot start another collection. One original Paid Order/cash movement; no second stock/Kitchen effect. |
| manual-promptpay-original-order | Use a separate accepted Order. Check the configured payment image, verify actual test payment/receipt/reference and confirm once. One original settlement/receipt; no cash movement. No real transfer is authorized by this checklist. |
| switch-app-lock-resume | Switch apps on phone and idle-lock Windows POS with retained work. Original cashier authenticates/resumes; staff history rows/cursor clear and late list responses cannot restore them. Customer display uses its safe paused view and never shows branch history. No automatic logout loses operational recovery. |
| network-loss-original-recovery | Interrupt phone/network and cashier response separately, including after commit. Reconnect/restart and verify the original request/payment. Do not rescan/recreate/collect again to clear uncertainty. |
| qr-revocation-scope-denial | Revoke/rotate a test entry and try old/guessed/wrong-scope work. Guest/pending acceptance fails closed; another table remains usable. Already accepted original Order recovery continues. |
| paid-receipt-pdf-reprint | Original live visit shows safe Paid receipt; cashier loads/prints/reprints the same receipt to PDF. Verify actual saved PDF. Cancelled/failed print does not change Paid or authorize another collection. Ended/revoked visits cannot retrieve by guessing the reference. |
| cash-reporting-reconciliation | Observe actual broker delivery, exactly one cash/reporting effect and original financial breakdown/receipt identity. Gaps remain gaps, not readiness. Complete the separately authorized cash/shift reconciliation; next-day/day-close gates remain distinct. |

Stop a failed case, retain its original identifiers privately and record the blocker/retest
against this same candidate and environment. Ask staff to reconcile inaccessible immutable
collection custody; do not delete local state or invent a replacement Order. A later fix
requires a new candidate and fresh affected tests. Final user acceptance and release freeze
require actual evidence, not completed source checks or a manually edited template.
