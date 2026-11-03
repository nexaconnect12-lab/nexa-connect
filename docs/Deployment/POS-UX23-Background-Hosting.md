# POS UX23 — supervised Windows local pilot

UX23 uses a least-privilege **current-user interactive Task Scheduler task** to own the
normal pilot backend graph independently of a launcher terminal. It preserves the existing
Windows DPAPI identity and does not store a Windows password, use SYSTEM, add a public
endpoint or change business APIs, permissions, schemas or financial recovery.
See [ADR-062](../Architecture/Decisions/ADR-062-current-user-pilot-supervision.md).

## Diagnosis and scope

The observed symptom is an open POS with stopped application processes and live Docker
containers. The historical cause is unconfirmed. A disposable test reproduces termination
of a process assigned to a kill-on-close Windows job; a scheduled worker launched from
that job survives its termination. This establishes an isolation mechanism, not proof that
the original interruption had that cause. All fixture tasks are removed after verification.

The host is local test tooling, not production service hosting. It requires Windows,
PowerShell7.4+, .NET10, Node/Git, local Docker Desktop, a matching provisioned normal pilot
and its owned terminal installation. Windows must be signed in as the account owning the
protected data. Demand startup is the default. Optional at-logon startup resumes after
Windows restart **and that account signs in**; it does not run before logon. Docker Desktop
must also be available. The tool never reboots the PC or changes Docker startup settings.

## Prepare and install

Finish work and close POS. Take the usual private backups, then use the [UX22 refresh](POS-UX22-Local-Pilot-Startup.md)
to publish/install matching source and, when needed, prepare fresh QR activation. No previous
operator passes transfer to a new target. With all prerequisites ready:

```powershell
./scripts/local-pilot-host.ps1 -Action Install -ConfirmTestResources
# Optional explicit QR ownership and Windows sign-in startup:
# ./scripts/local-pilot-host.ps1 -Action Install -PilotId <fresh-pilot-id> -EnableLogonStart -ConfirmTestResources
$hostId = '<fresh ID printed by Install>'
./scripts/local-pilot-host.ps1 -Action Start -HostId $hostId
./scripts/local-pilot.ps1 -Action Start -ConfirmTestResources
```

Install defaults to the current account's owned DraftTerminal directory; `-InstallDirectory`
can select a separate owned installation. It rejects recovery labs and mismatched scope,
reference, source, package and backend inventories. It publishes the bounded telemetry tool,
pins its files and worker script, registers one non-elevated interactive task without stored
credentials, and writes a protected current-host pointer. It does not start the task.
Registration failure attempts removal of only its unchanged newly registered task.

Metadata and diagnostics live under `.runstate/local-pilot-host/<host-id>/`; the run pointer
is `.runstate/local-pilot/<run-id>/host-current.json`. Host/configuration hashes detect drift;
they are not signatures or an authorization boundary against the same Windows user.
Different task actions, principal, trigger, working directory, limits or battery policy are
rejected without stopping/adopting that owner. A changed installed release requires a fresh
host binding. No scoped recovery is copied, deleted or reset.

## Recovery behavior

The worker checks owned PID/start time, executable/assembly and listeners every five seconds
between bounded lifecycle operations. It resumes **exited** services using the new owned
`first-draft-stack.ps1 -Action Resume`; it never rebuilds, migrates, provisions or calls an
order/payment endpoint. Source/package/backend/reference/worker/telemetry bindings are
revalidated before recovery. Foreign ownership blocks recovery. A live non-listening process
is reported degraded and is not killed automatically. HTTP business health and cashier
permissions remain per-action checks, separate from process availability.

Resume preserves existing healthy PIDs. Newly started producers initially have outbox
dispatch disabled until required consumer bindings are ready, then only those new producers
restart with dispatch enabled. Failure cleans only processes started by that Resume call.
Database transactions, durable inbox/outbox and original idempotency/recovery rules retain
their existing owners. A request interrupted by process failure still needs explicit original
order/payment verification; the supervisor never resubmits it.

