# POS UX5 — printer setup and 80 mm receipts

After installing the [terminal-sections follow-up](POS-UX10-Windows11-Cashier.md#terminal-sections-follow-up),
printer controls are under **Terminal & sync → Input & devices → Receipt printer**. Earlier
releases place them directly on the Terminal & sync page; the workflow below is otherwise unchanged.

Implemented and installed in the retained local pilot on 28 October 2026. Saved-PDF
inspection and actual printer acceptance remain pending. The purchased printer's model,
connection, driver and exact printable area are still unconfirmed.

## Configure and use

1. Sign in and open **Terminal & sync → Receipt printer**. Select **Refresh printers**.
   Discovery runs off the UI thread and lists installed Windows local/connected queues.
   Install the vendor's Windows driver if the printer is absent. No raw ESC/POS, drawer
   pulse, scanner integration or device certification is introduced by this slice.
2. Select a queue or **Ask each time (Windows dialog)**, the default. Choose **Standard
   page (default)** or **80 mm roll**, then **Save printer settings**. The selection is
   saved per Windows user and full organization/restaurant/branch/store/terminal scope.
   It is independent of default-off touch input and Standard/Touch display density.
3. Select **Preview test receipt** to inspect synthetic text without printing. **Print
   test receipt** explicitly opens the Windows dialog and prints a document marked
   **TEST PRINT — NOT A SALE**. It creates no order, payment, refund or cash movement.
4. The saved queue is preselected in the dialog; the operator still confirms every job.
   A different queue chosen in that dialog applies only to that job. Save settings to
   change the terminal's preference. Missing saved queues fail without substituting a
   default printer; refresh and explicitly select another queue or Ask each time.
5. Set the actual paper size/orientation in the driver's print preferences. This POS
   layout does not install drivers, configure paper forms or force custom driver sizes.
   The 80 mm layout requires reported paper width at least 80 mm (one DIP rounding
   tolerance), at least 64 mm usable content and 20 mm usable vertical space. Missing,
   invalid or undersized bounds fail before spool submission.
6. In **Receipts**, load an original paid Order or completed provider refund, inspect its
   preview and select Print/reprint. Both use the saved printer/layout. A standard page
   remains the default for existing PDF workflows. An 80 mm preview uses 200 mm pages;
   printing uses the driver's reported height, so pagination can differ. On larger PDF
   paper it remains an 80 mm strip at the left, not a receipt enlarged to A4.

The roll layout has nominal 4 mm side margins and at most 72 mm content width, reduced
only to respect the driver imageable area. Text wraps without ellipsis; long receipts
paginate. Server names/numbers/totals remain literal text and are not recalculated.
Typography is black on white and independent of POS density. This is an ordinary sales
receipt, not a fiscal tax invoice; no business identity or tax fields are invented.

## Authorization, failures and custody

Sales reads retain live branch `order.read`; completed provider refund reads retain live
`payment.refund.read`. Every print reloads/validates before the dialog and again after it
closes, verifies unchanged receipt content, and checks the original session/generation at
each asynchronous boundary. Refund lookup also checks the exact requested refund UUID.
Lock/sign-out/display safety prevents a pending print from starting submission; already
submitted Windows jobs cannot be withdrawn by POS lock. Delayed refund results cannot
repopulate a receipt preview after a session transition; retained operations remain verifiable.
Customer display never receives receipt or printer settings data.

Print cancellation, missing/offline/error queues, insufficient paper bounds, authorization
failure and spool errors have payment-preserving guidance. There is one explicit submission
attempt and no automatic print retry. Inspect the Windows queue and actual output before
retrying an uncertain job; a spool failure may occur after Windows has accepted it. A
successful call certifies submission only, not paper delivery or a PDF saved to disk. Never
collect again to fix a receipt. Printing does not alter Paid/refund state, idempotency,
Inventory, Kitchen, shifts, settlements or outbox. Native print dialogs remain Windows UI;
physical touch/driver/accessibility behavior must be tested on the terminal.

`%LOCALAPPDATA%\NexaConnect\Preferences\<scope-hash>\printing.json` is a separate non-secret
version-1 file, bounded to 4096 bytes on read/write, written atomically with link/junction
guards. It stores only queue name (nullable, at most 256 characters without controls) and
paper enum. Invalid/unreadable files fall back to Ask each time/Standard; failed saves retain
the prior settings. No input-preference, SQLite, financial recovery or API schema changes.
Receipt content and test preview are session-only and clear at privacy transitions.

Service `nexaconnect-pos-client`, logger `NexaConnect.POS.Printing`, emits safe printer
preparation/submission failure and Windows-spool submission events. Existing receipt HTTP
reads retain correlation and safe boundary events. No queue name, receipt content, amounts,
tokens, personal data or exception bodies are logged by the new printer adapter.

## Verification and pending acceptance

Source-bound candidate `145f4ee9-6a4c-409c-89e1-82db0d89726c` passed on 28 October
2026: 1,168 unit tests, 6 architecture checks, 47 HTTP tests, 47 hosting checks, 88 frontend
tests, 57 QR browser cases, 45 financial browser cases and 7 owning persistence cases with
cleanup/broker recovery verified. Candidate/acceptance guards and the required Windows
`pos-print-layout` check passed. The separate WPF settings/input/density fixture passed.
Independent documentation audit found no blocking findings. Package
`2f026a28cde040d39649e8697448f3a0` was installed as recorded below. This is a dirty-source
local candidate, not a signed or accepted release.

### Local activation

After the operator confirmed closure, the retained stack `c30b628ab23141f389119daa565f894f`
rebuilt without reset and reached Ready with all 12 services. A diagnostic helper rebuilt
the local Debug POS output after the initial gate; strict compiled-artifact checking
rejected the first unconfigured pilot. The earlier proof and rejected attempt were retained.
A complete fresh gate passed against unchanged source as candidate
`af781de7-4255-42ff-b62a-5e70f5ec1bdb`, with the same passing counts above. Fresh pilot
`0ba2df75d4ee4e2c80b874d719c0ce8b` configured against that proof and passed trusted HTTPS
diagnostics at the unchanged `https://192.168.0.191:8443` origin: guest 200, staff-route 404.
Certificate bytes are unchanged. Diagnostic builds must not be treated as preserving an
earlier compiled-output proof; revalidate rather than rewriting failed evidence.

Release `2f026a28cde040d39649e8697448f3a0` installed into the existing current-user
DraftTerminal and reopened with a responsive native window. All seven recorded recovery,
gallery and preference files matched before launch; reference, scope and service URLs
were unchanged. Read-only printer discovery found eight queues including Microsoft Print
to PDF and submitted no jobs. Settings remain no saved printer/Standard paper, Standard
density and automatic keyboard off. A live gallery-only read still returned two staff
slides, two customer slides and three checksum-valid PNGs after the stack refresh.

No interactive native sign-in, sale, refund, print dialog or real spool submission was
performed during activation. Actual saved-PDF, physical printer, touch/driver and other
phone/display/long-lock acceptance remain explicitly pending. Fresh phone templates
contain 24 pending observations; earlier device checks are not reused as new passes.

`ReceiptPrintingTests` covers preferences, isolation, bounds, exact refund identity,
live reauthorization after dialog, immutable reprint content, lock races, cancellation,
missing queues and a single uncertain spool attempt. Windows fixtures run without live
authentication, financial data or printer submissions:

```powershell
dotnet run --project tests/Windows/NexaConnect.PosPrintingTests
dotnet run --project tests/Windows/NexaConnect.PosTouchTests
./scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure
```

The candidate gate now requires `pos-print-layout`; old receipts lacking that check do
not satisfy the new candidate inventory. Rendering checks Standard/80 mm pagination,
all-page glyphs within printable margins, long names, Thai text, exact totals and literal
markup; output previews are synthetic under `.runstate`. Settings fixtures check save,
rollback, test preview, fake cancellation and privacy clearing.

After installation, explicitly select Microsoft Print to PDF, save each layout, print the
synthetic test and inspect the resulting file. Then print/reprint one authorized original
paid receipt and verify identical receipt identity/amounts without another collection.
Check dialog cancellation, denied reads, driver outage, missing saved queue, restart,
keyboard/mouse and optional touch use. When hardware arrives, confirm printer model/driver,
80 mm paper form/margins, feed/cut behavior and actual readability. Drawer integration is
separate. Long locked slideshow, phone, physical display and release gates remain pending.
