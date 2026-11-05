# Physical Windows POS onboarding and versioned updates

## Scope and status

This slice implements preparation and guarded lifecycle tooling for a **separate LAN
pilot with fresh test data on the development Windows PC**, chosen by the operator on
5 November 2026. It reuses the private self-contained EXE installer, current public-client
OIDC/PKCE flow, owning service authorization and original local recovery. It does not
migrate or replace the retained normal/QR pilot, publish a download URL, install drivers,
enroll the physical device remotely or enable automatic updates.

Confirmed equipment: S200D Windows 11, 15.6-inch staff and 11.6-inch customer displays,
both user-reported 1366×768; PAISHUO POS806UF printer; USB Type-A 2D reader labelled
S26332; 4141P drawer connected to DK/DRAWER. Scaling, per-screen touch, printer/scanner
driver/input mode and physical output remain separate acceptance gates. Drawer opening
remains disabled. See the [equipment baseline](../Planning/Next-Iteration-Phase-0-Baseline.md).

Current network finding: development Ethernet is **192.168.0.191/24**, while the physical
POS is now reported as **192.168.0.165**, replacing the earlier `192.168.87.54` report.
The reported device address matches the host's /24 prefix; actual device connectivity and
its network configuration remain unverified. The launcher requires the same directly connected
private IPv4 subnet, and matching addresses alone do not establish working HTTPS or login.
It does not change DHCP, interfaces, routing, DNS, certificate trust or firewall rules.
The operator confirms the physical Windows account as **PC** and installation of the CA on
both Windows PCs. Normal Windows chain/SAN/expected-root verification passed on the development
PC; device trust is operator-confirmed and still requires its own onboarding check. The actual
installation must use account PC. The separate backend is now Ready/provisioned; physical
device installation and acceptance remain pending.
Never use an arbitrary unused-looking static address without checking its DHCP reservation.

## Topology and ownership

`NexaConnect.PosPilotEdge` terminates HTTPS on one exact private host IPv4/unprivileged
port. Only the development-PC and physical-POS source addresses are admitted. It forwards
the new pilot's one realm login/discovery/resources and `/api/{pos,order,catalog,payment,media}/v1/`
routes to distinct owned loopback upstreams. Admin/master/internal/guest/metrics routes
are hidden. This listener is independent of the existing guest-only QR edge; never point
staff POS at the phone listener or broaden its route list.

Business APIs require a bearer token before forwarding, except the exact locked-gallery
GET with its existing bounded `X-Nexa-Gallery-Reader` capability. Owning services retain
all live permissions, organization/branch boundaries, limits, transactions, idempotency
and audit. The edge never retries a financial command. API requests do not forward browser
cookies; identity forwarding preserves its login cookies/form and only returns same-origin
or the existing `nexaconnect-pos://oauth/callback` redirects. Client-supplied proxy headers
are discarded and rebuilt from fixed origin and admitted connection data. Request/response
buffers and admission are bounded; no TLS validation bypass is introduced.
Responses prohibit framing and use origin-only referrers without login paths/queries;
identity responses preserve upstream Content Security Policy. Same-origin browser forms
retain their Origin; null/cross-origin requests remain denied. The two native terminal and
client-operation GUID headers are validated and preserved for owning endpoint/idempotency
checks. See the transport contract for exact header behavior.

The new Compose override changes only the new container's canonical Keycloak hostname and
proxy-header interpretation; infrastructure ports remain loopback. Both frontend and
backchannel use the canonical staff HTTPS issuer. Backend metadata requests require normal
host trust. Keycloak administration remains local. See [ADR-066](../Architecture/Decisions/ADR-066-isolated-physical-pos-lan-pilot.md).
The shared Compose environment helper supplies the validated staff origin during both stack
lifecycle and the separate provisioning step, and restores the caller's prior environment.
Provisioning must not depend on an environment variable left behind by Start.
The [staff-edge transport contract](../API/Physical-POS-Pilot-Edge.md) lists exact admitted
surfaces, limits and failure semantics; it adds no business API or financial authority.

No schema, financial rule or new permission is added. Existing provisioning creates a fresh
branch/menu/terminal and scoped test identities in separately owned databases/realm/volumes.
Existing development orders/PINs/recovery are not copied. This LAN slice is manual cash and
branch-approved manual PromptPay only: no Omise sandbox, mobile QR activation or physical
drawer driver is enabled. The old pilot's accounts/orders/phone QR links do not belong to
this new branch. Local manager portal access uses the new run's printed loopback BFF URL.