There are three automatic graph recovery attempts, with bounded backoff; the durable budget
survives worker restart. Sixty seconds of stable process availability resets that graph budget.
The scheduler may restart a failed worker up to three times at one-minute intervals. A
separate three-attempt QR budget prevents repeated edge startup. A source drift or blocked
recovery leaves healthy dependencies alone. Status timestamps can pause during a bounded
startup; Start waits up to five minutes and then reports pending rather than opening POS early.
The transitional `running` / QR `recovering` status is not ready: the worker still owns
the QR maintenance fence. Start waits for a known non-recovering QR state before returning;
a separately reported blocked QR state is not evidence of QR or business acceptance.

If configured with a fresh QR pilot, the host also resumes its exited owned edge. It preserves
foreign/live non-listening edges and never refreshes certificates, trust, candidate proof or
phone observations automatically. Expired proof blocks QR recovery until explicit maintenance;
it does not tear down healthy backend services. Existing live edges are process observations,
not fresh camera/ordering acceptance.

## Status, explicit retry and shutdown

```powershell
./scripts/local-pilot-host.ps1 -Action Status -HostId $hostId
./scripts/local-pilot-host.ps1 -Action Retry -HostId $hostId -ConfirmTestResources
# Finish work and close POS before shutdown:
./scripts/local-pilot-host.ps1 -Action Stop -HostId $hostId
./scripts/local-pilot-host.ps1 -Action Remove -HostId $hostId
```

Status exports a fixed whitelist of operational enums, counters, process/time metadata and
false business/release flags. Retry explicitly resets recovery budgets after binding checks;
ordinary Start does not bypass an exhausted budget. Stop signals the worker and waits for
owned QR/backend shutdown, preserving volumes and recovery. It refuses an open POS. A pending
graceful stop is reported; no retained POS/task is force-killed. Remove requires a stopped
graph and unchanged task/pointer ownership; it unregisters only that task and retains evidence.

Supervision and UX22 maintenance share a lifecycle fence. Direct first-draft mutations are
blocked while a host pointer is installed; the worker's owned operations identify that host.
The host ID is a local coordination marker, not a secret or permission token. Stop/Remove
before rebuild, QR refresh, reset or installer maintenance; then install the new host binding.
The host supplies `NEXACONNECT_LOCAL_PILOT_HOST_ID` only to its lifecycle children for this
fence; do not put it in `.env` or manually use it to bypass maintenance ownership. Backend
application startup strips inherited operational overrides through the existing lifecycle.
UX22 Start delegates to the installed host and rechecks reachability before opening POS.
The normal POS and recovery lab still run sequentially under their existing callback guards.

## Telemetry and verification

`NexaConnect.LocalPilotTelemetry` uses the shared observability factory for JSON console and
optional OTLP logs, service `nexaconnect-local-pilot-host`, logger `NexaConnect.LocalPilot.Host`.
Only fixed phase/QR enums, attempt counts and generated GUID correlations are emitted on
transitions. Private event output rotates at 64 KiB with one previous file. Optional Install
`-OtlpEndpoint http://localhost:4317` selects a local collector; export is off by default and
export failure does not change financial/runtime outcomes. Never log tokens, passwords,
URLs, headers, response bodies, payment references or unrestricted personal data.

Run `test-local-pilot-host-guards.ps1` and `test-local-pilot-host-isolation.ps1 -ConfirmTestResources`.
The isolated real-stack `test-local-pilot-host.ps1 -ConfirmTestResources` additionally needs
POS closed: it creates fresh databases/identities/native scope and its own fixture installation,
starts a real scheduled host after owned Stop, exits only its Catalog, checks recovery without
restarting healthy dependencies, restarts the host and verifies task/resource cleanup. It
does not install over the normal terminal, open an interactive POS window or seed financial
transactions. Its guarded terminal installer does run the native `--validate-install`
preflight; the fixture receipt's `nativePosLaunched=false` means no interactive window.
The QR6 gate includes pure host guards and the disposable job/trigger check; the real host
receipt remains separate. Unit tests cover the telemetry event allowlist.

