# POS UX22 — normal pilot startup and service recovery

[UX23 background hosting](POS-UX23-Background-Hosting.md) is the optional scheduled owner of the normal graph. Once installed, Start delegates to it; Stop/Remove the host before Refresh/RefreshQr or direct lifecycle maintenance. Without a host installed, the original UX22 startup behavior below remains available.

UX22 adds a current-Windows-account launcher for the **normal local test pilot** and a
staff-only service availability row. It reuses first-draft lifecycle, immutable terminal
installation and QR7 activation guards. It creates no orders, payments, reference data,
permissions, schema or replacement acceptance tracker.

## Starting the installed normal pilot

From the repository root in PowerShell7.4+:

```powershell
./scripts/local-pilot.ps1 -Action Status
./scripts/local-pilot.ps1 -Action Start -ConfirmTestResources
```

The default target is the current account's owned `NexaConnect/DraftTerminal` installation.
`-InstallDirectory` selects another owned installation; optional `-RunId` must equal its
installed run. The package inventory, reference scope, service addresses and identity must
match. Recovery-lab markers or lab access references are rejected: use [UX21's launcher](POS-UX21-Real-Identity-Recovery-Lab.md)
for that environment. An already-open different POS profile blocks normal startup.

Status reports `target=normal-pilot`, run/release identity, published-input matching and fixed
service/status rows. It does not read cashier credentials or mutate services, callbacks,
recovery or acceptance. It creates a protected diagnostics directory/lifecycle lock.
Anonymous header-only probes use generated correlations, no cookies and no redirects;
401/403/404 responses mean reachability, not authorized business access. Identity discovery
must respond too. Its port must map exactly to the single running Keycloak container labelled
for this run; an unrelated listener is not accepted. Status and Start require Docker Desktop's
local named-pipe context for this ownership check. A stale `Ready` state is never sufficient.

Start verifies PID/start time, executable/assembly and listener ownership before probing.
A foreign listener or changed executable is never stopped or adopted. If services respond
and installed native/server inputs match source, Start opens POS or reports that the exact
installation is open. If services need recovery, finish work and close every POS window.
Explicit test-resource confirmation permits the owned Stop/Start `-NoBuild` sequence:
it resumes the same compiled publication, scopes, volumes, credentials and configuration,
then checks again. It restarts the owned graph, never a financial request. Docker Desktop
must be available through its local named-pipe context with existing images.

Every successful stack Start now records source and executable inventories for all profiles.
NoBuild checks both before startup and never silently rebuilds or accepts drift. Legacy QR
inventories are supported; an older non-QR run without inventories needs one build-enabled
refresh. Source mismatch blocks Start even if ports respond.

## Explicit same-scope refresh

After verification and POS closure:

```powershell
./scripts/local-pilot.ps1 -Action Refresh -ConfirmTestResources
```

This uses owned Stop and build-enabled Start, creates a fresh package and installs it with
read-only recovery preflight and atomic activation. POS stays closed. Take normal private
backups before maintenance; this launcher is not a backup system. Failure preserves protected
diagnostics and never invokes Reset or completed reference provisioning. Use Start afterward.
If a QR edge is running, pass its exact `-PilotId`, which must belong to the installed run;
it is stopped before rebuild. Refresh does not rebind or activate that edge.

Hidden child maintenance operations write separate protected stdout/stderr files under
`.runstate/local-pilot/<run-id>/`. No logs print secrets, headers, response bodies or payment
data. Inspect the fixed failed step and owning component's correlated events. Native service
`nexaconnect-pos-client`, logger `NexaConnect.POS.TerminalDiagnostics`, emits safe
`Terminal reachability failed`/`rejected` events keyed by service/status/correlation.

## Refreshing and starting table QR access

After a fresh [QR6 gate](QR6-Integration.md), retain its exact candidate directory:

```powershell
./scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure
./scripts/local-pilot.ps1 -Action RefreshQr -PilotId <previous-pilot-id> -CandidateDirectory .runstate/qr6/<fresh-candidate-id> -ConfirmTestResources
# Use the NEW pilot ID printed by RefreshQr:
./scripts/local-pilot.ps1 -Action Start -PilotId <new-pilot-id> -ConfirmTestResources
```

RefreshQr requires POS closed and matching Ready backend inputs. It creates a fresh plan,
checks the previous activation hash, original plan bytes, exact branch scope, same origin/
address and certificate hashes, and copies only existing certificate material and recorded
device facts/trust. QR7 Configure rechecks actual PC address, DNS, source, server inventories,
exact SAN, chain and lifetime. No device trust is installed. Changed/expired certificates or
network facts require normal prerequisite setup. Old plan/activation/observations stay unchanged;
new phone cases stay pending. There is no evidence transfer.

After Configure succeeds, the helper stops only the previous owned edge and starts the new
trusted HTTPS edge. Failure preserves records; there is no automatic rollback or TLS bypass.
The new ID is saved at `.runstate/local-pilot/<run-id>/qr-current.json`; pass it on later Starts.
An already-running valid edge is verified and reused; stale metadata uses QR7's owned Stop/Start.
Source proof still expires under the existing 24-hour candidate policy.

## Cashier status and retry

The header's **Check services / retry** works before sign-in and during active work. Existing
bounded anonymous probes summarize Identity, Menu, Orders and Payments as responding,
unavailable or not checked. Root 401/404 is not proof of selling/settlement access. Terminal
Overview retains detailed last-check rows. No background polling, service restart, token
refresh or automatic payment/menu retry occurs. Once connectivity recovers, choose Refresh
menu or explicitly verify the saved original order/payment through its existing workflow.

Checks are unavailable during busy work, display safety blocking or authentication lock.
They remain inside the staff surface; privacy transitions cancel/clear observations and
generation/identity fences reject late results. Customer display/screensaver/authentication
panels receive no diagnostics. Menu failure points to checks without clearing carts,
custody or original financial recovery.

## Verification and limits

Run `scripts/test-local-pilot-guards.ps1`, `node --test scripts/release/local-pilot.test.mjs`,
unit `TerminalDiagnosticsTests` and Windows `NexaConnect.PosTouchTests`. The complete QR6
gate includes the new pure guards. Verify a real normal-pilot Stop/Start-NoBuild cycle,
matching installation, nonempty authenticated menu and trusted guest HTTPS during activation.
Automated checks never record an operator pass. Physical equipment, UX21 operator recovery/
access cases, fresh phone workflows, hosting and release readiness remain separate. No native
POS is force-killed by the launcher.

## Verified preparation — 1 November 2026

Candidate `a48926d1-0571-4c76-8a98-543251ae9b6d` passes the complete source-bound gate:
1,391 unit tests, seven architecture tests, 136 HTTP tests, 90 frontend tests, native WPF/
printing checks and 154 browser cases. The 22 launcher guards and nine QR-plan guards pass;
13 owning-service and four drawer-authority fixture cases pass with cleanup verified.
Source revision is `e26bca219f85ccf7343c5e1ec5723939dbb524f1`, dirty=true, input hash
`1f492681bfda55ac4eb6d0d41084dc4ebf3dc2446749e610f69cf9257850347d`.
The earlier candidate was rejected for source drift during the ownership fix and is not proof.

Normal-run package `657f4a83b68a4ecbae42cf9bc5469f16` is prepared and its 23-file inventory
validates. The documentation audit passes. At this preparation stage installation, the real
normal-pilot NoBuild recovery cycle and QR activation remain pending POS closure; phone and
cashier operator cases remain independent. No release readiness is asserted.

### Rollout checkpoint — 2 November 2026

The authorized normal-pilot Refresh completed and installed release
`c2bf1bbefe8345cfb1b608aa305981ac`; its installer preflight passed. Final stopped-service
NoBuild recovery and fresh QR activation remain pending. POS was subsequently reopened
from the run's published `pos` directory, so it must close again before those maintenance
checks. Earlier preservation snapshots precede that operator use and are historical.
The prepared QR candidate is now outside its 24-hour activation window: run a fresh gate
before QR rebinding. Installation alone does not pass the remaining operator cases.

That checkpoint is superseded by [UX23's activated successor](POS-UX23-Background-Hosting.md#activated-normal-pilot--2-november-2026): stopped-graph recovery and fresh trusted QR startup are verified on the new installed target. Evidence is not transferred to the older UX22 release or operator pack.
