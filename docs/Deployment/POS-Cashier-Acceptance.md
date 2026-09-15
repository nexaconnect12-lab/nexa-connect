# Cashier checkout acceptance

## Implemented scope

The WPF client has Checkout, Payment, Shift & cash, Cash review, and Terminal & sync views. Checkout uses named product tiles, case-insensitive name search, preparation-station filters, quantity controls, explicit currency totals, and a separate payment step. Station filters use the existing menu contract, not Catalog categories. Cashier operations do not require typing service identifiers; deployment still supplies the organization, restaurant, branch, store, and terminal IDs.

The header identifies the configured branch/terminal with abbreviated IDs and displays authentication/shift state. It does not resolve employee display names or branch names. Connectivity is checked by each operation; the header does not claim continuous health or offline order capability. The layout supports a minimum 1000 × 680 device-independent window with scrollable payment and management views, 48-unit action buttons, keyboard focus indicators, and accessible input names.

Service authorization, tenant ownership, Order totals, cash attribution, and transactional audit remain authoritative. UI visibility is not authorization. The cart rejects products whose menu currency differs from the configured checkout currency; it does not convert currencies. Active reconciliation uses the existing cash schema; supervisor history requires POS migration 5 and Authorization migration 7.

## Recovery and operator boundaries

- An operation disables the workspace and sign-in/out actions while awaiting its result; browser sign-in keeps the sign-in button available as Cancel sign-in. Cart edits require an authenticated open shift without a pending payment.
- Active sessions silently refresh one minute before access-token expiry and persist rotated credentials under Windows data protection. Five minutes without key, click, or touch input locks authenticated actions; the first input after the boundary is consumed by the lock. Idle and ten-hour absolute locks replace online credentials with a nonsecret persisted marker, preserve shift, cash, checkout, payment, outbox, and review recovery across restart, and require interactive Keycloak authentication. No timeout closes a financial session.
- A shift permits one cash session. After closing cash, close that shift and open a new shift before opening cash again. A repeated open must return a safe `409` with this guidance rather than an HTTP `500` or provider detail.
- With a valid shift and cart, Send order remains actionable. For `cash_manual` without an open cash session, selecting it navigates to Shift & cash and explains the prerequisite without creating an order or pending-checkout record.
- A pending settlement opens the Payment view after restart. Attempted tender fields become read-only. The saved tender is restored; a pending checkout also requires the original configured checkout mode and scope before startup can proceed.
- Recovery is persisted with `OutcomeUncertain=true` before sending payment confirmation. Failure to persist prevents the network call. Timeout, rejected HTTP responses, process loss, or failure to remove recovery state retain the same settlement identity for verification.
- The cashier must explicitly confirm receipt before the first settlement. Verify payment reuses the original command and warns against collecting again. PromptPay requires the configured readable QR, receipt confirmation, and reference.
- Close cash is unavailable while any payment is pending or that session has queued/rejected cash movements. Close shift requires a closed cash session and no pending payment. Sign-out remains blocked while any of these are active.
- Shift & cash displays authoritative opening cash, signed net movements, expected cash, counted-cash variance preview, and movement history. Close sends the reviewed concurrency version and terminal scope. If the version changed, the session remains open and the cashier must review refreshed figures before trying again. If the post-close read fails, the UI labels its calculated display as verification pending and permits refreshing the closed session; do not treat it as final until the service returns the closed summary.
- Terminal & sync lists unresolved operation IDs, states, attempt counts, bounded HTTP status, and last-attempt time. Protected request payloads remain hidden and rejected operations have authenticated retry without an unaudited discard action.
- Manual cash movements do not settle or refund an Order. The screen explains that cash checkout is projected automatically when the settlement consumer is enabled.
- Cash review lists closed sessions for the configured store over at most 31 days. Read access and resolve access are separate. Investigate/approve requires a 1-200 character reason, explicit confirmation, and the displayed financial/review versions. Before the request is sent, SQLite schema 2 protects the exact decision identity, scope, reason, and versions. An uncertain response or client restart restores that locked request as **Verify decision**; a conflicting decision and sign-out stay blocked until history proves the request committed or a validation/concurrency rejection proves it did not. A later `403`/`404` retains recovery because access or scope may have changed after the original request. A late cash settlement atomically recalculates stored expected cash and variance, advances the financial version, and supersedes the earlier decision: the session becomes `review_required` when variance remains nonzero or `balanced` when it becomes zero. Review reads derive expected cash from movements so older closed sessions with missing or stale stored expected values remain internally consistent.

