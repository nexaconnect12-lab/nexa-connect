# NexaConnect POS

QR8 Orders refreshes the current page every 15 seconds while selected, signed in and active. Deactivation clears rows; collection guidance explains blockers without granting authority. See [QR8](../../../docs/Deployment/QR8-Usability.md).

**Orders** automatically loads current-branch history with all/QR/POS source and open/paid
filters, refresh and 25-row continuation. A cashier selects an eligible original QR row
and **Load selected QR order** reuses preview, persisted custody and Payment without typing
a UUID. Reading history needs `order.read`; collection retains live payment permission,
shift/admission and empty-cart/recovery guards. Session-local rows/cursors clear on
lock/sign-out and never reach the customer display. See
[cashier steps and checks](../../../docs/Deployment/POS-Order-List.md) and
[API/telemetry](../../../docs/API/Branch-Order-List.md). Matching Order/POS builds are
required; this adds no SQLite/backend schema or financial command.

QR7 prepares a fresh isolated test-stack QR profile and guest phone edge; Windows POS remains on the matching provisioned loopback scope and original-cashier DPAPI recovery. No native checkout, financial authority or customer-display behavior changes. Actual POS/OIDC/lock/resume/dual-screen/PDF observations stay pending in the operator record. See [QR7](../../../docs/Deployment/QR7-Pilot.md).

QR5 adds **QR collection**: enter the guest collection reference/original Order UUID, check original table/items/pricing, and collect through existing cash/manual PromptPay Payment controls. Read-only preview rejects invalid references before retaining work; claim persists original Order/operation/cashier/shift in existing DPAPI schema2 recovery before network I/O. QR recovery cannot invoke quote/place; original-cashier lock/resume and settlement replay retain identity. No table-wide combined bill or custody reassignment. Requires matching Order22/POS service and enabled QrCollection. See [contract](../../../docs/API/QR-Collection.md) and [setup/verification](../../../docs/Deployment/QR5-Collection.md).

Phase 5 optionally uses `Services:MediaApi` for approved branch staff/customer screensavers.
Active authenticated sessions refresh once a minute; lock displays separate role playlists
beside staff Resume/Authenticate, with no customer authentication/transaction content.
Validated PNGs/public text have a five-minute maximum lease in a bounded DPAPI cache; invalid,
expired or removed content becomes neutral branding. Defaults remain disabled/neutral until
the Media endpoint is configured. Media never renews operator activity or submits business
commands. See [settings, cache and acceptance](../../../docs/Deployment/Next-Iteration-Phase-5.md).

