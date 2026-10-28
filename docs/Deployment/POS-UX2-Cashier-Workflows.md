# POS UX2 — cashier workflows and UI refinement

The [optional input-mode setting](POS-Input-Mode.md) now defaults to keyboard/mouse
operation. Enable touch-first explicitly for automatic keyboards; the pinned UX2 dock
and financial/recovery behavior apply in both modes. Historical UX2 evidence below
does not establish acceptance of this subsequent setting change.

This slice implements the next bounded step in the
[long-term POS principles](../Planning/Next-Iteration-Roadmap.md#long-term-pos-product-design-principles),
building on [UX1 touch input](POS-UX1-Touch-Input.md). Source implementation does not by
itself update an installed package or certify the purchased S200D's touch/scanner behavior.

## Cashier experience

A pinned staff workflow dock remains above the on-screen keyboard and outside the
scrolling workspace. It shows the next-step guidance, amount provenance and status.
The original **Send order / Verify original order** button is pinned when Checkout is
selected; the original **Confirm payment / Verify payment** button is pinned in Payment.
Other tabs do not show these financial actions. These are the same named buttons and
handlers, not additional command paths or automatic confirmations.

The amount is explicitly a **Cart estimate · review server pricing** until an original
Order exists. A retained checkout without an Order response says **Verify original order**
instead of showing an empty QR cart's zero as a bill. A pending unpaid Order shows its
server-returned total and currency. The dock never infers Paid from a price, tap or timeout.

Next-step buttons only navigate: pending/blocked cancellation explicitly offers verification
or reconciliation rather than receiving payment. Original payment/checkout recovery takes priority,
followed by saved cash-review/refund recovery, shift setup, pending cash movements and
the cash-session prerequisite. Ready work returns to Checkout. Opening Terminal & sync
does not replay movements, and opening Payment does not collect or settle. The existing
buttons still perform the explicit authorized action and confirmation.

**Clear filters** resets menu search and station choice without typing. The QR collection
screen offers **Browse accepted orders**, opening the existing branch Orders view so a
cashier can choose a row and use the QR filter instead of typing a UUID. It preserves
existing collection eligibility, live scoped read authorization and original-order custody.
It does not accept guest requests, merge table bills or automatically collect an order.

Empty menu guidance now distinguishes sign-in, missing shift, busy work, no search matches
and retrying an unloaded/empty menu. Busy actions show a progress indicator, disable pinned
financial actions and explain that the cashier should wait rather than repeat the command.
Status and original session guidance remain in a bounded scrollable area of the dock.

Segoe UI, the existing green brand, consistent rounded text-field surfaces and at least
48-DIP text fields, dropdown items, data rows and common touch controls refine the Windows
11-inspired direction. Existing menu buttons and cart +/- controls are retained. Financial
confirmation dialogs and printing remain native Windows dialogs with their existing
explicit confirmation semantics; this slice is not a full framework/dialog redesign.

## Boundaries and failure handling

`CashierWorkflow` is a pure presentation projection of current client state. Its destination
and text provide no command authority. `MainWindow.Workflow.cs` maps destinations to
existing tabs. Shortcuts reject busy, expired/signed-out, locked and display-safety-blocked
states. Amounts come from the current estimate or original server response, never from
new tax/discount/payment calculations. No API, schema, service, identity role/client,
transaction, idempotency, audit/outbox or local recovery format changes.

The dock and keyboard hide behind the shared security/display-safety lock. Only staff
controls receive the new projection; the allow-listed customer display is unchanged.
Moving the original action buttons outside the tab content requires explicit busy
disabling; handlers and existing financial confirmations still enforce their original
guards. Retained shift, drawer, pending order/payment and original operator identities
are not discarded or reassigned by navigation, filter reset, keyboard use or styling.

At the supported logical window sizes, the workspace scrolls to keep the focused field
visible while the dock/keyboard reserve space. Small viewports can require scrolling to
other controls. Real resolutions, DPI, both screens' touch capability and peripheral
models remain pending specifications; inches alone do not establish layout acceptance.

## Verification and local rollout

From the repository root on Windows:

```powershell
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj --no-restore
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~CashierWorkflow|FullyQualifiedName~TouchInput|FullyQualifiedName~CashierPresentation|FullyQualifiedName~PosSession|FullyQualifiedName~LockPrompt'
dotnet run --project tests/Windows/NexaConnect.PosTouchTests/NexaConnect.PosTouchTests.csproj
```

Pure tests cover recovery guidance precedence, shift/cash prerequisites, explicit amount
provenance, no automatic replay and empty-menu states. The isolated Windows fixture checks
text/numeric field visibility at 1000×680, 1280×850 and 1536×864, pinned dock/keyboard
non-overlap, original action visibility, navigation, filter reset, busy disabling/progress,
native text insertion and lock privacy. It uses synthetic identity and temporary storage,
not live cashier data or financial commands. `-- --preview` renders synthetic layouts
under `.runstate`, including `pos-ux2-checkout-preview.png`.

The 28 October Windows-enabled unit run passed all 1,116 tests with no failures or skips.
The final Windows fixture passed text/numeric layouts at all three sizes, pinned amount/
action placement, guarded navigation, filter reset, busy disabling/progress and lock privacy.
It also checks the original Payment action, server total instead of an empty cart estimate,
uncertain-payment guidance and cancellation-recovery guidance without sending a command.
The existing fullscreen regression probe also passed staff placement/restoration/safety,
three customer layouts and three centered lock-panel layouts. Synthetic preview review
confirmed the search field and original action controls remain above the text keyboard.
These results do not certify physical touch, real keyboard/OIDC or financial UAT.

Source-bound candidate `dec17d72-fc2b-4114-9ebe-bbd1b2dcee6e` passed all 19 QR6 gate
checks: 1,116 unit tests, six architecture checks, 31 HTTP cases, 47 hosting checks,
22 candidate guards, 39 acceptance guards, the Media-profile runner, builds/frontend
checks/audits and 88 frontend tests. Browser suites passed 57 QR, eight day-close, eight
cutoff, 22 seal and seven settlement cases; seven owning cases passed with verified cleanup
and broker recovery. Unit/browser runs have no failures or skips; browser runs have no flaky
cases. Its protected `verification.json` records `productionVerified=false`.

An earlier attempt timed out loading the settlement browser's branch selector; the isolated
unchanged suite subsequently passed all seven cases. Another gate was cancelled before a
source correction. Neither attempt is relabeled as the accepted candidate above.
Matching package `1a52f0e3dd0f43b69e9b905dfb698e5e` is installed; its separate
retained-pilot activation evidence is recorded below.

Package/install and source-bound local pilot refresh use the existing
[same-scope procedure](QR7-Pilot.md#refreshing-source-for-the-same-qr-pilot-scope) and
[Windows terminal workflow](First-Draft-Windows-POS.md). Preserve the current branch,
original recovery, valid certificate identity and approved media; close POS before install.
Record fresh candidate/activation evidence without copying earlier phone acceptance.

### Local activation — 28 October 2026

After explicit user confirmation that POS was closed, release
`1a52f0e3dd0f43b69e9b905dfb698e5e` was installed into the existing current-user
DraftTerminal. The retained stack `c30b628ab23141f389119daa565f894f` was rebuilt
without reset and reached Ready with all 12 services. Strict candidate revalidation
passed after publication. Fresh pilot `b263970db3a547ec8375061e4e8c5087` configured
and started at the unchanged `https://192.168.0.191:8443` origin using the unchanged
valid certificate identity. Live HTTPS diagnostics passed; phone observations remain
pending for this activation rather than inheriting earlier results.

All five recorded recovery files matched their pre-update fingerprints before launch;
terminal scope, provisioned references and service URLs were unchanged. A
read-only cashier gallery request returned HTTP 200 with two staff slides, two customer
slides and three checksum-valid PNGs. No orders, payments or gallery selections were
submitted as part of activation.

The exact installed executable reopened in its retained locked state. Native inspection
showed the centered authentication panel and confirmed workflow amounts/actions were
hidden. The agent did not authenticate or verify a signed-in cashier workflow. Original
cashier sign-in, live touch/keyboard use and physical acceptance remain pending below.

Pending operator acceptance:

- Check the text/numeric keyboard, pinned amount/actions and scrolling on the purchased
  staff display at actual resolution/DPI; verify customer privacy on the second display.
- Complete an ordinary sale and accepted QR collection; confirm quote review, tender,
  original payment confirmation and Paid receipt behavior are unchanged.
- Reopen pending checkout/uncertain payment recovery and verify the next-step button
  navigates to the original work without placing, collecting or paying automatically.
- Test opening/counting/closing cash with touch only, busy/network-failure messages,
  lock/resume and scanner input. Actual financial UAT, OIDC, scanner and hardware acceptance
  remain pending until observed; automated fixture results do not replace them.

Card-reader sign-in, continuous locked screensaver playback, new payment/refund workflows
and broader professional UI redesign remain separate work.
