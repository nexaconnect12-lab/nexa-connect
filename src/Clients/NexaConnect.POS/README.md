# NexaConnect POS

This project is the Windows WPF POS client scaffold. It opens Keycloak in the system browser, uses Authorization Code + PKCE S256, validates the callback state, redeems the code without a client secret, and stores the resulting token set with the Windows Data Protection API under the current user's profile. The current operational slice signs in, opens and closes a server-side shift through the POS API, persists the active shift identifier locally, and provides a menu-driven order-entry screen that submits to the authenticated Order workflow endpoint.

The operational UI also provides cash-session reconciliation/open/close, terminal enrollment, offline outbox replay, and an Order-owned cashier Paid control for Bangkok cash and manually verified PromptPay. Shift & cash shows the server-calculated opening amount, signed net movements, expected cash, counted-cash variance preview, and movement history. A successful close is authoritative only after the closed summary is read back from the service. If that read fails after the close response, the UI labels its local variance as verification pending and keeps Refresh reconciliation available for that closed session. Terminal & sync lists unresolved operation identity, state, attempts, status, and last attempt without exposing protected payloads. `%LOCALAPPDATA%\NexaConnect\POS\pos-state.db` is the schema-versioned SQLite owner for shift, cash-session, checkout, settlement, and outbox state. Payloads are DPAPI-protected for the current Windows user, the database is permanently bound to organization/branch/store/terminal scope, and WAL plus `synchronous=FULL` protect committed transitions. Cash movement enqueue commits before its first HTTP attempt. `sending` rows return to `queued` after process interruption; HTTP `400`, `403`, and `409` become `rejected`; transient failures return to `queued`; successful acknowledgements become `completed`. The UI provides authenticated retry and no unaudited discard. Cash close remains blocked by unresolved movements.

Keycloak client: `nexaconnect-pos`

`scripts/run-pos-development.ps1` is a shift-only diagnostic launcher for Authorization, Restaurant, and POS. It does not start Catalog or the order workflow and cannot support Refresh menu or checkout. Use the complete checkout launcher described below for the WPF cashier application. A Keycloak `401` during a POS workload token request can indicate an invalid client id, secret, or client configuration; check those values and restart the services after correcting them.

- Public installed client
- Authorization Code flow with mandatory PKCE S256
- Access tokens explicitly include the stable Keycloak `sub` and `nexaconnect-api` audience
- Redirect URI: `nexaconnect-pos://oauth/callback`
- Access tokens must include the `nexaconnect-api` audience. The development realm configures this as a client-level audience mapper so POS tokens remain valid even when the optional client-scope claim is not expanded.
- No embedded client secret
- Tokens stored with Windows data protection or an approved hardware-backed credential store

The custom callback URI is handled by the single-instance app. Windows launches a second process for `nexaconnect-pos://oauth/callback`; that process forwards the bounded callback payload to the primary instance through a current-user-only named pipe and exits. The callback state is validated and accepted only once per sign-in attempt. The installer must register the `nexaconnect-pos` protocol for the deployed executable.

For a local build, register the protocol for the executable with:

```powershell
./scripts/register-pos-protocol.ps1 -ExecutablePath ./src/Clients/NexaConnect.POS/bin/Debug/net10.0-windows/NexaConnect.POS.exe
```

Run locally after Keycloak is available:

```powershell
dotnet run --project src/Clients/NexaConnect.POS/NexaConnect.POS.csproj
```

The development configuration expects Keycloak at `http://localhost:8080/realms/nexa-dev` and the POS API at `http://localhost:5225/`. Do not log callback URIs, authorization codes, access tokens, or refresh tokens.

Configure `Pos:RestaurantId`, `Pos:OrganizationId`, `Pos:BranchId`, `Pos:StoreId`, `Pos:TerminalId`, `Pos:Currency=THB`, and `Pos:PaymentMethod` (`cash_manual` or `promptpay_manual`), plus `Services:OrderApi`, before placing manual-tender orders. For PromptPay, set `Pos:PromptPayQrImagePath` to a deployment-controlled local restaurant QR image; the Paid action fails closed while the image is absent. PromptPay also requires explicit receipt verification and a reference. Cash requires an open THB cash session. The client sends tenant context to Order, prevents another order while settlement is pending, and retains the settlement identity and exact tender fields in SQLite before network I/O. A timeout or server failure changes the action to Verify payment and replays the identical command. Broader order, shift, device, menu, and authorization synchronization remains future work.

