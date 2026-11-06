# Compact cashier workspace

## Menu and payment follow-up

The cashier navigation labels the cart and item workspace **Menu**. The cashier name and
session status share one header row. Select **Review order total** to see the server-priced
lines and total in Menu, then **Send order · Continue to payment**. Editing the cart
or changing the checkout method discards the quote. An original order that needs repricing
uses the same inline review with its original order identity.

In **Payment**, **Confirm payment received** submits the original amount and tender
without a modal prompt. PromptPay still requires verified proof and a reference.
Uncertain payments use **Verify payment** on the original attempt. Refunds, cancellation,
cash review and drawer authorization follow their own safeguards.

## Scope and status

This update reduces routine text and expands menu browsing in the existing WPF POS. Compact release 9ab9d05b76ab45d9b4a87e51a5c2f6ea introduced this layout. It is superseded by the matching [refund-selector release](POS-Refund-Payment-Selection.md), with existing business/PIN/recovery state preserved and fresh operator/physical-device acceptance pending. No API, schema, permission, payment calculation, identity policy or financial recovery format changes are introduced.

## Cashier layout

- A compact header shows the brand, verified cashier, session/shift status and Lock. Full sign-in remains visible when needed. **More** reveals the original sign-out, Windows keyboard, service-check and branch/terminal details; their existing guards remain authoritative.
- Checkout omits the duplicate ready banner. The ready identity/register/start-sale card remains on **Shift & cash** when idle.
- Search and category selection share one row. **More filters & scanner** contains station selection, refresh, clear filters, category status and the existing explicitly enabled scanner controls, in a scroll area bounded to160DIP. Its toggle retains the selected Standard/Touch target height (36/52DIP). Empty/no-match states retain direct Refresh menu/Clear filters actions. The Terminal scanner shortcut opens the advanced section without enabling scanning or submitting anything. Collapsing it moves focus back to search; automatic input follows the saved input preference.
- Menu tiles fit the available width, with the complete name available through its accessible name/tooltip, a bounded two-line visual name, optional existing approved image, price and conditional sold-out label. Repeated preparation-station labels are removed from tiles; station filtering remains. Existing item identities/prices and photo isolation are unchanged.
- The cart column uses280/330/380DIP at window widths below1100/below1500/otherwise, giving the remaining width to the menu. The original cart-total text remains as an internal presentation source; the pinned workflow total is the single visible total. Existing quantity controls, payment selector and server pricing breakdown remain. Routine sandbox-capability text is hidden for manual checkout; sandbox and retained-method guidance remains when relevant.
- Routine workflow explanations are collapsed. Payment uncertainty, retained-order/refund/cancellation, sync, prerequisite and busy guidance stays visible at all supported heights. The status area remains visible and scrollable; no business/recovery warning is converted into an automatically dismissed toast.

