# Omise POS card-token handoff

The WPF POS supports an opt-in local Omise test checkout. The operator generates a single-use success-card token with the existing [local HTTPS helper](../../src/Tools/NexaConnect.OmiseTokenHelper/README.md) and pastes it into a masked Checkout field. This is a Development/Testing operator tool, not production card collection. PAN/CVV go directly from approved browser tokenization to Omise; NexaConnect accepts neither card details nor provider keys in the token field.

## Enable the local checkout

1. Reconcile all earlier POS checkout/settlement recovery before changing terminal configuration. Do not change the terminal, tenant or payment method of an unresolved original checkout.
2. In `src/Clients/NexaConnect.POS/appsettings.json`, set `Pos:PaymentMethod` to `card_omise_test` and `Pos:EnableOmiseTestCheckout` to `true`. Keep `Pos:Currency=THB` and the existing valid organization/restaurant/branch/store/terminal identifiers. All service APIs must be loopback. Leave defaults unchanged for cash/manual PromptPay.
3. Provision the existing Order, Payment and dependent service migrations, seed data and permissions. No new migration is required. The full checkout launcher additionally requires `ConnectionStrings__Payment` (or `NEXACONNECT_PAYMENT_IMPORT_DB`), `NEXACONNECT_PAYMENT_SERVICE_CLIENT_SECRET`, the existing checkout database/workload settings and RabbitMQ connection.
4. Inject the Omise test secret in the launcher terminal without echoing it:

   ```powershell
   $masked = Read-Host 'Omise test secret' -AsSecureString
   $env:NEXACONNECT_OMISE_TEST_SECRET_KEY = [Net.NetworkCredential]::new('', $masked).Password
   $masked.Dispose()
   pwsh -NoProfile -File scripts/run-checkout-development.ps1 -EnableOmiseTestCheckout -ValidateOnly
   pwsh -NoProfile -File scripts/run-checkout-development.ps1 -EnableOmiseTestCheckout
   ```

   Stop the earlier development launcher before starting this stack. The opt-in launcher also starts Payment on port 5272; it enables `CardCheckout__EnableOmiseTestCheckout` only for Order and selects Omise with the test secret only for Payment. It strips `NEXACONNECT_OMISE_*` from child environments and restores the parent environment when stopped. Do not configure a global reusable card token. Payment recovery retains its normal leases, timeout guard and status-only reconciliation.
5. Start the POS in another terminal, sign in, open the shift, refresh the menu and add a test cart. Generate a fresh token with your test public key in the HTTPS helper, paste it into **Fresh Omise test token**, and send the order once. The UI clears the masked field after an attempt and at sign-out/session lock. It does not save the token in SQLite, PendingCheckout, settlement or outbox state. Card orders cannot use the manual Paid action.
6. A confirmed provider capture completes the original Order as Paid. Compare amount and identity in the test dashboard and verify the original Order. If the result is uncertain, leave the token field blank and use **Verify original order**. Do not collect again or create another Order.
7. If authorization never started (for example a process stopped before the token reached Payment), the original Order remains PaymentPending with a pending Payment intent and no authorization-start record. **Verify original order** returns `cardTokenRequired=true` only when the state-aware adapter confirms this pending state. The POS then enables the masked field and shows that the original Order needs a token. Generate a new unused token and explicitly use **Authorize original test card order**. After restart, session lock or an uncertain attempt, the field remains disabled until another validated verification response requests a token. The flag is an action hint, not financial confirmation; the server rechecks state and leases at submission. It never uses a new token to replace an already authorizing, unknown, captured or terminal intent. Already-authorized state resumes capture without a token. Uncertain/exhausted provider operations remain owned by reconciliation/review.

For independently hosted Order, enable `CardCheckout:EnableOmiseTestCheckout=true` only in Development/Testing, select Omise in Payment and configure the existing authenticated Order-to-Payment connection. Outside these environments startup rejects the option. Client-side enablement alone cannot enable the server capability.

## Recovery and financial boundaries

The optional workflow `cardToken` body is transient and bounded by a 64 KiB placement request limit plus strict test-token syntax. Authorization and tenant checks still run before Application behavior. The Application command excludes tokens from JSON serialization and its string representation; request string representations are redacted. The aggregate, stored workflow context and integration events never contain the token. Do not enable request-body logging or include the command/request in unrestricted logs.

