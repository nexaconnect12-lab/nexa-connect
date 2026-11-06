# Receipt printing

Client-owned presentation and Windows driver integration. `ReceiptPrinterPreferences`
stores separate bounded, atomic non-secret terminal preferences; `ReceiptPrintLayout`
calculates Standard/80 mm imageable bounds. `ReceiptDocumentFactory` wraps literal immutable
server text in WPF pages without recalculating money or accepting markup.
The nominal 80 mm mode fits driver forms from 72 mm wide, including the operator-reported
72×297 mm form and 79.5 mm page reports, with one-DIP width rounding tolerance. Actual imageable
bounds, margins and 64 mm content/20 mm height minimums remain mandatory; a restrictive
72 mm driver form can still be rejected safely.
A separate `0.0000001` DIP arithmetic tolerance applies only to the final minimum-content
width/height comparisons, avoiding floating-point rejection of an exact 64 mm boundary.
It does not alter or enlarge reported page/imageable bounds.

`ReceiptPrintWorkflow` loads and previews the original content, prepares the queue,
reauthorizes and compares the content, checks the session, then submits once.
`IReceiptPrintDriver`/`IReceiptPrintJob` permit isolated cancellation/race fixtures.
`WindowsReceiptPrintDriver` resolves installed queues, retains the selected job's server/
queue lifetime and uses WPF printing. A saved queue prints directly with the user's
Windows preferences. **Ask each time** uses the driver dialog. Disable the driver's
drawer-on-print setting for receipt jobs. Raw device control belongs to separate hardware adapters.

`MainWindow.Printing.cs` binds staff settings and synthetic test output. Sales/refund reads
remain owning-service authorized. No backend, database or financial command changes.
Session-current test-print completion updates both printer-panel and main status; the
main “Preparing” message no longer persists after a returned cancellation/failure/result.
This source-only feedback follow-up does not establish POS806UE paper output or diagnose
a driver failure; inspect the final message and original Windows job before retrying.
Native print dialogs may require driver-specific touch/accessibility acceptance.

Service `nexaconnect-pos-client`, logger `NexaConnect.POS.Printing`: filter `Receipt printer
preparation failed`, `Receipt spool submission failed` and `Receipt submitted to Windows
spooler`. No queue names, receipt bodies, amounts, exceptions or credentials are logged.
See [UX5 setup, ownership, failures and tests](../../../../docs/Deployment/POS-UX5-Receipt-Printing.md).
