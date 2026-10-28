# Receipt printing

Client-owned presentation and Windows driver integration. `ReceiptPrinterPreferences`
stores separate bounded, atomic non-secret terminal preferences; `ReceiptPrintLayout`
calculates Standard/80 mm imageable bounds. `ReceiptDocumentFactory` wraps literal immutable
server text in WPF pages without recalculating money or accepting markup.

`ReceiptPrintWorkflow` is the read-only orchestration seam: load/preview, explicit driver
dialog, reauthorize and compare the original content, session check, single submission.
`IReceiptPrintDriver`/`IReceiptPrintJob` permit isolated cancellation/race fixtures.
`WindowsReceiptPrintDriver` resolves installed queues, retains the selected job's server/
queue lifetime and uses WPF printing. It does not implement the future raw-byte
`Hardware.IReceiptPrinter`, ESC/POS, printer installation or drawer/scanner control.

`MainWindow.Printing.cs` binds staff settings and synthetic test output. Sales/refund reads
remain owning-service authorized. No backend, database or financial command changes.
Native print dialogs may require driver-specific touch/accessibility acceptance.

Service `nexaconnect-pos-client`, logger `NexaConnect.POS.Printing`: filter `Receipt printer
preparation failed`, `Receipt spool submission failed` and `Receipt submitted to Windows
spooler`. No queue names, receipt bodies, amounts, exceptions or credentials are logged.
See [UX5 setup, ownership, failures and tests](../../../../docs/Deployment/POS-UX5-Receipt-Printing.md).