Actual Windows reboot/sign-in, sustained device operation, phone workflows, paid-order
operator cases and production readiness remain separate. Do not infer a reboot pass from
task registration, trigger inspection, launcher isolation or worker restart tests.

## Initial prepared verification — 2 November 2026

This historical checkpoint predates the readiness correction below. Its package is not the
current activation candidate.

Candidate `7704391c-1641-40c6-b0fc-61a34e163931` passes the full source-bound QR6 gate,
including 1,399 unit, seven architecture, 136 HTTP, 90 frontend and 154 browser cases,
native UI/printing checks, 18 pure host guards and the actual Windows job/demand/logon-task
fixture. Existing 13 owning-service and four drawer-authority cases pass with cleanup.
Isolation receipt `f60e85e3a82243429e6c5cc6a4e6313d` confirms its task cleanup and explicitly
does not confirm the historical shutdown cause or a Windows reboot.

Source is revision `e26bca219f85ccf7343c5e1ec5723939dbb524f1`, dirty=true, input hash
`9506380d2a01ad6847de671ff56b025b41c3f8bdc3618a7d8f7899f6b9549f52`.
Normal-pilot package `7ef9f18cf4ed480888467865380f7783` was prepared. At that checkpoint the
real isolated installer/host-recovery fixture and normal activation remained pending POS closure. No
retained terminal/task installation, reboot pass, operator case or release readiness is
inferred from this preparation.

## Corrected verification — 2 November 2026

Candidate `56062c16-8cb5-42f5-bbf8-ca6859a55a5d` passes the full gate after the QR-readiness
fence correction: 1,399 unit, seven architecture, 136 HTTP, 90 frontend and 154 browser cases,
native checks, 22 pure host guards and the disposable job/trigger check. Existing owning-
service and drawer-authority fixtures pass with verified cleanup. Source revision remains
`e26bca219f85ccf7343c5e1ec5723939dbb524f1`, dirty=true, input hash
`6d47db808f0526bffd946cd6f2d5e7ddc89b9569bd7f167044793fe8b560ae38`.

Real isolated host fixture `92c88c8b581d4821ae4b56a2f18f117f` also passes scheduled startup,
Catalog exit recovery with healthy dependency PIDs retained, host restart and cleanup. It
does not mutate the normal installation or seed financial transactions. Normal activation
is a separate authorized rollout; no normal-installation, phone, actual Windows reboot,
operator-case or release-readiness pass is inferred from these automated receipts.

## Activated normal pilot — 2 November 2026

The authorized same-scope rollout installs release `34a7f88b4991413aaa3dd7028c67223a` and
host `34cd14322c6648cabe08ee31be0101e4`, with QR pilot `179d8478ce3940b885745604cfc2320a`.
The corrected gate and real host fixture above remain the matching verification evidence.

The normal graph starts from stopped state through the scheduled worker: twelve owned
services and its QR edge run independently of the launcher command. The observed worker's
parent is Windows `svchost.exe`. A separate Start command exits while the worker remains
running. The original cashier's read-only PKCE check returns three menu items; anonymous
menu access is denied. Trusted guest `/order` returns 200 and guest-edge `/kitchen` returns 404.
The matching native window is responsive and left open. The activation checks submit no
orders or payments.

The pre-interactive preservation checkpoint confirms nine checked local files and 159
checked business tables unchanged, with original scope/configuration, credential values,
reference and certificate retained. These are checkpoint observations, not a promise that
files or business records remain byte-identical during later operator use. Evidence is under
`.runstate/ux23-activation/`; `.runstate/ux23-current.json` identifies the active target.

Demand startup remains configured: automatic Windows sign-in startup is off. Reopen the
normal terminal from the repository root using:

```powershell
./scripts/local-pilot.ps1 -Action Start -ConfirmTestResources
```

The host already owns the new QR pilot; no old activation or acceptance record is reused.
New installed-target pack `6b511123c5ec4351bbb6984e1fdd4f2e` starts with all seventeen
operator cases pending. Earlier operator evidence remains historical. Actual Windows
reboot/sign-in, phone camera/workflow and cashier/peripheral acceptance remain unverified;
no source test or activation result grants release or production readiness.
