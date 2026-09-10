# Cashier checkout acceptance

## Implemented scope

The WPF cashier has Checkout, Payment, Shift & cash, and Terminal & sync views. Checkout uses named product tiles, case-insensitive name search, preparation-station filters, quantity controls, explicit currency totals, and a separate payment step. Station filters use the existing menu contract, not Catalog categories. Cashier operations do not require typing service identifiers; deployment still supplies the organization, restaurant, branch, store, and terminal IDs.

The header identifies the configured branch/terminal with abbreviated IDs and displays authentication/shift state. It does not resolve employee display names or branch names. Connectivity is checked by each operation; the header does not claim continuous health or offline order capability. The layout supports a minimum 1000 × 680 device-independent window with scrollable payment and management views, 48-unit action buttons, keyboard focus indicators, and accessible input names.

Service authorization, tenant ownership, Order totals, cash attribution, and transactional audit/event publication remain authoritative. UI visibility is not authorization. The cart rejects products whose menu currency differs from the configured checkout currency; it does not convert currencies. No service schema or endpoint changed in this slice.

## Recovery and operator boundaries

- An operation disables the workspace and sign-in/out actions while awaiting its result; browser sign-in keeps the sign-in button available as Cancel sign-in. Cart edits require an authenticated open shift without a pending payment.
- A shift permits one cash session. After closing cash, close that shift and open a new shift before opening cash again. A repeated open must return a safe `409` with this guidance rather than an HTTP `500` or provider detail.
- With a valid shift and cart, Send order remains actionable. For `cash_manual` without an open cash session, selecting it navigates to Shift & cash and explains the prerequisite without creating an order or pending-checkout record.
- A pending settlement opens the Payment view after restart. Attempted tender fields become read-only. The saved tender is restored; a pending checkout also requires the original configured checkout mode and scope before startup can proceed.
- Recovery is persisted with `OutcomeUncertain=true` before sending payment confirmation. Failure to persist prevents the network call. Timeout, rejected HTTP responses, process loss, or failure to remove recovery state retain the same settlement identity for verification.
- The cashier must explicitly confirm receipt before the first settlement. Verify payment reuses the original command and warns against collecting again. PromptPay requires the configured readable QR, receipt confirmation, and reference.
- Close cash is unavailable while any payment is pending or that session has queued/rejected cash movements. Close shift requires a closed cash session and no pending payment. Sign-out remains blocked while any of these are active.
- Manual cash movements do not settle or refund an Order. The screen explains that cash checkout is projected automatically when the settlement consumer is enabled.

## Automated verification

From the repository root:

```powershell
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj --no-restore --verbosity minimal
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~CashierPresentationTests|FullyQualifiedName~SettlementAttemptTests|FullyQualifiedName~PosCheckoutIntegrationTests|FullyQualifiedName~ManualTender|FullyQualifiedName~PosCashSessionApplicationTests|FullyQualifiedName~PosShiftApplicationTests' --verbosity minimal
```

Windows protected-state tests require the normal interactive user's DPAPI key store. In a disposable test run, set `NEXACONNECT_POS_DPAPI_ACCEPTANCE=1` and include `FullyQualifiedName~PosPendingSettlementRecoveryTests` in the filter. The tests create isolated temporary files. They do not drive WPF or authenticate against Keycloak.

The preceding implementation turn passed three DPAPI cases under the normal Windows profile. The sandbox DPAPI run failed because user key storage was unavailable. Final post-correction verification passed 35 focused cases and a WPF build with no warnings or errors. The normal output build was blocked by the running POS executable; the successful build used .runstate/cashier-verification as its output directory. The initial signed-out Checkout window was visually inspected. These are component results, not joined checkout acceptance. Final verification for this slice is recorded in the implementation handoff.

## Live acceptance procedure — not yet signed off

Use a disposable test branch, cashier identity, matching menu, enrolled terminal, and the required Order 5 / Authorization 6 / Reporting 14 / POS 4 migrations. The client requires `Pos:Currency=THB` and `cash_manual` or `promptpay_manual`; incompatible configuration is rejected at startup. Configure all service URLs and a test-only PromptPay QR. The POS launcher alone does not prove the whole Catalog/Inventory/Kitchen/Order graph is ready.