Pending settlement recovery is a protected `pending-settlement` row in `pos-state.db` and can be decrypted only by the same Windows user. Integrity failure, unreadable payloads, a newer schema, scope mismatch, or simultaneous legacy and SQLite state fails startup closed: preserve the complete POS directory, reconcile the Order/service audit, and do not collect payment again. On first safe startup, `state.json`, `cash-session.json`, `pending-checkout.bin`, `pending-settlement.bin`, and `outbox.json` migrate in one SQLite transaction and are deleted only after commit.

Run `powershell.exe -NoProfile -File scripts/test-pos-paid-workflow.ps1 -ConfirmDisposableInfrastructure -ConfirmDestructiveRollback` from a normal interactive Windows user session after injecting the same three non-production PostgreSQL/RabbitMQ settings required by the POS settlement runner. It builds into a run-specific directory so an active development stack cannot lock its test assemblies. It requires 14/14 current-user protected SQLite/recovery cases and 3/3 live PostgreSQL/RabbitMQ projection/migration cases, then writes sanitized evidence under `.runstate/pos-paid-workflow/<run-id>`. A skipped DPAPI test fails the evidence gate. This matrix does not drive WPF controls and does not prove live OIDC; those remain explicit manual/joined gates.

The upgraded matrix passed 17/17 on 2026-09-10. Sanitized evidence is retained under `.runstate/pos-paid-workflow/9ec72791806540d28dd2947a55666801`; it records `interactiveWpfVerified=false` and `liveOidcVerified=false` because joined WPF/OIDC remains a separate gate.

Sign out from the POS window to clear the current access and refresh tokens from memory and Windows-protected storage. When a shift, cash session, checkout, or settlement is active, Sign out remains clickable and explains the exact recovery step while preserving the work. Close shift behaves the same way when cash or payment work must be resolved first. After detecting an expired access token while idle, the client enables Sign in again and retains the saved operational state; authenticate again before closing or continuing it.

Online Keycloak authentication enrolls the employee and device. Offline unlock, cached permissions, expiration, manager overrides, and audit records remain NexaConnect responsibilities and must not be represented as indefinitely valid Keycloak tokens. The POS must clear online credentials when a device is revoked or deregistered and must never treat a locally entered PIN as a Keycloak password.

## Cashier workspace

Checkout now provides touch-sized product tiles, menu-name search, preparation-station filters, quantity controls, and explicit currency totals. Payment has its own view, while Shift & cash and Terminal & sync separate operational management from selling. The header shows abbreviated configured branch/terminal identifiers plus session state; employee/branch display-name resolution and continuous connectivity monitoring remain future work.

For `cash_manual`, Send order remains available once an authenticated shift and cart are present. If no cash session is open, selecting it moves the cashier to **Shift & cash** and explains that a THB cash session is required; no order or recovery record is created until that prerequisite is satisfied. This preserves the cash-control rule without presenting an unexplained disabled action.

A shift can own only one cash session. The client displays the POS API's bounded `409` recovery title: restore or reconcile an existing open session, or close the current shift after its cash session has been closed and open a new shift before opening cash again. Reconciliation is scoped by the authenticated subject and configured terminal. The cashier reviews the latest server concurrency version before close; if movements changed, the client refreshes the figures and requires another close action. PostgreSQL commits expected, counted, variance, closure, and the version advance together. A successful movement, open, replay, or close remains successful if its follow-up summary refresh fails, with guidance to refresh before another financial action.

Pending settlement resumes in Payment after restart. Attempted tender fields are locked and the stored method overrides the configured default. Recovery is written as uncertain before the HTTP attempt; failed persistence prevents sending, and cleanup happens only after success. Verify payment replays the same command without asking the cashier to collect again. Active operations disable workspace actions, and cash/shift closure is blocked by pending settlement.

See [cashier acceptance and remaining limits](../../../docs/Deployment/POS-Cashier-Acceptance.md) for the test commands, live acceptance procedure, supported window size, and the distinction between protected client replay and incomplete server workflow recovery. Joined SQLite-backed local OIDC/cash checkout passed on 2026-09-10. The current reconciliation is one terminal cash-session view; shift history, supervisor sign-off, named cashier context, exports, and physical-terminal acceptance remain open.