The test method maps to Payment's existing `card` intent using stable `order:{orderId}` identity. New placement can omit a token: recovery creates/reloads that intent, does not send authorization, and binds the original Order as awaiting operator action. Fresh-token submission reuses the original placement identity and verifies organization, branch, Order ID, currency, method and product quantities. It uses stored prices and repeats neither Inventory reservation nor Kitchen creation. Payment's durable lease fences authorization/capture starts. Foreground Order writes retain existing claim fencing and atomic event enqueue; card completion has a stable event ID so concurrent completion retries cannot enqueue multiple completion events. Order's Payment HTTP client has no automatic transport retries; ambiguous command responses trigger an authoritative state read. GenericHttp and Disabled reject card-token input while pending.

Managed token strings may remain in memory until garbage collection; clearing the UI is not a memory-zeroization guarantee. Clipboard clearing remains an operator action. Do not persist, publish, log, export or send tokens in URLs.

## Guarded pre-authorization interruption acceptance

The earlier [four-scenario hosted gate](../Architecture/Evidence/Omise-Hosted-Recovery-Acceptance.md) remains passed and historical. The new optional fifth case is implemented but credentialed execution and interactive POS acceptance remain pending.

Prepare five distinct fresh unused tokens in the same test account. Follow the masked injection in the [hosted guide](Omise-Hosted-Recovery-Acceptance.md), adding `NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN`. Then run once:

```powershell
pwsh -NoProfile -File scripts/test-order-provider-recovery-live.ps1 `
    -Adapter Omise -IncludeCardTokenHandoff -OmiseAmount 50.00 `
    -ConfirmDisposableInfrastructure -ConfirmProcessTermination -ConfirmSandboxTransactions
```

This opt-in matrix creates five test authorizations, captures three and reverses two. The fifth fixture stops the acceptance process before authorization, starts real Order/Payment hosts, requires recovery to retain one pending intent without any authorization start, and supplies a fresh token through the real Order placement and Payment authorization HTTP routes. It then verifies Paid/captured, stable identity, one durable authorization/capture start, transactional-outbox persistence, Payment restart and persistent publication. Its completion uses direct state-changing responses; it does not claim an Order reconciliation inbox event for an operation that never became uncertain. The other four scenarios still verify their reconciliation inboxes. Authentication uses the harness workload fixture, not live cashier OIDC.

Require `matrixPassed`, `cleanupPassed`, `cardTokenHandoffVerified` and `intentCreatedBeforeAuthorizationVerified` true; `scenarioCount=5`. The existing four-scenario default still excludes this boundary. Failed financial stages must not be retried with consumed tokens: inspect test-account charges, resolve abandoned authorizations separately and prepare new tokens for an independent run. Cleanup removes only generated local infrastructure, never remote charges.

The fifth fixture begins at a durable pending intent; earlier Submitted/InventoryReserved recovery is covered separately by the existing Order workflow acceptance. No production card collection, real cashier UI/OIDC pass, 3DS, PromptPay, refunds, settlement or production onboarding is certified by local controlled-response tests.

## Observability and verification

Use `nexaconnect-pos-client`, `nexaconnect-order` and `nexaconnect-payment` shared structured telemetry with the validated correlation ID. Query `{service_name="nexaconnect-order"}` or `{service_name="nexaconnect-payment"}` in Loki; POS checkout boundary events retain only operation, status and correlation. Never add token/key/body/proof/payment amount to telemetry. See [ADR-009](../Architecture/Decisions/ADR-009-ephemeral-test-card-token-handoff.md).

Local verification passed 169 focused unit cases, including transient POS transport, same-Order resume, immutable scope/content rejection, tokenless recovery, strict key/whitespace rejection, response-action validation and shared foreground/reconciliation completion identity. Fourteen HTTP/discovery integration cases passed. Hosted preflight passed ten rejection cases; isolated checkout-launcher preflight passed seven configuration cases without starting hosts or making provider requests. POS, Order and integration builds were clean. Credentialed five-scenario and interactive POS/OIDC acceptance remain pending.

The complete five-scenario GenericHttp local hosted regression also passed with five provider-boundary and five Payment-host interruptions, confirmed outbox restart/delivery and cleanup. Retained summaries are `.runstate/order-provider-recovery-live/5ad7f3b931834f49bcc15ecd8cae7086/summary.json` and `.runstate/order-provider-recovery-local/98c07b32600b4a1894e2fd9bff30ec1d/summary.json`. This simulator evidence does not certify the new credentialed Omise token-handoff case. No Omise financial commands were issued during implementation.