## Automated verification

From the repository root:

```powershell
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj --no-restore --verbosity minimal
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~PosLocalSqliteStoreTests|FullyQualifiedName~PosPendingSettlementRecoveryTests|FullyQualifiedName~SettlementAttemptTests|FullyQualifiedName~CashierPresentationTests|FullyQualifiedName~PosCheckoutIntegrationTests|FullyQualifiedName~PosCashSessionApplicationTests' --verbosity minimal
```

Windows protected-state tests require the normal interactive user's DPAPI key store. In a disposable test run, set `NEXACONNECT_POS_DPAPI_ACCEPTANCE=1` and include `FullyQualifiedName~PosPendingSettlementRecoveryTests|FullyQualifiedName~PosLocalSqliteStoreTests` in the filter. The tests create isolated temporary databases and legacy files. They do not drive WPF or authenticate against Keycloak.

Current isolated verification passed all 12 SQLite-store tests, including schema 1-to-2 upgrade, protected Cash Review restart recovery, scope/payload rejection, corruption refusal, and count-only inspection. The complete unit project passed 304 tests with five current-user DPAPI tests skipped in the non-interactive host; the interactive evidence runner requires all 17 protected SQLite/recovery tests to pass. POS and inspector builds completed with no warnings or errors. The earlier protected SQLite/PostgreSQL/RabbitMQ/migration runner passed its then-current 17-case matrix on 2026-09-10; sanitized evidence is retained under `.runstate/pos-paid-workflow/9ec72791806540d28dd2947a55666801`. The expanded runner now requires 20 cases. Joined SQLite-backed checkout evidence is recorded below.

## Extended live acceptance scenarios

Use a disposable test branch, cashier identity, supervisor identities, matching menu, enrolled terminal, and the required Order 5 / Authorization 7 / Reporting 14 / POS 5 migrations. Authorization 7 includes the migration-6 manual-tender grants, and POS 5 includes the migration-4 settlement projection. The client requires `Pos:Currency=THB` and `cash_manual` or `promptpay_manual`; incompatible configuration is rejected at startup. Configure all service URLs and a test-only PromptPay QR. The POS launcher alone does not prove the whole Catalog/Inventory/Kitchen/Order graph is ready.

1. Sign in interactively using OIDC. Open a shift and a THB cash session with a known opening amount.
2. Load the menu; test search, station filtering, unavailable products, keyboard focus, quantity increase/decrease, and removal at quantity one. Check the actual terminal resolution and display scaling.
3. Submit one cash order. Confirm the authoritative amount and Kitchen ticket. Rapid repeated clicks must not submit concurrent UI operations. Confirm received cash once.
4. Repeat with manually verified PromptPay. Missing QR/reference/receipt confirmation must prevent settlement. Verify no drawer movement is attributed to PromptPay.
5. In disposable infrastructure, interrupt a settlement response and restart the client under the same Windows user. The Payment view must restore the original amount, method, reference, and verification action. Verify once and confirm one Order settlement/event projection.
6. Repeat with a denied branch permission and a concurrent settlement. Confirm recovery is retained, no automatic new payment is made, and operator guidance explains reconciliation.
7. Queue a cash movement with the service unavailable, restore it, and sync. Rejected/pending movements must block cash closure. Refresh reconciliation and confirm opening plus signed movements equals expected cash. Enter an exact count and verify zero preview variance, close cash, then close shift. In disposable data, repeat with a non-zero count and verify the closed summary retains the variance. Insert a concurrent movement after review and confirm the first close returns conflict/refreshed guidance without closing.
8. Interrupt the placement response and restart under the same Windows user before payment recovery is saved. Verify the original order and confirm its order identity and settlement key are retained. Intermediate outcomes and unrecognized conflicts must keep checkout locked; a matching terminal Rejected or PaymentFailed result releases the checkout lock. Changing terminal scope or checkout mode must reject recovery before another HTTP placement.
9. Sign in as an accountant and open Cash review. Load a nonzero closed variance and confirm detail/history are readable while both decision actions remain unavailable. Verify a different store/session cannot be disclosed.
10. Sign in as a store manager. Mark the variance investigating with a reason, refresh, then approve it with a second reason. Confirm two ordered immutable history entries, the recorded Authorization decision identities, and one current approved projection. Replay the exact decision operation and confirm no duplicate history.
11. Open the same detail in two clients. Commit one decision, then submit the stale version from the other and confirm `409` plus refreshed state. In disposable data, project a late cash settlement into the closed session and confirm its financial version advances and status returns to review required before another decision can be made.

