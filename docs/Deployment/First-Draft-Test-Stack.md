# First-draft local test stack

Optional `-EnableOmiseTestCheckout` on a stopped, build-enabled Start adds sandbox card
capability beside the manual default; defaults remain off. It retains the test credential
in existing current-user DPAPI secrets and publishes only the capability flag to POS.
Enabled profiles inherit it on restart; no key is copied to client settings. See
[setup, key handling and acceptance gates](POS-Mixed-Omise-Sandbox.md).

Optional local image-upload dependencies are now selected explicitly with `-EnableMedia`; base drafts remain unchanged. See [Media activation, lifecycle and real-browser verification](First-Draft-Media.md).

Build-enabled publication also sets POS `Services:MediaApi` from the enabled profile's
validated generated loopback inventory. Base/off profiles keep it empty. Refresh and install
the matching POS package to use approved screensaver galleries; prior installed settings
with an empty Media endpoint do not initialize gallery refresh.

QR7 adds optional `-QrPilotOrigin https://<confirmed-lan-host>:8443` at fresh draft creation only. It enables the explicit local QR profile, pins that origin across rebuild/restart and verifies source/published server inventories before Restart/NoBuild. It cannot convert a retained normal draft. Host/API/identity/database listeners remain loopback; phones use the separate closed [QR7 HTTPS edge](QR7-Pilot.md). Normal QR defaults remain disabled. Runtime Data Protection keys are retained separately from executable inventory.

For final user testing, retain the provisioned environment across Singapore midnight and follow the [Slice 7 UAT/handover guide](First-Draft-UAT.md). Disposable release-gate environments are Reset after verification and cannot serve as the retained user installation.

Slice 6 adds [candidate verification and a quiesced isolated restore drill](First-Draft-Release.md), including owning service and Keycloak databases, copied configuration/key ring and same-account credentials. Clone databases have no network or published ports. Original broker/Redis volumes are retained rather than restored; the original graph resumes. See executed evidence for status; production backup restoration and clean-commit CI remain distinct gates.

For Slice 5, use the [manager guide](First-Draft-Manager.md). The normal draft retains real clocks; historical completed-day checks run in a separate guarded disposable test host, not through a production clock setting. Reporting accepts the scoped customer-user entry role with existing live product authorization. Actual next-day staff acceptance remains pending.

After provisioning, use [Windows terminal packaging/install/update](First-Draft-Windows-POS.md) for Slice 4. It preserves protected terminal recovery separately from application releases. New manual settlement requires the POS admission endpoint; generated `Services:POS` wiring already supplies the address. Interactive WPF/PDF acceptance is recorded separately.

Infrastructure loopback ports are generated up front and retained in run metadata,
then explicitly bound by Compose and checked against actual published listeners.

Startup also validates Order's API audience, exact `azp` and service-account username
claim for manual-tender admission. Existing persisted realms require a reviewed Order
access-token username mapper update and fresh workload token; see [realm lifecycle](../../docker/keycloak/README.md).

Each new base run allocates fifteen distinct ports: eleven application listeners and four
infrastructure bindings (PostgreSQL, Keycloak, RabbitMQ and Redis). The helper supplies
the four `NEXACONNECT_DRAFT_*_PORT` values transiently from retained metadata.

The optional Media profile adds one application listener and two loopback infrastructure
bindings (MinIO and ClamAV), for eighteen distinct retained ports. It starts seven owned
infrastructure containers instead of the base five; the bucket initializer is a separate
one-shot profile. No storage console or scanner port is published to the LAN.
Docker Desktop can reassign an ephemeral host port on container stop/start; retained
bindings keep recovery clients on the original address. Existing retained runs with
populated infrastructure metadata can Stop/Start with the new Compose definition while
preserving owned volumes. A port collision fails safely. Older initial failed runs without
infrastructure ports require reviewed metadata recovery or an explicitly confirmed owned
reset/new run; credentials are never regenerated over retained secrets.

Owned StopService interruption testing is available for a selected service; Status includes missing expected processes as false. Ready is the retained lifecycle marker, not current dependency readiness. Restart preserves generated state and still checks financial consumers; see [joined checkout sequencing](First-Draft-Checkout.md).

Every child host receives explicit `ASPNETCORE_ENVIRONMENT=Development` and
`DOTNET_ENVIRONMENT=Development` through `Start-Process -Environment`, including restart.
An inherited Production environment cannot select the child's hosting mode; this
restriction is local draft tooling, not a production deployment setting.

Inherited application, hosting and telemetry configuration overrides are removed before
launching each child; only the run's explicit settings are supplied. Transient launcher
environment values are restored after each scoped operation. An
originally absent variable is removed rather than restored as an empty string, preserving
typed configuration defaults for later verification commands in the same PowerShell session.