1. Sign in interactively using OIDC. Open a shift and a THB cash session with a known opening amount.
2. Load the menu; test search, station filtering, unavailable products, keyboard focus, quantity increase/decrease, and removal at quantity one. Check the actual terminal resolution and display scaling.
3. Submit one cash order. Confirm the authoritative amount and Kitchen ticket. Rapid repeated clicks must not submit concurrent UI operations. Confirm received cash once.
4. Repeat with manually verified PromptPay. Missing QR/reference/receipt confirmation must prevent settlement. Verify no drawer movement is attributed to PromptPay.
5. In disposable infrastructure, interrupt a settlement response and restart the client under the same Windows user. The Payment view must restore the original amount, method, reference, and verification action. Verify once and confirm one Order settlement/event projection.
6. Repeat with a denied branch permission and a concurrent settlement. Confirm recovery is retained, no automatic new payment is made, and operator guidance explains reconciliation.
7. Queue a cash movement with the service unavailable, restore it, and sync. Rejected/pending movements must block cash closure. Enter the counted cash, close cash, then close shift. Inspect server-side reconciliation and audit; the current UI does not display a full expected-versus-counted reconciliation report.
8. Interrupt the placement response and restart under the same Windows user before payment recovery is saved. Verify the original order and confirm its order identity and settlement key are retained. Intermediate outcomes and unrecognized conflicts must keep checkout locked; a matching terminal Rejected or PaymentFailed result releases the checkout lock. Changing terminal scope or checkout mode must reject recovery before another HTTP placement.

Retain sanitized boolean evidence for these scenarios. Do not include tokens, QR banking details, receipt references, personal data, or full HTTP bodies. Record real OIDC, WPF interaction, service graph, and terminal hardware coverage separately.

Keep terminal configuration unchanged while checkout or settlement is pending. New checkout records retain and validate organization, restaurant, branch, store, terminal, currency, and checkout mode. Legacy settlement-only records retain order, amount, currency, idempotency key, and tender fields but still obtain request scope from current configuration. Reconcile pending work before moving a terminal to another scope.

## Remaining boundaries

Full offline order/shift synchronization, SQLite, hardware drivers, human-readable identity context, full reconciliation reporting, and Thai localization are subsequent work. Order placement now retains a protected original command before sending and reuses it after timeout/restart, including the response-to-settlement-save window. Incomplete server workflow steps still require reconciliation; this client slice does not automatically resume them. Only a matching terminal Rejected or PaymentFailed result releases the checkout lock; other failed commands remain retained.

## Sign-in recovery check

Browser sign-in offers Cancel sign-in and expires after three minutes if no callback completes. Cancellation restores the controls and retains pending cashier work. A late callback from a cancelled attempt cannot install credentials. During active shift, cash, checkout, or settlement work, Sign out remains clickable and explains the required recovery step without clearing saved work. Close shift remains clickable when a shift exists and similarly explains cash, checkout, payment, or authentication blockers.

Click Sign in, then Cancel sign-in without completing the browser flow. Confirm Sign in becomes available again. Repeat and leave the browser flow unfinished for three minutes; controls must recover with a timeout message. Complete a fresh sign-in after either case. For access-token expiry, leave the client idle until expiry is detected, confirm Sign in becomes available, confirm the saved shift/payment state remains visible, sign in again, and continue or close that state. Exercise Sign out and Close shift with each active blocker and confirm the footer identifies the next action. Live execution of these checks remains pending.

## Checkout integration verification

Post-integration acceptance includes direct Catalog routing with tenant/correlation headers, identical placement replay after a simulated lost response, rejection of changed terminal scope before HTTP, invalid configuration guards, and DPAPI restart/corruption tests for both placement and settlement. The original placement identity is generated once and retained until successful payment cleanup. Server intermediate states are retained for explicit verification/reconciliation; the client does not automatically resume abandoned service workflow steps.