## Prepare and start

Run on the development PC from the repository, using PowerShell 7.4+ and the existing
[draft prerequisites](First-Draft-Test-Stack.md). This creates a plan only:

```powershell
./scripts/physical-pos-pilot.ps1 -Action Init -HostAddress 192.168.0.191 -DeviceAddress 192.168.0.165
./scripts/physical-pos-pilot.ps1 -Action Status -PlanPath .runstate/physical-pos/<id>/plan.json
```

Those addresses record the current host measurement/device report, not verified connectivity.
Before Start, confirm both PCs are on the intended same LAN/subnet and update the
**not-yet-started** plan if their actual addresses change. Set its `origin` to
`https://<development-ip>:9443` (use an explicit free
unprivileged port consistently), the intended `deviceWindowsAccount`, `certificatePath`,
`privateKeyPath` and `clientCaPath`. Supply an exact-IP SAN server-auth certificate with
matching PEM private key and a valid public CA certificate. Install/verify the CA on both
Windows PCs through normal administration, then truthfully set `hostTrustConfirmed`,
`deviceTrustConfirmed` and `confirmFreshTestData` to true. These are prerequisites, not
automated device acceptance observations. Keep server private keys on the development PC.

Start is explicit and refuses an unrelated retained run. It checks the directly connected
subnet before starting resources, creates the separate stack, starts the bounded staff edge,
uses the canonical issuer and invokes existing reference-data provisioning:

```powershell
./scripts/physical-pos-pilot.ps1 -Action Start -PlanPath .runstate/physical-pos/<id>/plan.json -ConfirmTestResources
./scripts/get-first-draft-login.ps1 -RunId <id> -Role cashier
```

Use the login helper's existing account selection options locally; never share passwords or
tokens. If Windows Firewall blocks access, an administrator must explicitly allow only the
chosen HTTPS port on the Private profile and only the physical-POS address; no public port
forwarding or database/identity-management exposure. The tool creates no firewall rule.
After startup, exact plan/certificate/key hashes and edge inventory are retained. Do not edit
them to relocate an active issuer. Certificate rotation/identity relocation requires reviewed
maintenance; compatible setup never changes issuer or terminal custody.

This tool does not install UX23 supervision or sign-in startup for the new graph. Use complete
Start/Stop maintenance, not partial Resume/Restart/StopService. Finish all physical POS work
and close its window before stopping its backend. `Stop` retains business data and recovery:

```powershell
./scripts/physical-pos-pilot.ps1 -Action Stop -PlanPath .runstate/physical-pos/<id>/plan.json
```

On partial failure, retain the protected run/evidence and diagnose owned resources. Do not
adopt another run, overwrite state, force-close a cashier or reset databases to bypass guards.

## Build and transfer the first installation

Build the latest source through the existing approved compiler. Use a fresh directory and
explicit private-test version; this is not a signed/public release:

```powershell
./scripts/package-pos-installer.ps1 -CompilerPath .runstate/phase7-tools/inno/ISCC.exe -OutputDirectory .runstate/physical-pos-installer/0.8.0 -PackageVersion 0.8.0 -PrivateUnsigned
./scripts/physical-pos-pilot.ps1 -Action Bundle -PlanPath .runstate/physical-pos/<id>/plan.json -InstallerDirectory .runstate/physical-pos-installer/0.8.0 -OutputDirectory .runstate/physical-pos-transfer/<id>-0.8.0
```

Bundle requires the provisioned matching Ready pilot and unchanged source-bound installer.
The installer, published backend and current repository must have matching source identity;
the launcher also verifies the backend and complete staff-edge artifact inventories.
Bundle validation/copying holds the existing draft lifecycle lock to exclude concurrent
maintenance of that run; it does not quiesce or migrate a live cashier transaction.
It exports only setup, a strict public terminal profile, canonical public DER CA certificate,
the Windows PowerShell 5.1-compatible device check, instructions and a hashed onboarding
manifest. It never copies state databases, tokens, PINs, private keys, secrets or provisioning
credentials. PEM trailing material is not copied. Manifest, setup and public-CA hashes are printed
for separate verification. Keep the folder private; operational scope IDs are not secrets
but should not be unnecessarily published. Transfer by USB or a controlled private channel;
no installation link is published automatically. Hashes do not authenticate an unsigned publisher.