After Ready, run the [slice 2 provisioner](First-Draft-Provisioning.md) before launching POS. It creates stable branch/store/terminal IDs and scoped accounts, retains reference edits/revocation on completed reruns and updates the separate generated POS settings. Joined financial workflows remain later acceptance slices.

This is the slice 1 implementation for the [November plan](../Planning/November-2026-First-Draft.md). It coordinates normal production application hosts with their normal clocks on a local Windows PC: Platform Directory, Authorization, Restaurant, Catalog, Inventory, Kitchen, Order, Payment, POS, Reporting and Customer BFF/portal. No historical clock switch or financial fixture is enabled. The selected test scope is THB cash/manual PromptPay, Singapore business timezone and PDF receipt printing. The separate [slice 2 provisioner](First-Draft-Provisioning.md) supplies branch/menu/product-role records.

## Setup and lifecycle

Use Windows, PowerShell 7.4+, local Docker Desktop, the SDK in `global.json`, Node 22.12+ (22.x) or 24+, and a local .NET HTTPS development certificate. Ensure `dotnet dev-certs https` succeeds; trust that development certificate for interactive browser use. Loopback APIs and Keycloak use Development-only HTTP; Customer BFF uses HTTPS. Do not expose these listeners to a LAN or reuse this configuration outside local development. Non-local deployments retain the existing TLS and protected Data Protection certificate requirements.

From the repository root:

```powershell
./scripts/test-first-draft-guards.ps1
./scripts/first-draft-stack.ps1 -Action Validate -PaymentMethod cash_manual
./scripts/first-draft-stack.ps1 -Action Start -ConfirmTestResources
```

Start prints a generated RunId and the portal URL. Preserve that RunId for subsequent commands; substitute it for `<run-id>` below:

```powershell
./scripts/first-draft-stack.ps1 -Action Status -RunId <run-id>
./scripts/first-draft-stack.ps1 -Action Restart -RunId <run-id> -Service Kitchen
./scripts/first-draft-stack.ps1 -Action Stop -RunId <run-id>
./scripts/first-draft-stack.ps1 -Action Start -RunId <run-id> -NoBuild
```

Start builds owned service/portal/migration/POS artifacts unless `-NoBuild` is selected; that option requires an already completed publication and is only appropriate while those binaries match the source/schema being tested. Start previews and applies current catalog migrations to the generated databases, validates workload API audiences, restricts history grants and enables all financial consumers before dispatchers. Active queue consumers and exact financial bindings must be observed before producer release. POS cash-close publication, settlement recovery and Order workflow recovery remain enabled; provider/card checkout defaults disabled and is enabled only by the explicit retained Omise sandbox profile described above. Webhooks remain disabled.

Status reports process identity/liveness and container state; it is read-only and does not repair anything. Stop verifies each recorded PID/start time/executable before stopping it, stops only the named project's containers and retains data. Restart handles one owned service, then rechecks bindings. A failed Start/Restart stops owned applications, marks the run Failed and retains its private logs/data for diagnosis; it does not discard financial history. Stop before retrying a run with recorded processes. Port races or unavailable dependencies fail startup rather than adopting another listener.

Reset is destructive only to the named draft, so it requires the explicit switch:

Lifecycle commands hold an exclusive per-run file lock. Concurrent Start/Restart/Stop/Reset/Status commands fail without interleaving state writes or process ownership; retry after the active command finishes. The operating system releases the lock if its owning shell exits.

If `state.json` is missing while protected credentials remain, Start refuses to regenerate/overwrite them. Preserve the directory and recover its original state metadata; do not treat retained data as a fresh empty run.

```powershell
./scripts/first-draft-stack.ps1 -Action Reset -RunId <run-id> -ConfirmTestResources
```

Reset verifies owned containers and data volumes, removes the exact project's infrastructure, checks that its containers/volumes are absent and deletes encrypted secrets and the exported local certificate. It preserves private logs/build artifacts as diagnostic evidence and never deletes a POS user's existing `%LOCALAPPDATA%\NexaConnect\POS` state. A Reset run cannot be resumed. Start without RunId creates fresh data; there is no truncate/financial-history downgrade shortcut.

## Configuration and scope

The launcher derives each target and the highest minimum application compatibility from migration metadata, currently `0.38.0`: Platform Directory 3, Authorization 15, Restaurant 4, Catalog 4, Inventory 5, Kitchen 3, Order 22, Payment 18, POS 23 and Reporting 22. Compatibility is not a schema target. Database names retain the reused helper's `nexa_review_it_<uuid>_<service>` namespace, exclusively inside the generated draft PostgreSQL container. Runtime identities remain service-owned and cannot create databases/roles/tables or write migration history.