Retain sanitized boolean evidence for these scenarios. Do not include tokens, QR banking details, receipt references, personal data, or full HTTP bodies. Record real OIDC, WPF interaction, service graph, and terminal hardware coverage separately.

Keep terminal configuration unchanged while checkout or settlement is pending. New checkout records retain and validate organization, restaurant, branch, store, terminal, currency, and checkout mode. Legacy settlement-only records retain order, amount, currency, idempotency key, and tender fields but still obtain request scope from current configuration. Reconcile pending work before moving a terminal to another scope.

## Remaining boundaries

Full offline order/shift/device synchronization, hardware drivers, human-readable identity context, multi-store review, review export/Reporting projection, offline supervisor authorization/execution, and Thai localization are subsequent work. The cashier reconciliation view covers the active or just-closed terminal cash session; Cash review covers online closed-session history for one configured store. SQLite protects current operational state, cash-movement replay, and the exact pending supervisor decision across brief client/service interruption; it does not execute orders or decisions without the service graph or cache offline authorization. Order migration 6 and the enabled Order recovery worker can resume manual-tender server workflows in Submitted or InventoryReserved state through KitchenAccepted; provider-payment and other workflow states still require reconciliation. Only a matching terminal Rejected or PaymentFailed result releases the checkout lock; other failed commands remain retained.

## Sign-in recovery check

Browser sign-in offers Cancel sign-in and expires after three minutes if no callback completes. Cancellation restores the controls and retains pending cashier work. A late callback from a cancelled attempt cannot install credentials. During active shift, cash, checkout, or settlement work, Sign out remains clickable and explains the required recovery step without clearing saved work. Close shift remains clickable when a shift exists and similarly explains cash, checkout, payment, or authentication blockers.

Click Sign in, then Cancel sign-in without completing the browser flow. Confirm Sign in becomes available again. Repeat and leave the browser flow unfinished for three minutes; controls must recover with a timeout message. Complete a fresh sign-in after either case. While producing regular key/click/touch input, remain signed in across at least two five-minute access-token lifetimes and confirm renewal does not interrupt work. Then leave the client untouched for five minutes: the next input must lock rather than execute its command, saved shift/payment state must remain visible, and Sign in must force an interactive Keycloak login. In a disposable configuration, shorten the absolute timeout and confirm it clears online credentials while retaining recovery. Reject and temporarily interrupt refresh separately; rejected refresh must clear credentials, while a transient failure may retry only until the access token expires. Exercise Sign out and Close shift with each active blocker and confirm the footer identifies the next action. Live execution of these session checks remains pending.

## Checkout integration verification

Apply Order migration 6 before starting the updated Order service. Enable `WorkflowRecovery__Enabled=true` only after Catalog, Inventory, and Kitchen endpoints plus Order workload identity are healthy. Defaults are a 5-second poll, 30-second lease, and 15-second retry delay. The worker uses the original Order ID for idempotent downstream replay, retains the original correlation context, and stops manual-tender recovery at KitchenAccepted. Monitor `order.workflow_recovery.pending`, `order.workflow_recovery.oldest_age_seconds`, `order.workflow_recovery.steps`, and `order.workflow_recovery.failures`; these instruments contain no tenant or Order labels.

