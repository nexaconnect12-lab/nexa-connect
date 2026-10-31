# Terminal setup diagnostics

UX17 `PosLifecycleTrace` writes bounded local JSON-line lifecycle logs under the current terminal storage directory’s `diagnostics` subdirectory: service `nexaconnect-pos-client`, category `lifecycle`, numeric process/time metadata and fixed stage/outcome enums only. The exact eight-field allow-list excludes exception messages, identity, endpoints and business data. Up to 96 records/process and 64 KiB/file are allowed, with best-effort retention of eight matching files; writes are background/serialized, reject links and fail silently. ApplicationExit completion is not OS-process or financial completion evidence. No upload/export is added. See [paths, debugging and rollout](../../../../docs/Deployment/POS-UX17-Shutdown-Diagnostics.md).

The terminal overview is the initial **Terminal & sync → Overview** inner section. Input,
display and printer shortcuts explicitly select **Input & devices** before scrolling to the
target. Drawer/history and sync/recovery have separate inner sections. Switching sections
hides input targeting an inactive field; selecting a section does not run diagnostics or
business actions. See [layout/rollout](../../../../docs/Deployment/POS-UX10-Windows11-Cashier.md#terminal-sections-follow-up).

`TerminalSetup` projects typed presentation facts and explicit check results into staff
readiness rows. Configuration, detection and unrecorded physical acceptance remain distinct.
`MainWindow.TerminalSetup.cs` owns navigation-only shortcuts, cancellation/generation fences,
deduplicated bounded printer discovery and explicit support export. It does not save settings
or start receipt/barcode/business actions.

`PosTerminalDiagnostics` is an independent anonymous HttpClient with redirects/cookies off,
four-second request timeout, six-second overall deadline, generated correlation and header-
only completion. Identity uses discovery; other checks use origin root. The adapter never
reads response bodies and accepts no cashier token. HTTP response availability does not certify business readiness.
Service `nexaconnect-pos-client`, logger `NexaConnect.POS.TerminalDiagnostics`: search safe
`Terminal reachability failed`/`rejected` events by service enum, status, time and correlation.
Never log URLs, exceptions, bodies, headers or credentials.

`TerminalSupportReport` is an explicit 16 KiB allow-list, not serialization of UI/configuration
objects. Only numeric versions, UTC times, flags, counts and service enum/status are saved.
Atomic file replacement and link guards protect the explicit user-selected destination.
No business identifiers, queue names or endpoints are exported. There is no upload or new
local state/recovery schema. See [file contract](../../../../docs/API/POS-Terminal-Diagnostics.md)
and [setup, failures and verification](../../../../docs/Deployment/POS-UX7-Terminal-Setup.md).