Generated POS artifacts live under `.runstate/first-draft/<run-id>/pos`, with a retained `pos-configuration.json` beside them. Checked-in POS defaults are now `cash_manual`, disabled test card and an empty QR path. The launcher supplies loopback addresses and the generated identity realm without modifying the checked-in file or an existing terminal's protected recovery state. Initial organization/restaurant/branch/store/terminal UUIDs are placeholders: do not launch POS until slice 2 provisions actual owning records and updates the retained configuration. Subsequent rebuilds preserve that retained scope rather than generating new identifiers. Configure the actual branch as `Asia/Singapore` in slice 2; the launcher does not claim to have created the branch.

Only the explicit QR profile also sets `Pos:UseIsolatedLocalPilotStorage=true` in the
published and retained POS configuration. A same-scope rebuild preserves the provisioned
identifiers and selects the same scope-hashed current-user `POS-Pilots` directory for
recovery/outbox/tokens. Earlier default `POS` data is untouched. A normal draft retains
the absent/false default. This flag requires the generated loopback realm and manual
tender; it cannot enable remote/hosted isolation or change an existing installed release's
custody. See [Windows setup](First-Draft-Windows-POS.md) and
[same-scope source refresh](QR7-Pilot.md).

For a new manual PromptPay run, provide an administrator-controlled test QR image:

```powershell
./scripts/first-draft-stack.ps1 -Action Validate -PaymentMethod promptpay_manual -QrImagePath <test-qr.png>
./scripts/first-draft-stack.ps1 -Action Start -ConfirmTestResources -PaymentMethod promptpay_manual -QrImagePath <test-qr.png>
```

It copies the asset into the run's POS directory. This verifies configuration presence, not QR ownership/bank confirmation; the cashier must still explicitly verify a test receipt/reference. Do not switch a running terminal's tender/scope while recovery is pending. This slice offers no manual-tender refunds, live bank/provider transfers, SGD pricing or fiscal certification.

The launcher's identity-only `draft-smoke` customer-viewer account verifies authentication without product membership or branch grants. It is not a privileged cashier/manager account. Secrets are random and stored as a current-user DPAPI `SecureString` in `secrets.clixml`; the run directory ACL grants the Windows owner and SYSTEM only. The owned PostgreSQL/identity/broker/Redis volumes persist Stop/Start. Explicitly configured Redis is now honored in Development/Test as well as production; an absent local connection retains the memory default, while production without a store still fails closed. The BFF's persisted per-application key ring and Redis session namespace are bound to this run. Rebuilding/Stop does not remove them; Reset retires the environment.

## Verification and limitations

```powershell
./scripts/test-rabbitmq-health-ownership.ps1 -ConfirmDisposableInfrastructure
./scripts/test-first-draft-stack.ps1 -ConfirmDisposableInfrastructure
```

Install locked frontend dependencies with `npm ci --ignore-scripts` and Chromium with `npx playwright install chromium` from `src/Frontend` before browser verification. The draft test owns a generated environment, checks manual configuration, performs real OIDC sign-in, verifies the existing browser session after BFF restart, restarts Kitchen, stops/starts the full stack, verifies persistent login and resets with exact cleanup. Use `-RunId` only for a disposable draft that may be removed by the test. Its bounded `verification.json` and login/resume boolean receipts are safe evidence; the browser storage state, credentials, certificates and raw service logs are private and excluded from sharing. A test failure never counts as acceptance.

Order, POS and Reporting expose anonymous `GET /health/live` with `{ "status": "Healthy" }`; existing shared JSON/correlation logging remains active. These routes demonstrate a live HTTP host, not financial/database readiness. Payment uses its existing `/health/ready`; other APIs are probed using authenticated OpenAPI metadata and Platform Directory's existing `/health`. Catalog-derived migrations, restricted runtime grants, workload audience checks and active financial bindings provide distinct initialization evidence. Reporting OpenAPI generation currently throws a GUID schema-export error; that metadata defect is recorded separately and its route is not used as liveness evidence. Joined product workflows/authorization, physical WPF/PDF printing, staff UAT, backup restoration, production acceptance and a pushed Ubuntu CI rerun remain later gates.

Service names retain `nexaconnect-customer-bff`, `nexaconnect-order`, `nexaconnect-pos`, `nexaconnect-reporting`, `nexaconnect-kitchen` and the existing owner names. Structured JSON service logs reside in the restricted run directory; OTLP export remains opt-in and is disabled for this standalone local graph. To debug with configured centralized telemetry, query `{service_name="nexaconnect-customer-bff"}` and `{service_name="nexaconnect-kitchen"}` by validated correlation ID; use the existing financial service queries in their runbooks. Never publish tokens, headers, cookies, bodies, financial data, private logs or arbitrary container configuration. No external alert receiver is provisioned by this slice.