On the physical POS, use its intended ordinary Windows account (not elevated or a different
account). First compare `onboarding.json` SHA-256 with the manifest hash supplied through the
separate trusted channel, using Windows `Get-FileHash`. Before executing the checker or
installing the CA, compare their file hashes with the corresponding entries in that verified
manifest. Do not rely on an unverified checker to authenticate itself. Verify the public CA's
fingerprint independently and install it through normal administration, then run:

```powershell
.\Test-DeviceOnboarding.ps1 -ExpectedManifestSha256 <separately-supplied-manifest-sha256>
```

If Windows blocks a transferred script, review its hash/content before unblocking that
specific file through normal Windows controls; do not disable system-wide execution policy.
The independently verified manifest binds the profile/CA/checker as well as setup, preventing
manifest replacement from concealing changed configuration. The check validates fixed bundle hashes, normal HTTPS trust, exact issuer/PKCE and same-origin
OIDC endpoints, plus unauthenticated API denial. It does not log in, install software, mutate
trust or submit financial commands. A pass proves connectivity/configuration only.

Finish work and close POS, then run `NexaConnect-POS-setup.exe`, selecting `terminal-profile.json`.
The installer bundles the Desktop Runtime; no .NET SDK or separate Desktop Runtime is required
on the physical device. Existing setup maintenance/callback/custody and read-only recovery
checks remain authoritative. Start POS from its Start Menu entry and use the new pilot account.
Only authenticate on the staff screen. Configure Windows Extend displays and actual role
assignment/scaling, select/test the printer and enable/test scanner input using existing UI.

## Later version updates

Git source revision/input hashes identify code; the app/installer semantic version identifies
the installed release. Build each changed release under a new version/output directory,
for example 0.8.1. Run Bundle with `-PreviousBundle <prior-folder>` to reject equal/older
versions and issuer/terminal/tender custody changes. The actual installer also checks installed
version and retained custody, even without the optional prior bundle. Close POS after finishing
work, run the newer compatible setup and explicitly reopen/retest. No update runs during a sale.

Retain prior installer/manifests and backups for support. They do not authorize arbitrary
downgrades or restoring old financial/local state over newer work. Use reviewed forward fixes
or compatible recovery; preserve unknown original payment/refund operations and verify them
once through their original identity. Neither source tests nor installing a package certifies
an external provider transaction, peripheral, restore scenario or production deployment.

## Verification and safe diagnostics

### Local backend activation evidence — 5 November 2026

Plan `.runstate/physical-pos/b1bd29fd3d334a2eaaa9cae18f1cd0e2/plan.json` records host
`192.168.0.191` and reported device `192.168.0.165`, with intended Windows account **PC**.
The operator confirmed CA installation on both Windows PCs. Development-PC normal Windows
chain/SAN/expected-root verification passed; device trust remains an operator confirmation
until its own checker executes. No device installation/acceptance is claimed.

Protected plan-local TLS material is prepared: a 14-day exact-host-IP server-auth leaf and
matching key, plus a 30-day public CA. The root signing key was not retained. In-memory
custom-chain, SAN and key checks passed before the later normal development-PC trust check.
Certificate installation was performed/confirmed by the operator. The tooling did not install
trust or change firewall rules; physical-device HTTPS connectivity remains unverified.

The separate LAN graph is **Ready/provisioned**, with 12 application processes, seven owned
infrastructure containers, a staff listener at `https://192.168.0.191:9443` and three seeded
menu items. Canonical HTTPS identity discovery is reachable from the development PC. Its local manager
portal is `https://127.0.0.1:56464/`. Existing normal/QR data and listeners remain separate.

An initial provisioning failure exposed a missing staff-origin environment in the independent
provisioning step. The shared save/restore helper now supplies its validated optional
`StaffPilotOrigin` to both lifecycle and provisioning. Only the new, then-unprovisioned graph
was fully restarted/rebuilt before successful provisioning. Plan/Compose, 33 lifecycle and
21 Media guard checks passed after this fix.

