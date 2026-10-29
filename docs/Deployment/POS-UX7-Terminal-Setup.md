# POS UX7 — terminal setup and peripheral diagnostics

Implemented in source on 29 October 2026. Local activation and actual operator/hardware
acceptance are separate and must be recorded when observed.

## Use the overview

Sign in and open **Terminal & sync → Terminal setup overview**. It projects current input
mode, density, display roles, receipt layout, printer selection/detection and scanner mode.
It adds no setup wizard that silently changes settings. Defaults remain keyboard/mouse,
Standard density, Standard paper/Ask each time and scanner opt-in off at startup.

- **Configured** means an application setting, not successful physical operation.
- **Detected** means Windows discovery or an HTTP response at the last explicit check.
- **Untested** means no check/result exists, or physical acceptance is not recorded here.
- **Unavailable** means discovery failure, missing saved queue, no detected device,
  display safety lock, transport failure or an unusable HTTP response.

**Refresh setup & services** reads Windows display/queue metadata and performs anonymous,
header-only reachability checks. No sale, order, pricing, payment, refund, stock, shift or
drawer command is sent. It does not save printer choices or reassign display roles. Printer
selection and display-role controls remain in their existing sections. Input, display,
printer/test-receipt and barcode/test buttons navigate there without enabling scan mode,
printing or adding a product automatically. Use the explicit UX5/UX6 test controls afterward.

The six logical checks are Identity discovery, POS, Order, Catalog, Payment and optional
Media. Blank Payment uses the existing Order fallback; blank Media remains unconfigured/
untested. Other service checks GET the configured origin root; Identity GETs its configured
realm's well-known discovery address. Requests carry a fresh validated correlation ID and
no bearer, cookie, tenant header or body. Redirects are disabled and bodies are never read.
HTTP 2xx/4xx means the endpoint responded; final 1xx/3xx/5xx or transport/TLS failure is unavailable.
An HTTP 401/403/404 can therefore be **Detected**. This is reachability only: service health,
realm correctness, authorization, schema compatibility and business readiness remain untested.

Checks run concurrently with four-second HTTP timeouts and a six-second overall deadline.
Windows queue discovery waits at most six seconds in UI work; a blocked underlying Windows
call cannot be forcibly cancelled, so its task is reused until completion rather than
launching repeated blocked calls. Display enumeration uses the existing Windows adapter.
Counts in the overview/report are bounded to 256 queues and 32 displays. Values and
timestamps describe the last discovery, not a live hardware guarantee. A detected queue
does not establish online paper output; a keyboard-emulating scanner cannot be identified
by this feature and remains **Untested**. No manual acceptance checkbox invents a pass.

## Privacy, failures and report export

Overview actions require a current verified operator, no busy command and safe display.
Lock/sign-out/privacy reset cancels checks and clears service/printer observations. Late
results cannot install after the original subject/generation changes. Display count/role
metadata can remain as non-private window state. Existing customer projection never receives
diagnostic or support-report content. Errors use fixed guidance, without raw exceptions.

**Export support report** opens a Windows Save dialog and rechecks the original session
after it closes. It saves only after explicit destination selection. The version-one JSON
contains reported app/runtime/Windows versions, UTC timestamps, presentation flags, bounded
device counts, and six named service status/HTTP snapshots. It excludes endpoint addresses,
queue/device names, filesystem paths, organization/branch/terminal/operator identifiers,
credentials, tokens, cookies, headers, barcodes, bodies and transaction/financial data.
It is a diagnostic snapshot, not a support upload or acceptance certificate. See
[the exact file contract](../API/POS-Terminal-Diagnostics.md).

Serialization is allow-listed and capped at 16 KiB, with enum/count/version validation.
Save uses a unique temporary file and atomic replacement, rejects link/junction traversal
and removes its temporary file after failure. Failed export does not change operational
data. There is no automatic report, retained diagnostic database, recovery schema or new
preference file. The filename uses UTC with a `Z` suffix; timestamps are explicitly UTC.
Windows dialogs and installed drivers still require physical touch/accessibility testing.

Service `nexaconnect-pos-client`, logger `NexaConnect.POS.TerminalDiagnostics`, emits safe
`Terminal reachability failed`/`Terminal reachability rejected` events with the fixed service
enum, optional HTTP code and correlation ID. Filter logs by that category/event/time and
correlation; never attach bodies, endpoint strings, headers or exceptions containing secrets.

## Verification and remaining acceptance

`TerminalDiagnosticsTests` covers no-credential/header-only requests, no response-body read,
HTTP/TLS/cancellation outcomes, settings versus detection/acceptance labels, strict report
fields, rejected injection/invalid values, atomic export and destination failure cleanup.
The required Windows input/scanning fixture additionally tests overview rows, explicit
checks, navigation without submission, and cleared/late results on the WPF dispatcher.
It performs no live authentication or actual printer submission. Synthetic overview render
inspection is separate from real POS/device acceptance.

Run the existing complete `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` gate.
After activation, sign in, refresh the overview, compare it with actual settings, use the
existing receipt/barcode tests and explicitly export/inspect a JSON report. Confirm that
no identities, URLs, printer names or operational data appear. Verify cancellation, missing
queue/service, lock during checks and both input/density modes on the actual terminal.
Saved PDF, native scan-to-cart, physical printer/scanner/display/touch, phone and long-lock
acceptance from earlier slices remain pending until explicitly observed.
