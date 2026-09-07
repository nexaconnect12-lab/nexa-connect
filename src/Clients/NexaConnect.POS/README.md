# NexaConnect POS

This project is the Windows WPF POS client scaffold. It opens Keycloak in the system browser, uses Authorization Code + PKCE S256, validates the callback state, redeems the code without a client secret, and stores the resulting token set with the Windows Data Protection API under the current user's profile. The current operational slice signs in, opens and closes a server-side shift through the POS API, persists the active shift identifier locally, and provides a menu-driven order-entry screen that submits to the authenticated Order workflow endpoint.

The operational UI also provides cash-session open/close, terminal enrollment, offline outbox replay, and an Order-owned cashier Paid control for Bangkok cash and manually verified PromptPay. A cash movement and its terminal id are written to the local outbox before its first HTTP attempt; that attempt and every replay send the same durable operation and terminal identity to the POS API even if current configuration later changes. HTTP `400`, `403`, and `409` responses are retained as rejected entries rather than retried indefinitely, later pending entries can continue, and an authenticated operator may return rejected entries to the queue after correcting authorization or configuration. The UI provides no unaudited discard action. A cash session cannot close while it has queued, in-flight, or rejected movements. Authentication, connectivity, and server failures remain retryable after their cause is resolved. Legacy queue entries created before terminal identity was persisted fail closed as rejected instead of adopting current configuration. A corrupt JSON queue fails closed instead of being treated as empty; preserve the file for recovery rather than deleting it blindly.

Keycloak client: `nexaconnect-pos`

For a consistent local startup, run `scripts/run-pos-development.ps1` from the repository root. It stops duplicate API processes and starts Authorization, Restaurant, and POS with the Development PostgreSQL settings. The launcher accepts the conventional `ConnectionStrings__*` and `WorkloadIdentity__ClientSecret` names; when separate aliases are absent, it reuses the already-injected restricted `NEXACONNECT_RESTAURANT_IMPORT_DB`, `NEXACONNECT_POS_IMPORT_DB`, and `NEXACONNECT_POS_SERVICE_CLIENT_SECRET` values in process memory without printing them.

- Public installed client
- Authorization Code flow with mandatory PKCE S256
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

Configure `Pos:RestaurantId`, `Pos:OrganizationId`, `Pos:BranchId`, `Pos:StoreId`, `Pos:TerminalId`, `Pos:Currency=THB`, and `Pos:PaymentMethod` (`cash_manual` or `promptpay_manual`), plus `Services:OrderApi`, before placing manual-tender orders. For PromptPay, set `Pos:PromptPayQrImagePath` to a deployment-controlled local restaurant QR image; the Paid action fails closed while the image is absent. PromptPay also requires explicit receipt verification and a reference. Cash requires an open THB cash session. The client sends tenant context to Order, prevents another order while settlement is pending, and atomically retains the settlement idempotency key and exact tender fields under current-user Windows Data Protection. A timeout or server failure changes the action to Verify payment, which replays the identical command; it never generates a new settlement identity. Cash-session and terminal-enrollment APIs now exist on the POS service. Cash-movement replay wiring is implemented, but the scaffold currently persists its queue as an atomically replaced JSON file. Production still requires the documented SQLite outbox/checkpoint store, corruption recovery, device implementations, and replay wiring for order, shift, and device operations.

Pending settlement recovery is stored at `%LOCALAPPDATA%\NexaConnect\POS\pending-settlement.bin` and can be decrypted only by the same Windows user. If that file is corrupt or cannot be decrypted, startup fails closed: preserve the file, reconcile the Order in the service/audit trail, and do not collect payment again. The default suite covers Order provider bypass and rejection of non-THB manual checkout before side effects. The opt-in Windows matrix covers protected-state restart and corruption; interactive WPF controls, live OIDC, and joined client-to-service replay still require live acceptance.

Run `powershell.exe -NoProfile -File scripts/test-pos-paid-workflow.ps1 -ConfirmDisposableInfrastructure -ConfirmDestructiveRollback` from a normal interactive Windows user session after injecting the same three non-production PostgreSQL/RabbitMQ settings required by the POS settlement runner. It requires 3/3 current-user DPAPI recovery/corruption cases and 3/3 live PostgreSQL/RabbitMQ projection/migration cases, then writes sanitized evidence under `.runstate/pos-paid-workflow/<run-id>`. A skipped DPAPI test fails the evidence gate. This matrix does not drive WPF controls and does not prove live OIDC; those remain explicit manual/joined gates.

The combined matrix passed 6/6 on 2026-09-03 UTC. Sanitized evidence is retained under `.runstate/pos-paid-workflow/9c322b1ace3f422a9e7e6cd37b67658b`; it records `interactiveWpfVerified=false` and `liveOidcVerified=false`.

Sign out from the POS window to clear the current access and refresh tokens from memory and Windows-protected storage. The client blocks sign-out while a shift, cash session, or pending settlement remains active.

Online Keycloak authentication enrolls the employee and device. Offline unlock, cached permissions, expiration, manager overrides, and audit records remain NexaConnect responsibilities and must not be represented as indefinitely valid Keycloak tokens. The POS must clear online credentials when a device is revoked or deregistered and must never treat a locally entered PIN as a Keycloak password.

## Cashier workspace

Checkout now provides touch-sized product tiles, menu-name search, preparation-station filters, quantity controls, and explicit currency totals. Payment has its own view, while Shift & cash and Terminal & sync separate operational management from selling. The header shows abbreviated configured branch/terminal identifiers plus session state; employee/branch display-name resolution and continuous connectivity monitoring remain future work.

Pending settlement resumes in Payment after restart. Attempted tender fields are locked and the stored method overrides the configured default. Recovery is written as uncertain before the HTTP attempt; failed persistence prevents sending, and cleanup happens only after success. Verify payment replays the same command without asking the cashier to collect again. Active operations disable workspace actions, and cash/shift closure is blocked by pending settlement.

See [cashier acceptance and remaining limits](../../../docs/Deployment/POS-Cashier-Acceptance.md) for the test commands, live acceptance procedure, supported window size, and the distinction between settlement replay and the still-incomplete durable order-placement recovery. Interactive OIDC/checkout, full reconciliation reporting, named cashier context, and physical terminal acceptance are not signed off by the UI implementation.

Browser sign-in now offers Cancel sign-in and expires after three minutes if no callback completes. Cancellation restores the controls and retains pending cashier work. A late callback from a cancelled attempt cannot install credentials. When already authenticated with an active shift, cash session, or pending settlement, both Sign in and Sign out can remain disabled intentionally; finish that work before signing out.