The cashier uses [borderless full-monitor placement](Next-Iteration-Phase-4.md#setup-and-recovery).
Menu and cart have independent scrolling during ordinary sales, while keyboard and form
views use the outer scroll area. Standard input and density are the defaults. Touch input
and Touch density remain independent preferences.

## Verification and rollout

The isolated native fixture checks Standard/Touch at1093×614,1366×768 and1920×1080 logical sizes, default collapsed options, at least three Standard menu columns, menu height, no whole-checkout scroll, a reachable pinned action/total, visible uncertainty guidance and access to secondary controls. Logical1093×614 approximates a1366×768 display at125% scaling; it is not actual Windows DPI/device acceptance. Existing native payment, scanner, keyboard, session, display, QR, photo and shutdown fixtures remain required.

Synthetic review images are written to `.runstate/pos-compact-checkout-{width}.png`; the sample-cart preview remains `.runstate/pos-win11-cashier-preview.png`. These use temporary storage and simulated identities/items, never retained transactions. Review images visually as well as checking geometry. Run:

```powershell
dotnet run --project tests/Windows/NexaConnect.PosTouchTests -- --shell-preview
dotnet run --project tests/Windows/NexaConnect.PosTouchTests
```

Complete source verification and prepare the matching package before activation. Finish current work and close POS before a local refresh; retain orders, shifts, PIN registry/key rings, settings, galleries and recovery. Actual cashier/hardware acceptance and installed-target evidence remain separate from source tests. The current retained installation is recorded below; no operator pass is inferred from it.

## Executed source verification and prepared package — 4 November 2026

The final source-bound gate `.runstate/qr6/adcff21a-63d9-4cd5-a896-aae9f2f07141/verification.json` passes 1,468 unit, eight architecture, 150 HTTP, 99 frontend unit and 161 browser cases. Fifteen owning persistence/broker cases and four drawer-authority cases pass with verified cleanup. The final native fixture includes the compact layout, Standard/Touch targets, workflow, payment, keyboard, scanner, session, display and shutdown checks. POS build and printing checks pass with zero build warnings/errors; both production and full dependency audits report zero vulnerabilities.

Candidate `adcff21a-63d9-4cd5-a896-aae9f2f07141` has binding `50d16dd943f767978b70c2a4f71e96273d83e9baa40f56e79a107ebdebaf1d1e`. Matching 23-file package `9ab9d05b76ab45d9b4a87e51a5c2f6ea` was prepared with validated package inventory and matching raw native source identity, then activated as recorded below. It remains a working-tree local-test-candidate, with release/production readiness false. Metadata is retained in `.runstate/pos-compact-cashier-current.json`.

The final shell preview and three compact logical-size previews are generated at the paths above. Visual review of the sample-cart preview and 1093×614 fixture confirms the compact header, reachable secondary controls, expanded menu area and one visible pinned total/action. These are synthetic images, not actual cashier/hardware acceptance.

The earlier closure wait ended with explicit user confirmation and the matching activation recorded below. Prior PIN release `b84f38efc111407fb7ddd05dac646825` is now historical; its PIN policy remains unchanged in the compact release. Source tests and installation do not transfer historical operator passes.

## Retained pilot activation — 4 November 2026

After explicit user confirmation that POS was closed, release `9ab9d05b76ab45d9b4a87e51a5c2f6ea` is installed on retained run `c30b628ab23141f389119daa565f894f`. QR pilot `574227f0ed214ecfb559c6bf9d5bf219` and supervisor `931b7e2b47ac4cf4aae053a1dac23ae1` are active. The supervisor runs independently under the Windows scheduler (`svchost` parent), with automatic Windows sign-in startup off. Native verification confirms the exact installed executable path, a responsive reopened POS and a running QR listener. Source-gate evidence above remains unchanged; POS26/Authorization17 and PIN/session policy are retained.

Preservation receipts confirm nine protected local files and 161 business tables unchanged, including existing PIN credential/audit data, with five stable Authorization tables and all permissions unchanged. Both existing key-ring files, credential values, configuration, reference scope, certificate bytes and URLs are unchanged. No agent PIN writes/enrollment, financial action or media mutation is performed.

Real-cashier PKCE checks pass menu and scoped QR-order reads. PIN status returns 200 with the preserved active credential at version 1; anonymous access returns 401 and a wrong terminal returns 403. This checks credential metadata/authorization, not a real PIN unlock. Real-manager BFF grid/details/picker/cancel checks pass without business mutation. Trusted portal and guest ordering HTTPS return 200; guest Kitchen returns 404. Evidence is retained in `.runstate/pos-compact-cashier-current.json` and `.runstate/pos-compact-cashier-activation/`.

Fresh acceptance `ffc05c90a0194d59b6466db712bc85d0` has valid installed-target binding and a fresh QR candidate, with all seventeen operator cases pending. The original PIN target is superseded, with no historical passes transferred. Actual cashier/PIN workflow, physical Windows scaling/DPI, scanner/display/peripheral and provider-policy acceptance remain separate; installation does not establish release readiness or production verification.

The activation snapshot above is historical and is superseded by the [readable refund-selector activation](POS-Refund-Payment-Selection.md#retained-pilot-activation), which retains the compact layout, original PIN and recovery behavior.