Browser sign-in offers Cancel sign-in and expires after three minutes if no callback completes. Cancellation restores the controls and retains pending cashier work. A late callback from a cancelled attempt cannot install credentials. The footer and button tooltips identify the next required step for active checkout, payment, cash movement, cash session, or shift state.

## Durable checkout integration

The POS now uses `Services:CatalogApi` for menu reads, with organization/application context and generated correlation IDs. `Services:PosApi` handles shifts/cash/terminals and `Services:OrderApi` handles placement and settlement. The default local ports are 5268, 5225, and 5230 respectively. Configuration validation rejects missing/unsafe URLs, empty scope identifiers, and non-THB/non-manual checkout. Loopback HTTP is development-only; remote endpoints require HTTPS. Service authorization still validates actual branch/terminal ownership.

Before order submission, a DPAPI-protected `pending-checkout` row is committed to SQLite. It retains the original order/idempotency identity, settlement key, organization, restaurant, branch, store, terminal, currency, method, and product quantities. The original row survives the placement response until settlement succeeds, when checkout and settlement rows are cleared in one transaction. Restart before settlement persistence returns to **Verify original order**. Cart edits, sign-out, and cash/shift closure are blocked while checkout is unresolved. Identical replay can recover a completed placement; intermediate states and unrecognized conflicts remain for operator reconciliation; matching terminal Rejected or PaymentFailed results release the checkout lock. This is not a server-side workflow resumer or offline ordering implementation.

A recovered database and checkout must match terminal scope, currency, and checkout mode. Restore the original configuration before retrying; never delete or edit SQLite rows to bypass recovery. Corrupt or conflicting state fails closed.

Use `./scripts/run-checkout-development.ps1 -ValidateOnly` to validate configuration and required secret names, then `./scripts/run-checkout-development.ps1` to build and supervise the nine local service hosts. Add `-StartInfrastructure` to start the existing Compose postgres/redis/rabbitmq/keycloak services. Provision service-owned migrations, a THB branch/menu, cashier permissions and terminal before live acceptance. The script refuses occupied ports, uses separate build/log directories, waits on bounded loopback listener probes, and stops only its own children on exit. It does not stop shared infrastructure.

Placement conflicts that contain a matching terminal `Rejected` or `PaymentFailed` Order result are definitive: the client clears only the protected checkout lock and keeps any in-memory cart for cashier review before a new order. After a client restart, the saved checkout protects the original product IDs and quantities for replay but does not reconstruct visible cart rows. Other conflicts, intermediate states, transport failures, and malformed responses retain recovery and continue to prohibit replacement orders.

If Refresh menu returns `403` after changing an existing Keycloak realm, verify that `nexaconnect-pos` has the `oidc-sub-mapper`, provision Platform Directory membership and the branch-scoped cashier role for the user's stable Keycloak `sub`, then obtain a new POS token by signing in again. Running a second checkout launcher will only cause port conflicts.

The launcher needs `ConnectionStrings__<Service>` for PlatformDirectory, Authorization, Restaurant, Catalog, Inventory, Kitchen, Order, POS and Reporting (matching `NEXACONNECT_<SERVICE>_IMPORT_DB` aliases are accepted); workload secrets `NEXACONNECT_<SERVICE>_SERVICE_CLIENT_SECRET` for Catalog, Inventory, Kitchen, Order and POS; and `NEXACONNECT_CHECKOUT_RABBITMQ`. Missing values are reported by name only. These may be supplied in local `.env`. TCP listener probes do not certify HTTP handling, database/migration state, seed data, or permissions.

Checkout transport events use the shared observability foundation with service name `nexaconnect-pos-client`. The client emits operation code, status and generated correlation only; no credentials, tender data, HTTP bodies or arbitrary headers. Search JSON logs for `Checkout boundary rejected` / `Checkout transport failed`, then follow `CorrelationId` in `nexaconnect-order` or `nexaconnect-catalog` logs. Console visibility requires a console-attached host; WPF does not install a file sink or enable OTLP by default.

After a live cash checkout, copy the Paid Order ID shown in the footer, close cash, close the shift, and sign out. Run `scripts/verify-pos-cashier-live-acceptance.ps1` with that Order ID and the three explicit interactive confirmation switches. The verifier pins this repository's Compose stack and permits only local Docker Desktop Windows named pipes. It requires one exact-total THB cash settlement, an equal POS cash projection and cash movement linked through one closed cash session and shift, and absence of local token/shift/cash/checkout/settlement files before writing sanitized evidence under `.runstate/pos-cashier-live/`.