The earlier private unsigned **0.8.0** candidate at
`.runstate/physical-pos-installer/0.8.0-eec52903621243d89f0cafb49edaa30e` is superseded
by this source correction. A freshly source-bound installer and onboarding bundle must be
built before transfer; the old compiled candidate must not bypass Bundle's current-source
and published-backend checks. A real browser form POST then exposed an edge-origin denial:
the earlier `no-referrer` response policy made Chromium submit `Origin: null`. Source now
uses `strict-origin` while still rejecting null/cross-origin requests. Native terminal and
client-operation GUID transport was also corrected to preserve original command identity.
The new graph was fully rebuilt in the same isolated scope after those fixes. A real
headless-browser PKCE sign-in over strict HTTPS now passes with the generated cashier,
as do UserInfo subject binding, three authorized menu items, unset PIN status, wrong-terminal
403, empty fresh order history, anonymous staff denial and hidden admin/master/guest/metrics
boundaries. The Node API portion uses the exact public test CA without a TLS bypass;
the browser uses normal trust. These are development-PC observations, not physical-device passes.

The matching private unsigned installer is now
`.runstate/physical-pos-installer/0.8.0-521c654942f64947b080a2b91359d414`.
The immutable six-file transfer bundle is
`.runstate/physical-pos-transfer/b1bd29fd3d334a2eaaa9cae18f1cd0e2-0.8.0`.
It is also copied, with hashes checked and no share-permission changes, to
`\\192.168.0.191\Shared\NexaConnect-POS-0.8.0-b1bd29fd`.
Its independently verifiable `onboarding.json` SHA-256 is
`354c24d1a700dcf1e60292e472e4dcb27cea75c1f2f9261ad2118d3308d27a89`.
No physical installation, payment, refund, sale or PIN change was submitted. The device
must still verify reachability/trust, install under PC and complete actual acceptance.

Final source/fixture checks passed: 50 HTTP cases (31 staff-edge and 19 guest-edge),
eight architecture checks, 11 Node cases, plan/parser/subnet and rendered Compose checks,
plus lifecycle 33, terminal 35, installer 15, Media 21 and QR 17 guard checks. These results
do not certify physical-device Keycloak/browser sign-in, device reachability/trust or
hardware acceptance. Development-PC identity reachability is the separate observation
above. No financial command or production deployment was performed.
The HTTP suite includes explicit same-origin callback, null-origin denial and native
operation-header preservation/rejection cases. Deployment-context guards also verify
the canonical origin is supplied and restored across independent provisioning.

```powershell
node --test scripts/release/physical-pos.test.mjs
./scripts/test-physical-pos-pilot-guards.ps1
./scripts/test-physical-pos-pilot-guards.ps1 -Compose
dotnet test tests/Integration/NexaConnect.IntegrationTests --filter FullyQualifiedName~PosPilotEdgeTests
./scripts/test-pos-installer-guards.ps1
```

Tests cover scope/version/source/hash rejection, no secret/local-path copying, canonical public
CA export, parser/subnet guards and synthetic HTTP host/client/route/bearer/identity/gallery/
redirect/body boundaries. Mocks do not establish actual Keycloak TLS/browser/device operation.
The optional `-Compose` check renders the owned fixture configuration with Docker Compose
without creating containers; it checks the canonical hostname and loopback infrastructure ports.
Complete fresh physical sign-in/PIN/lock, menu, original cash/PromptPay, receipt output/scanner,
dual-display privacy/reconnect, compatible update and interrupted-maintenance acceptance on
the actual device. Record failures and retest against the exact package/terminal.

Service name is `nexaconnect-pos-pilot-edge`; private logs are `StaffEdge.log` and
`StaffEdge.error.log` in the new run. Query by this service and validated `CorrelationId`,
then fixed `Upstream` and `StatusCode`. Shared observability registration, correlation
propagation and structured JSON are adopted. This tool rejects `Observability:OtlpEnabled=true`
before exporter registration: outbound identity-URL and original hosting-scope redaction
require separate verification before OTLP export can be supported. Original-path hosting
diagnostics and console scopes are suppressed; safe completion events include correlation
explicitly and inbound telemetry uses `/staff-edge`. Never share raw login URLs, cookies, bodies, headers, tokens,
provider data or private evidence. The edge's 401 probe precedes owning services and does not
certify live role/tenant denial; those require authenticated acceptance separately.
