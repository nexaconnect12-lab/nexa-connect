# Cashier checkout acceptance

## Implemented scope

The WPF cashier has Checkout, Payment, Shift & cash, and Terminal & sync views. Checkout uses named product tiles, case-insensitive name search, preparation-station filters, quantity controls, explicit currency totals, and a separate payment step. Station filters use the existing menu contract, not Catalog categories. Cashier operations do not require typing service identifiers; deployment still supplies the organization, restaurant, branch, store, and terminal IDs.

The header identifies the configured branch/terminal with abbreviated IDs and displays authentication/shift state. It does not resolve employee display names or branch names. Connectivity is checked by each operation; the header does not claim continuous health or offline order capability. The layout supports a minimum 1000 × 680 device-independent window with scrollable payment and management views, 48-unit action buttons, keyboard focus indicators, and accessible input names.

Service authorization, tenant ownership, Order totals, cash attribution, and transactional audit/event publication remain authoritative. UI visibility is not authorization. The cart rejects products whose menu currency differs from the configured checkout currency; it does not convert currencies. No service schema or endpoint changed in this slice.

## Recovery and operator boundaries

- An operation disables the workspace and sign-in/out actions while awaiting its result; browser sign-in keeps the sign-in button available as Cancel sign-in. Cart edits require an authenticated open shift without a pending payment.
- A pending settlement opens the Payment view after restart. Attempted tender fields become read-only. The saved method wins over a changed configured default.
- Recovery is persisted with `OutcomeUncertain=true` before sending payment confirmation. Failure to persist prevents the network call. Timeout, rejected HTTP responses, process loss, or failure to remove recovery state retain the same settlement identity for verification.
- The cashier must explicitly confirm receipt before the first settlement. Verify payment reuses the original command and warns against collecting again. PromptPay requires the configured readable QR, receipt confirmation, and reference.
- Close cash is unavailable while any payment is pending or that session has queued/rejected cash movements. Close shift requires a closed cash session and no pending payment. Sign-out remains blocked while any of these are active.
- Manual cash movements do not settle or refund an Order. The screen explains that cash checkout is projected automatically when the settlement consumer is enabled.

## Automated verification

From the repository root:

```powershell
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj --no-restore --verbosity minimal
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~CashierPresentationTests|FullyQualifiedName~SettlementAttemptTests|FullyQualifiedName~ManualTender|FullyQualifiedName~PosCashSessionApplicationTests|FullyQualifiedName~PosShiftApplicationTests' --verbosity minimal
```

Windows protected-state tests require the normal interactive user's DPAPI key store. In a disposable test run, set `NEXACONNECT_POS_DPAPI_ACCEPTANCE=1` and include `FullyQualifiedName~PosPendingSettlementRecoveryTests` in the filter. The tests create isolated temporary files. They do not drive WPF or authenticate against Keycloak.

The preceding implementation turn passed three DPAPI cases under the normal Windows profile. The sandbox DPAPI run failed because user key storage was unavailable. Final post-correction verification passed 35 focused cases and a WPF build with no warnings or errors. The normal output build was blocked by the running POS executable; the successful build used .runstate/cashier-verification as its output directory. The initial signed-out Checkout window was visually inspected. These are component results, not joined checkout acceptance. Final verification for this slice is recorded in the implementation handoff.

## Live acceptance procedure — not yet signed off

Use a disposable test branch, cashier identity, matching menu, enrolled terminal, and the required Order 5 / Authorization 6 / Reporting 14 / POS 4 migrations. Configure `Pos:Currency=THB` for manual tender; the existing local configuration may still use SGD and must be corrected for this test setup. Configure all service URLs and a test-only PromptPay QR. The POS launcher alone does not prove the whole Catalog/Inventory/Kitchen/Order graph is ready.

1. Sign in interactively using OIDC. Open a shift and a THB cash session with a known opening amount.
2. Load the menu; test search, station filtering, unavailable products, keyboard focus, quantity increase/decrease, and removal at quantity one. Check the actual terminal resolution and display scaling.
3. Submit one cash order. Confirm the authoritative amount and Kitchen ticket. Rapid repeated clicks must not submit concurrent UI operations. Confirm received cash once.
4. Repeat with manually verified PromptPay. Missing QR/reference/receipt confirmation must prevent settlement. Verify no drawer movement is attributed to PromptPay.
5. In disposable infrastructure, interrupt a settlement response and restart the client under the same Windows user. The Payment view must restore the original amount, method, reference, and verification action. Verify once and confirm one Order settlement/event projection.
6. Repeat with a denied branch permission and a concurrent settlement. Confirm recovery is retained, no automatic new payment is made, and operator guidance explains reconciliation.
7. Queue a cash movement with the service unavailable, restore it, and sync. Rejected/pending movements must block cash closure. Enter the counted cash, close cash, then close shift. Inspect server-side reconciliation and audit; the current UI does not display a full expected-versus-counted reconciliation report.

Retain sanitized boolean evidence for these scenarios. Do not include tokens, QR banking details, receipt references, personal data, or full HTTP bodies. Record real OIDC, WPF interaction, service graph, and terminal hardware coverage separately.

Keep the configured organization, branch, and terminal unchanged while a settlement is pending. The protected record retains the order, amount, currency, idempotency key, and tender fields; request scope and terminal still come from current configuration. Reconcile pending work before moving a terminal to another scope.

## Remaining boundaries

Full offline order/shift synchronization, SQLite, hardware drivers, human-readable identity context, full reconciliation reporting, and Thai localization are subsequent work. Order placement itself still generates a new identity per API call; a lost placement response is not covered by settlement replay. Reconcile an ambiguous placement before attempting another order. Broader durable placement recovery must be addressed before claiming duplicate-safe checkout across all failure points. An abrupt exit between the placement response and saving pending settlement also remains a recovery gap.

## Sign-in recovery check

Browser sign-in now offers Cancel sign-in and expires after three minutes if no callback completes. Cancellation restores the controls and retains pending cashier work. A late callback from a cancelled attempt cannot install credentials. When already authenticated with an active shift, cash session, or pending settlement, both Sign in and Sign out can remain disabled intentionally; finish that work before signing out.

Click Sign in, then Cancel sign-in without completing the browser flow. Confirm Sign in becomes available again. Repeat and leave the browser flow unfinished for three minutes; controls must recover with a timeout message. Complete a fresh sign-in after either case. Live execution of these checks remains pending.