Phase 4 adds a read-only customer window using the same active transaction. In **Terminal & sync**
an authenticated operator selects distinct **Staff display**/**Customer display** roles on an
extended desktop; startup defaults to one staff screen. Display/DPI changes or unsafe movement
close customer output and mask/lock staff content until restart on the private staff screen.
Customer input cannot authenticate or issue commands. Lock masks details and terminal outcomes
clear them; receipt/history/refund lookups never populate the customer window. Manual Paid
responses must match the original order/tender/amount/currency before local recovery clears.
See [setup, verification and device checklist](../../../docs/Deployment/Next-Iteration-Phase-4.md),
[presentation responsibilities](Presentation/README.md) and
[ADR-039](../../../docs/Architecture/Decisions/ADR-039-pos-staff-customer-displays.md).

Phase 2 extracts reusable native styles to `Presentation/PosTheme.xaml`, aligns touch/focus
and tab presentation, wraps terminal/session context and bounds the scrolling status area.
The local cart sum is labeled Cart estimate; authoritative pricing and payment/recovery
behavior are unchanged. See [UI foundation renders and verification limits](../../../docs/Deployment/Next-Iteration-Phase-2.md).
The first-draft installer remains unchanged; physical/user acceptance is still pending.

Next-iteration Phase 1 adds a dedicated idle-lock overlay and original-cashier authenticated
resume. Provider UserInfo establishes the subject/authority retained in the DPAPI lock
marker; wrong-user resume is rejected and startup requires authentication. Lock retains
shift/drawer/checkout recovery without signing out operational work. Legacy retained work
without an owner binding requires reconciliation before adoption. See [behavior, upgrade
constraints and verification](../../../docs/Deployment/Next-Iteration-Phase-1.md) and
[ADR-038](../../../docs/Architecture/Decisions/ADR-038-pos-authenticated-resume.md).

Draft packaging now compares typed source identity before/after publication, including a clean checkout's false dirty flag. A failed read-only install preflight leaves activation blocked and preserves recovery; publishing a package does not establish installation or device acceptance. See [Slice 8 evidence](../../../docs/Architecture/Evidence/First-Draft-Slice-8.md).

For final solo testing, use the [Slice 7 acceptance and handover guide](../../../docs/Deployment/First-Draft-UAT.md). New terminal manifests record `sourceInputHash`; packaging rejects source-input or HEAD changes during publication. UAT binds the verified package to an unchanged source candidate and local release receipt. Actual installation, active-release selection, recovery, callback and saved-PDF results remain explicit operator checks.

Use [slice 4 Windows deployment](../../../docs/Deployment/First-Draft-Windows-POS.md) for fresh win-x64 packaging, hashed per-user releases, scope-preserving install/update and Launch/Status. `--validate-install` performs read-only current-user recovery/configuration preflight without protocol registration, authentication or schema migration. Updates never overwrite the separate POS recovery directory. Primary startup registers the public PKCE callback; secondary callbacks cannot redirect registration and release only owned mutexes. Startup names pending original Orders; print cancellation/submission messages preserve Paid and require PDF/output verification. Interactive session and PDF checks are tracked separately from automated evidence.

Slice 2 draft provisioning replaces only the owned generated terminal configuration with stable THB / Asia/Singapore scope IDs. Both retained and published settings survive stack rebuilds; existing local recovery remains untouched. See [setup and scoped logins](../../../docs/Deployment/First-Draft-Provisioning.md).

Primary callback registration follows successful configuration/recovery construction, so
starting a wrong-scope release cannot take over the retained terminal's callback.

Checked-in settings default to THB `cash_manual`, `EnableOmiseTestCheckout=false` and no workstation-specific QR path. The [first-draft launcher](../../../docs/Deployment/First-Draft-Test-Stack.md) publishes separate run-scoped client settings for cash/manual PromptPay without modifying existing protected terminal state. Initial scope UUIDs require slice 2 provisioning before POS use. Singapore timezone belongs to Restaurant branch setup; PDF printing uses the Windows path. Test cards remain a separate opt-in.

The Receipts workspace includes manager-authorized refunds for captured provider payments. Configure `Services:PaymentApi`. The client saves the Payment intent, operation ID, amount, currency, and reason in protected local state before the command; a lost response is verified through the operation lookup and blocks another refund. Completed immutable receipts can be printed or reprinted. This control does not refund manual cash or PromptPay settlements.

The Payment tab can cancel the current manual pre-payment Order with a required printable reason and explicit confirmation. Before send it persists the reason and pending status alongside the DPAPI-protected settlement idempotency UUID, so restart or response loss offers only verification of the same server command. Completed cancellation clears checkout recovery; pending disables payment and review disables both payment and another cancellation. Provider-payment, offline, and post-capture refund cancellation are outside this control. See the [Order cancellation contract](../../../docs/API/Order-Cancellation.md).

The Receipts tab automatically previews a newly Paid Order and retrieves prior receipts by Order ID. It validates the server snapshot before rendering, stores only a protected last-Order reference, clears receipt contents on session lock/sign-out, and reloads with live `order.read` authorization before every standard Windows print/reprint. Receipt or printer failure never changes Paid; keep the Order ID and retry retrieval without collecting again. Device-specific printers and fiscal tax invoices remain planned. See [paid-order receipt contract](../../../docs/API/Paid-Order-Receipts.md).

New orders require a server pricing preview and explicit bill confirmation. Confirmed fingerprints persist before send; pricing changes retain the original checkout identity and require another confirmation. Accepted breakdowns persist with pending settlement and are displayed after restart. This requires the pricing-capable Order API and migration 8; hardware/live OIDC acceptance remains separate. See [pricing contract and rollout](../../../docs/API/Order-Pricing.md).

The optional signed Omise webhook worker is Payment-owned and requires Payment migration 8 plus explicit launcher enablement. POS never receives the webhook/API secret or callback body. Webhook notifications supplement existing polling and can reconcile uncertain server payment while the original checkout remains locked; they do not certify cashier UI interactions or trigger another card collection. See [test webhook enablement](../../../docs/Deployment/Omise-Webhooks.md).

A read-only POS Omise acceptance verifier now checks one Paid test-card Order, one amount/ownership-matched captured intent with single authorization/capture starts, an operator-supplied closed shift and cleared local credentials/recovery. UI/OIDC steps require explicit operator confirmation; the verifier does not certify remote charge status or process interruption. [Local POS Omise verification passed on 2026-09-18](../../../docs/Architecture/Evidence/Omise-POS-Card-Acceptance.md) with operator-attested UI/OIDC/sign-out; those interactions are not independently inspected. See the [card-checkout verification procedure](../../../docs/Deployment/Omise-POS-Card-Token-Handoff.md).


Opt-in Development-only `card_omise_test` checkout now forwards a masked, transient test token from POS to Order and Payment. Pending intents without a token await a fresh operator token; retries use the original Order identity and contents. For an original pending checkout, POS enables token input only after Verify original order returns validated `cardTokenRequired=true`; that permission is memory-only and cleared on each attempt/session lock. Tokens never enter SQLite/outbox or durable workflow state. The opt-in five-scenario hosted Omise gate passed on 2026-09-18, including pre-authorization/token handoff. Local POS Omise verification passed on 2026-09-18 with operator-attested UI/OIDC/sign-out; the verifier does not independently inspect those interactions. Production browser card collection remains planned. See the [configuration and acceptance guide](../../../docs/Deployment/Omise-POS-Card-Token-Handoff.md).

This project is the Windows WPF POS client scaffold. It opens Keycloak in the system browser, uses Authorization Code + PKCE S256, validates the callback state, redeems the code without a client secret, and stores the resulting token set with the Windows Data Protection API under the current user's profile. While the operator is active, it renews the short-lived access token with the rotated refresh token. Five minutes without key, click, or touch input locks the workspace, and a ten-hour absolute boundary clears online credentials; both require fresh interactive Keycloak authentication while preserving shift, cash, order, outbox, and review recovery. The current operational slice opens and closes a server-side shift through the POS API, persists the active shift identifier locally, and provides a menu-driven order-entry screen that submits to the authenticated Order workflow endpoint.

The operational UI also provides cash-session reconciliation/open/close, terminal enrollment, offline outbox replay, and an Order-owned cashier Paid control for Bangkok cash and manually verified PromptPay. Shift & cash shows the server-calculated opening amount, signed net movements, expected cash, counted-cash variance preview, and movement history. A successful close is authoritative only after the closed summary is read back from the service. If that read fails after the close response, the UI labels its local variance as verification pending and keeps Refresh reconciliation available for that closed session. Terminal & sync lists unresolved operation identity, state, attempts, status, and last attempt without exposing protected payloads. `%LOCALAPPDATA%\NexaConnect\POS\pos-state.db` schema 2 owns shift, cash-session, checkout, settlement, pending cash-review, and outbox state. Payloads are DPAPI-protected for the current Windows user, the database is permanently bound to organization/branch/store/terminal scope, and WAL plus `synchronous=FULL` protect committed transitions. Cash movement enqueue commits before its first HTTP attempt. `sending` rows return to `queued` after process interruption; HTTP `400`, `403`, and `409` become `rejected`; transient failures return to `queued`; successful acknowledgements become `completed`. The UI provides authenticated retry and no unaudited discard. Cash close remains blocked by unresolved movements.

The **Cash review** tab reads closed sessions for the configured store over a selectable range (up to 31 days), pages older results, displays financial movements and immutable decisions, and lets an authorized resolver mark a nonzero variance investigating or approved with an explicit reason and confirmation. Before transmission, SQLite schema 2 stores the complete scope, decision, reason, financial/review versions, and idempotency UUID as a DPAPI-protected `pending-cash-review` row. Restart opens Cash review recovery; loading history either confirms the UUID and clears recovery or restores the original locked values for **Verify decision**. Transport uncertainty preserves the row. A validation or concurrency rejection (`400`/`409`) clears the rejected attempt; `403`/`404` retains it because access or scope may have changed after the original uncertain request. A successful response is cleared only after client validation finds its identity in immutable history. Sign-out and conflicting decisions remain blocked while recovery exists. A late cash settlement transaction recalculates both stored expected closing cash and variance from authoritative movements, advances the financial version, and can return an approved session to review required. Review reads also derive both expected cash and variance from movements so sessions affected before this correction remain internally consistent. Read-only users can inspect history but cannot submit decisions.

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

The development configuration expects Keycloak at `http://localhost:8080/realms/nexa-dev` and the POS API at `http://localhost:5225/`. `Session:IdleTimeoutMinutes` defaults to 5 (allowed 1-60), `Session:AbsoluteTimeoutHours` defaults to 10 (allowed 1-24), and `Session:TokenRefreshLeadSeconds` defaults to 60 (allowed 15-240). Do not log callback URIs, authorization codes, access tokens, or refresh tokens.

Configure `Pos:RestaurantId`, `Pos:OrganizationId`, `Pos:BranchId`, `Pos:StoreId`, `Pos:TerminalId`, `Pos:Currency=THB`, and `Pos:PaymentMethod` (`cash_manual` or `promptpay_manual`), plus `Services:OrderApi`, before placing manual-tender orders. For PromptPay, set `Pos:PromptPayQrImagePath` to a deployment-controlled local restaurant QR image; the Paid action fails closed while the image is absent. PromptPay also requires explicit receipt verification and a reference. Cash requires an open THB cash session. The client sends tenant context to Order, prevents another order while settlement is pending, and retains the settlement identity and exact tender fields in SQLite before network I/O. A timeout or server failure changes the action to Verify payment and replays the identical command. Broader order, shift, device, menu, and authorization synchronization remains future work.

Pending settlement recovery is a protected `pending-settlement` row in `pos-state.db` and can be decrypted only by the same Windows user. Integrity failure, unreadable payloads, a newer schema, scope mismatch, or simultaneous legacy and SQLite state fails startup closed: preserve the complete POS directory, reconcile the Order/service audit, and do not collect payment again. On first safe startup, `state.json`, `cash-session.json`, `pending-checkout.bin`, `pending-settlement.bin`, and `outbox.json` migrate in one SQLite transaction and are deleted only after commit.

Pending Cash Review recovery is bound to organization, restaurant, branch, store, and terminal configuration. A mismatch, corrupt payload, or unsupported newer local schema fails startup closed and preserves the database. The read-only inspector reports only `pendingCashReviewCount`; it never decrypts or returns the decision reason, reviewer identity, or request payload.

Run `powershell.exe -NoProfile -File scripts/test-pos-paid-workflow.ps1 -ConfirmDisposableInfrastructure -ConfirmDestructiveRollback` from a normal interactive Windows user session after injecting the same three non-production PostgreSQL/RabbitMQ settings required by the POS settlement runner. It builds into a run-specific directory so an active development stack cannot lock its test assemblies. It requires 17/17 current-user protected SQLite/recovery cases and 3/3 live PostgreSQL/RabbitMQ projection/migration cases, then writes sanitized evidence under `.runstate/pos-paid-workflow/<run-id>`. A skipped DPAPI test fails the evidence gate. This matrix does not drive WPF controls and does not prove live OIDC; those remain explicit manual/joined gates.

The upgraded matrix passed 17/17 on 2026-09-10. Sanitized evidence is retained under `.runstate/pos-paid-workflow/9ec72791806540d28dd2947a55666801`; it records `interactiveWpfVerified=false` and `liveOidcVerified=false` because joined WPF/OIDC remains a separate gate.

Sign out from the POS window to clear the current access and refresh tokens from memory and Windows-protected storage. When a shift, cash session, checkout, or settlement is active, Sign out remains clickable and explains the exact recovery step while preserving the work. Close shift behaves the same way when cash or payment work must be resolved first. Active sessions refresh one minute before access-token expiry and persist a rotated refresh token before using it. A rejected refresh clears credentials and requires sign-in; a transient identity failure retries after 15 seconds while the current access token remains valid and locks when it expires. Idle and absolute locks replace online credentials with a nonsecret persisted lock marker, use `prompt=login`, disable authenticated operations, and retain all operational recovery across restart. Tokens saved by an older client without an absolute-session boundary are replaced by the same lock marker on upgrade.

Online Keycloak authentication enrolls the employee and device. Offline unlock, cached permissions, expiration, manager overrides, and audit records remain NexaConnect responsibilities and must not be represented as indefinitely valid Keycloak tokens. The POS must clear online credentials when a device is revoked or deregistered and must never treat a locally entered PIN as a Keycloak password.

## Cashier workspace

Checkout now provides touch-sized product tiles, menu-name search, preparation-station filters, quantity controls, and explicit currency totals. Payment has its own view, while Shift & cash and Terminal & sync separate operational management from selling. The header shows abbreviated configured branch/terminal identifiers plus session state; employee/branch display-name resolution and continuous connectivity monitoring remain future work.

For `cash_manual`, Send order remains available once an authenticated shift and cart are present. If no cash session is open, selecting it moves the cashier to **Shift & cash** and explains that a THB cash session is required; no order or recovery record is created until that prerequisite is satisfied. This preserves the cash-control rule without presenting an unexplained disabled action.

A shift can own only one cash session. The client displays the POS API's bounded `409` recovery title: restore or reconcile an existing open session, or close the current shift after its cash session has been closed and open a new shift before opening cash again. Suggested shift numbers include local seconds and a short random suffix, and a successful close immediately prepares the next number so a closed identifier is not reused after a quick restart or within the same minute. Reconciliation is scoped by the authenticated subject and configured terminal. The cashier reviews the latest server concurrency version before close; if movements changed, the client refreshes the figures and requires another close action. PostgreSQL commits expected, counted, variance, closure, and the version advance together. A successful movement, open, replay, or close remains successful if its follow-up summary refresh fails, with guidance to refresh before another financial action.

Pending settlement resumes in Payment after restart. Attempted tender fields are locked and the stored method overrides the configured default. Recovery is written as uncertain before the HTTP attempt; failed persistence prevents sending, and cleanup happens only after success. Verify payment replays the same command without asking the cashier to collect again. Active operations disable workspace actions, and cash/shift closure is blocked by pending settlement.

See [cashier acceptance and remaining limits](../../../docs/Deployment/POS-Cashier-Acceptance.md) for the test commands, live acceptance procedures, supported window size, and the distinction between durable retry identity and online execution. Joined SQLite-backed local OIDC/cash checkout passed on 2026-09-10, and guarded local human Cash Review acceptance passed on 2026-09-15. Multi-store portfolio views, exports, reviewer display-name resolution, a Reporting projection, offline authorization/execution, and physical-terminal supervisor/session-policy acceptance remain open.

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

Run `scripts/test-pos-cashier-sale.ps1 -ConfirmDisposableInfrastructure -ConfirmDestructiveRollback` for the repeatable backend gate. It composes 20 protected recovery/projection cases, three receipt/authorization/migration cases, and 15 verifier fixtures/guards in isolated PostgreSQL/RabbitMQ infrastructure and verifies cleanup. Its 38 checks passed locally on 2026-09-29.

After a live cash checkout, exercise receipt preview, failure isolation, and Print / reprint; copy the Paid Order ID, close cash, close the shift, and sign out. Run `scripts/verify-pos-cashier-live-acceptance.ps1` with that Order ID and all six explicit confirmation switches described in the [cashier acceptance guide](../../../docs/Deployment/POS-Cashier-Acceptance.md). The verifier pins this repository's Compose stack and permits only local Docker Desktop Windows named pipes. It requires one exact-total THB cash settlement, a matching immutable receipt, an equal POS cash projection and cash movement linked through one closed cash session and shift, and cleared local operational and credential state before writing sanitized evidence under `.runstate/pos-cashier-live/`.
## Dedicated-server pilot preparation

The [Phase 6 guide](../../../docs/Deployment/Next-Iteration-Phase-6.md) prepares hosted
APIs; it does not package, install or reconfigure this Windows client. For a dedicated
staging profile, set `Identity:Authority` to the exact public realm and `Services:PosApi`,
`OrderApi`, `CatalogApi`, optional `PaymentApi` and `MediaApi` to trusted HTTPS edge origins.
Retain the public `nexaconnect-pos` client/callback, provisioned organization/branch/store/
terminal identifiers and manual THB tender mode. No confidential/workload credential
belongs here. Verify remote login, lock/resume, checkout, Kitchen and PDF printing on
the intended Windows account/device. Preserve its DPAPI recovery; never copy it to the
server or another Windows account. Target hosting and remote-device acceptance remain pending.
## Phase 7 hosted installer preparation

The [per-user EXE guide](../../../docs/Deployment/Next-Iteration-Phase-7.md) adds public
profile onboarding and `--validate-package <profile> <previous-or-dash> <managed-directory>`.
That mode validates without opening UI, authenticating, replaying commands or migrating
SQLite. Compatible existing schema/scope and saved cashier authority are required; the
hosted profile cannot silently replace the original HTTP draft realm or terminal custody.
The new maintenance mutex masks normal startup while setup runs in the same logon session;
existing primary-instance callbacks still use the original current-user pipe/state checks.
Use the actual Windows account, never elevate or transplant DPAPI state. Uninstall retains
recovery/display/gallery state and public config. The loopback draft installer remains a
separate workflow, with explicit local QR storage selection documented below. Trusted
signing, VM interruption and real device/browser checks remain pending.

## Local QR pilot in an existing Windows account

`Pos:UseIsolatedLocalPilotStorage` defaults false. QR first-draft publication explicitly
sets it true for its generated loopback/manual-tender scope, selecting a fixed
`%LOCALAPPDATA%\NexaConnect\POS-Pilots\<scope-hash>` directory for preflight, SQLite,
outbox and protected tokens/lock markers. Original `%LOCALAPPDATA%\NexaConnect\POS`
state remains untouched; existing scope/integrity/operator/pending-work guards still apply
to the selected profile. Remote/hosted configuration cannot enable this option, and an
existing installed release cannot switch storage selection during an update. No arbitrary
path setting or recovery migration is added. See [storage ownership](../../../docs/Database/POS-Local-SQLite.md),
[current-user setup](../../../docs/Deployment/First-Draft-Windows-POS.md) and
[ADR-050](../../../docs/Architecture/Decisions/ADR-050-local-pilot-pos-storage.md).