The local preflight passes with the required secret names configured, and the supervised launcher reported all nine HTTP hosts ready on 2026-09-10. The development database was separately checked for enabled `nexa_connect` access and matching branch ownership. POS organization membership and branch-scoped cashier grants use the stable Keycloak `sub`; username aliases such as `nexa_pos` do not satisfy the identity claims contract. The expanded focused suite passed 69 cases with five opt-in cases skipped in the final non-interactive run; the protected-state matrix had previously passed under the interactive Windows profile. Joined local OIDC/WPF cash checkout was then verified separately by the evidence gate described below.

For the current WPF startup path, run `./scripts/run-checkout-development.ps1 -ValidateOnly` first. Once required secret names and configuration pass, run the same script without `-ValidateOnly` (optionally with `-StartInfrastructure`). Keep it running while performing the steps above. The launcher uses a bounded loopback listener probe so protected OpenAPI endpoints do not delay process supervision. Do not run the shift-only POS or Phase 8 launchers on the same ports concurrently.

When Refresh menu returns `403`, follow the correlation into Platform Directory. A denial stating that the token has no stable subject means the persisted `nexaconnect-pos` client is missing its subject mapper or the POS still holds an older token. Apply the checked-in `oidc-sub-mapper`, provision membership and cashier authorization for the emitted `sub`, then close active cash/shift work with the current session and sign in again. Never substitute `preferred_username` as an authorization key.

If Platform Directory succeeds but Catalog or Order reports a Restaurant `401`, inspect the affected service's temporary workload token without logging it. Its audience must include `nexaconnect-api`; apply the checked-in client-level audience mapper to an existing realm and restart that service to clear its cached token. During ambiguous placement recovery, retain the original checkout and use **Verify original order** after the Order token is repaired; do not create a replacement order.

If Restaurant authorization succeeds but Order fails before Catalog receives the request with an SSL frame or handshake error, verify the workflow adapter's `Authentication:TokenEndpoint`. It must use the token endpoint published by local Keycloak discovery (`http` in the checked local stack). Restart the supervised stack after correcting it, retain the original checkout, and verify that order again.

If verification returns a matching terminal `Rejected` or `PaymentFailed` Order result, POS clears the checkout recovery lock and retains any in-memory cart for review because the original order can no longer advance. For all other conflicts and intermediate states, recovery remains locked. Order sends tenant context to Inventory; an Inventory `5xx` is a dependency failure and must not persist the response body as an inventory business rejection.

## Final cash-checkout evidence

Complete these steps in one WPF session against the supervised local stack:

1. Sign in through Keycloak, open a new shift, and open its THB cash session.
2. Refresh the menu, add an item, and send the order. Wait for **Order sent to the kitchen**.
3. In Payment, confirm that the displayed amount and currency match the sale, select Cash, and choose **Confirm payment received** once. Copy the Paid Order ID from the footer.
4. Wait for the POS settlement consumer, enter the counted drawer amount, close cash, then close the shift.
5. Sign out and confirm the header shows **Signed out**.
6. Run:

```powershell
./scripts/verify-pos-cashier-live-acceptance.ps1 `
  -OrderId '<paid-order-uuid>' `
  -ConfirmInteractiveOidc `
  -ConfirmWpfCashCheckout `
  -ConfirmSignedOut
```

The verifier refuses `DOCKER_HOST`, unapproved contexts, and every endpoint except Docker Desktop's local Windows named pipes. It pins Compose to this repository's `docker-compose.yml` and the `nexa-connect` project, then reads only `NexaConnect_Order` and `NexaConnect_POS`. It requires exactly one completed Order and exact-total THB cash settlement; the same amount in one POS cash projection and its Order-linked cash movement; that movement's associated cash session and shift to be closed; and no current-user POS token, active-shift, cash-session, pending-checkout, or pending-settlement file. Only then does it write a sanitized `.runstate/pos-cashier-live/<run-id>/evidence.json`. The confirmation switches attest to the human interaction; the script verifies their durable effects and does not automate or independently prove the UI clicks.

The local gate passed on 2026-09-10 UTC for Order `28cea6a8-c0b8-4a4a-88db-fa60987da366`. Sanitized evidence is retained at `.runstate/pos-cashier-live/ca0dcf3292dd4bec9184204bf6d1c223/evidence.json`; it records confirmed interactive OIDC, WPF cash checkout, sign-out, one amount-matched cash lifecycle, closed cash and shift state, and cleared local credentials/recovery state.
