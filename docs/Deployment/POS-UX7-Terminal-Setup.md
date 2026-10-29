# POS UX7 — terminal setup and peripheral diagnostics

Implemented and activated in the retained local pilot on 29 October 2026. Actual operator
and hardware acceptance remain separate and pending.

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

The complete committed-source gate passed for candidate
`a990304b-d294-4611-98ab-d4d61a00a96a` at revision `ca050419e9e6e99a9054ea5ebd22089f635122fb`:
1,206 unit, six architecture, 55 HTTP, 47 hosting, 24 candidate-guard, 39 acceptance-guard,
90 frontend, 57 QR browser, 45 financial browser and eight owning integration checks,
plus required Windows build/print/input fixtures, media-profile guards and frontend
build/audits. Owning fixture cleanup and broker recovery passed. Independent documentation
review cleared the source implementation. Earlier dirty-source candidate evidence remains
historical; it was not relabelled after the source commit.

Installed release `d1840b9d3c284cb7b8041a4066d8b02e` uses retained stack
`c30b628ab23141f389119daa565f894f` and fresh QR pilot
`673db616cd9d4241b21ad5d6e2388917`. All 12 services returned Ready. The existing certificate
files were copied byte-for-byte; local Windows-trusted HTTPS returned guest page 200 and
staff denial 404. No phone acceptance was carried forward as a fresh pass.

The eight protected local files matched their pre-refresh hashes after installation;
terminal scope, reference data and service addresses were unchanged. POS reopened from the
installed release with a responsive native window. The installed diagnostic adapter detected
all six configured endpoints anonymously. A programmatic report saved successfully with
synthetic unknown peripheral facts and excluded configured URLs and identifiers. This probe
did not use the native Save dialog, authenticate a cashier, submit financial commands or
establish hardware readiness. Local receipts are retained under `.runstate/pos-ux7-*`;
these generated files are not committed release evidence or production acceptance.

At the UX7 post-activation guide update, only Markdown changed. The installed candidate was
guard-verified against the committed source before launch; its revision and executable
input hash were unchanged by that update. The checkout then became documentation-dirty,
whereas the immutable candidate records `sourceDirty=false`, so strict current-candidate
freshness no longer matched. Subsequent implementation work, including UX8 preparation,
has its own changed source and verification status. This historical installation evidence must not be relabelled
or reused to bypass the required current-source guard for a future activation. The guide
update does not change the installed binaries or require their reinstallation.

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
