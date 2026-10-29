# Explicit barcode entry

`BarcodeScanPolicy` validates alphabet/context; `BarcodeRepeatGuard` suppresses identical
successful scans inside 750 ms. `MainWindow.Scanning.cs` owns session-only opt-in, explicit
field, live lookup/context fences and cancellation before reusing the existing cart.
The opt-in lasts only for the running POS window, defaults off on startup and may remain
checked across lock/resume or sign-out. Those transitions clear partial input/pending lookup,
not the checkbox. No scan queue, barcode map or scanner preference is persisted.

`ScannerSafeConfirmation` protects POS-owned decisions while scanner mode is enabled:
text/Enter cannot select/accept, pointer/touch or Tab then Space is deliberate, and original
session invalidation cancels. Ordinary mode keeps existing native dialogs. This is not
global keyboard filtering or hardware identity. External driver/browser/app dialogs retain
their handling. Supported devices require Enter-only suffix, without Tab/Space/control
prefix/suffix. Vendor serial/SDK, weighted/GS1 and card authentication remain planned.

Existing Catalog transport uses correlation and `catalog.barcode.lookup`; codes, bodies,
menu content, tokens and arbitrary headers are not logged. Lookup and the existing server
quote remain authoritative. See [UX6](../../../../docs/Deployment/POS-UX6-Barcode-Scanning.md).
