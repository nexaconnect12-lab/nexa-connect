# NexaConnect POS

This project is the Windows WPF POS client scaffold. It opens Keycloak in the system browser, uses Authorization Code + PKCE S256, validates the callback state, redeems the code without a client secret, and stores the resulting token set with the Windows Data Protection API under the current user's profile. The current operational slice signs in, opens and closes a server-side shift through the POS API, persists the active shift identifier locally, and provides a menu-driven order-entry screen that submits to the authenticated Order workflow endpoint.

The operational UI also provides cash-session open/close, terminal enrollment, and offline outbox replay controls. A cash movement and its terminal id are written to the local outbox before its first HTTP attempt; that attempt and every replay send the same durable operation and terminal identity to the POS API even if current configuration later changes. HTTP `400`, `403`, and `409` responses are retained as rejected entries rather than retried indefinitely, later pending entries can continue, and an authenticated operator may return rejected entries to the queue after correcting authorization or configuration. The UI provides no unaudited discard action. A cash session cannot close while it has queued, in-flight, or rejected movements. Authentication, connectivity, and server failures remain retryable after their cause is resolved. Legacy queue entries created before terminal identity was persisted fail closed as rejected instead of adopting current configuration. A corrupt JSON queue fails closed instead of being treated as empty; preserve the file for recovery rather than deleting it blindly.

Keycloak client: `nexaconnect-pos`

For a consistent local startup, run `scripts/run-pos-development.ps1` from the repository root. It stops duplicate API processes and starts Authorization, Restaurant, and POS with the Development PostgreSQL settings.

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

Configure `Pos:RestaurantId`, `Pos:OrganizationId`, `Pos:BranchId`, `Pos:StoreId`, `Pos:TerminalId`, `Pos:Currency`, and `Pos:PaymentMethod`, plus `Services:OrderApi`, in `appsettings.json` (or deployment configuration) before opening a shift or placing orders. Cash-session and terminal-enrollment APIs now exist on the POS service. Cash-movement replay wiring is implemented, but the scaffold currently persists its queue as an atomically replaced JSON file. Production still requires the documented SQLite outbox/checkpoint store, corruption recovery, device implementations, and replay wiring for order, shift, and device operations. The separate local state file is only the active-shift recovery reference.

Sign out from the POS window to clear the current access and refresh tokens from memory and Windows-protected storage. Do not sign out while a shift is active.

Online Keycloak authentication enrolls the employee and device. Offline unlock, cached permissions, expiration, manager overrides, and audit records remain NexaConnect responsibilities and must not be represented as indefinitely valid Keycloak tokens. The POS must clear online credentials when a device is revoked or deregistered and must never treat a locally entered PIN as a Keycloak password.
