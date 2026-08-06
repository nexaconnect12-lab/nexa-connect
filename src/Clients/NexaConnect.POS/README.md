# NexaConnect POS

This project is the Windows WPF POS client scaffold. It opens Keycloak in the system browser, uses Authorization Code + PKCE S256, validates the callback state, redeems the code without a client secret, and stores the resulting token set with the Windows Data Protection API under the current user's profile. The current operational slice signs in, opens and closes a server-side shift through the POS API, and persists the active shift identifier locally so a terminal restart does not lose the session reference.

Keycloak client: `nexaconnect-pos`

- Public installed client
- Authorization Code flow with mandatory PKCE S256
- Redirect URI: `nexaconnect-pos://oauth/callback`
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

Configure `Pos:BranchId`, `Pos:StoreId`, and `Pos:TerminalId` in `appsettings.json` (or the deployment configuration) before opening a shift. Cash-session and terminal-enrollment APIs now exist on the POS service. The client includes durable local outbox and hardware-adapter interfaces; production device implementations and operation-specific replay wiring remain deployment work. The local state file is only the active-shift recovery reference.

Sign out from the POS window to clear the current access and refresh tokens from memory and Windows-protected storage. Do not sign out while a shift is active.

Online Keycloak authentication enrolls the employee and device. Offline unlock, cached permissions, expiration, manager overrides, and audit records remain NexaConnect responsibilities and must not be represented as indefinitely valid Keycloak tokens. The POS must clear online credentials when a device is revoked or deregistered and must never treat a locally entered PIN as a Keycloak password.
