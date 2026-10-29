# Terminal setup diagnostics

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