Run `pwsh -NoProfile -File scripts/test-order-workflow-recovery-live.ps1 -ConfirmDisposableInfrastructure` as the migration-6 recovery release gate. Its real-host matrix passed locally on 2026-09-15 after two controlled process interruptions, one exhausted dependency retry cycle, correlation and exact-transition checks, final cash settlement, and project-scoped cleanup. Retain the generated sanitized TRX/JSON evidence with the release record; see [Order recovery evidence](../Architecture/Evidence/Phase-10-Order-Workflow-Recovery.md).

Post-integration acceptance includes direct Catalog routing with tenant/correlation headers, identical placement replay after a simulated lost response, rejection of changed terminal scope before HTTP, invalid configuration guards, and DPAPI restart/corruption tests for both placement and settlement. The original placement identity is generated once and retained until successful payment cleanup. Server intermediate states remain available for explicit POS verification; when Order recovery is enabled, eligible manual-tender stages also advance in the service under a fenced claim. The client does not independently execute abandoned service workflow steps.

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

The verifier refuses remote Docker, pins this repository's Compose project, and reads only `NexaConnect_Order` and `NexaConnect_POS`. It requires one completed exact-total cash lifecycle with closed session/shift state. Its read-only local-state inspector then requires SQLite schema 2, `quick_check=ok`, zero active operational rows, zero unresolved outbox rows, zero pending Cash Review decisions, no interrupted sends, no legacy state files, and no token file. Only then does it write sanitized evidence. Confirmation switches attest to human interaction; the script does not automate UI clicks.

The historical SQLite-backed local gate passed on 2026-09-10 UTC for Order `60bc4440-4274-4c79-a12a-259a83d94740`, with evidence at `.runstate/pos-cashier-live/dcdceb87b24944aa913c9019b1855bc3/evidence.json`. It verified one completed exact-total cash settlement and POS projection, closed cash session and shift, signed-out client, then-current SQLite schema 1 integrity, zero active operational or unresolved outbox rows, and no retained credentials or legacy recovery files. New runs use schema 2.

## Cash Review recovery and live evidence

Use disposable nonzero-variance data and separate accountant and manager identities. This gate checks the protected recovery boundary and supervisor authorization as one workflow:

1. Apply POS migration 5 and Authorization migration 7. Close a cash session with nonzero variance.
2. Sign in as an accountant, load the session and history, and confirm both decisions remain unavailable.
3. Sign in as a store manager, submit **Investigate**, refresh, then submit **Approve** with a different reason.
4. In a disposable run, interrupt an additional decision response after the request leaves the client and before local cleanup, then restart the client under the same Windows user. Sign in, load Cash Review, and use **Verify decision**. Confirm one history entry exists for that operation identity.
5. Open the same detail in two clients, commit from one, and confirm the stale second submission returns conflict and refreshes without adding a duplicate.
6. Project a late cash settlement into the closed session. Confirm the financial version advances and review returns to `review_required`, then approve the new financial version. History must span at least two financial versions.
7. Sign out and confirm no pending Cash Review recovery remains. Run:

```powershell
./scripts/verify-pos-cash-review-live-acceptance.ps1 `
  -CashSessionId '<closed-cash-session-uuid>' `
  -ConfirmAccountantReadOnly `
  -ConfirmManagerWorkflow `
  -ConfirmRestartRecovery `
  -ConfirmConcurrencyConflict `
  -ConfirmLateSettlementInvalidation `
  -ConfirmSignedOut
```

The verifier pins local Docker, reads the POS database, and uses the count-only local-state inspector. It requires the target session to have a current approved projection, at least one investigating and two approving decisions, at least three total decisions spanning two financial versions, append-only history protection, SQLite schema 2 integrity, no operational/recovery/outbox state, and no retained token or legacy state file. It writes sanitized boolean/count evidence under `.runstate/pos-cash-review-live/<run-id>/evidence.json`; it does not retain reasons, reviewer identities, authorization decision IDs, or tokens. The guarded local human run passed on 2026-09-15 for cash session `6d370df7-39b9-4b24-82c2-beea11d3d686`, with three decisions spanning two financial versions and sanitized evidence at `.runstate/pos-cash-review-live/bc90ac914e3d4996b9ab08d3546c456b/evidence.json`. The evidence does not attest terminal hardware identity; physical-terminal execution remains a separate release gate.
