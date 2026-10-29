# Branch-approved manual PromptPay QR

## Status and prerequisites

Implemented and activated in the retained local stack/POS on 29 October 2026.
Actual merchant PNG upload, independent payee/bank verification and native payment
acceptance remain pending. The portal steps below are available in the refreshed local setup.

Final local verification passed on 29 October 2026: candidate
409f198b-ce6a-48dc-bd95-727c7e79332d, 1,270 unit tests, 78 HTTP tests,
66 QR browser cases, nine owning PostgreSQL cases with cleanup/broker recovery, WPF,
frontend/security and profile gates. Seven separate gallery browser cases also passed.
The documentation-maintainer audit cleared with no unresolved findings. Matching opt-in
release 3156f8a04c2e46f49b38fdc8d580fbdb is installed, with pilot
58ba02a9b9a94baf90e9f588590cb63a and a responsive native window. All 12 services are Ready;
eight protected recovery/preference/gallery files, custody scope, service URLs, reference
data and all three certificate files were preserved. Windows trusted TLS and guest 200/staff 404
checks passed; the three new BFF/Media/Restaurant protected entry points reject anonymous
requests with 401. Omise remains enabled beside the cash_manual default. No merchant image
was uploaded and no order/payment was submitted by the agent. Phone/payment acceptance is
not inferred from startup. Evidence is retained privately in .runstate/promptpay-activation.json.
Prior diagnostic runs remain historical and are not relabelled as the final candidate.

Customer Portal supports upload and explicit approval of one PNG per active THB branch.
Restaurant owns selection, Media owns bytes and POS displays a short-lived delivery copy.
This is manually verified PromptPay, separate from Omise sandbox, customer-order table QR
and screensaver galleries. No schema migration or new permission grant is required.
Actual merchant PNG upload, bank/payee verification and native payment UAT remain operator tasks.

Use static PNG only, at most 2 MiB, 64–2048 pixels on each side. Preserve the full white
border. SVG, JPEG, WebP and animated PNG are unavailable for this purpose. Server delivery
decodes and losslessly re-encodes PNG at its original dimensions, stripping metadata without
cropping or rescaling. File safety does not decode the payment QR or verify its payee,
currency or bank account. The approving manager must check those details independently.

## Upload and approve

1. Sign in to the staff Customer Portal as a manager with live branch
   `restaurant.configuration.manage`. The retained local portal is
   `https://127.0.0.1:63417/#configuration`; use the existing `draft-store-manager` account.
2. Open **Configuration → PromptPay QR — manually verified**, select the intended branch
   and click **Load branch QR settings**.
3. Choose the PNG, inspect its preview and click **Upload PromptPay PNG**. Upload completion
   does not change the branch selection or settle an order.
4. Independently verify merchant/payee, currency and full QR border. Check the explicit
   approval box, then click **Approve branch PromptPay QR**.
5. In native POS select manual PromptPay and **Refresh branch QR**. Confirm the correct
   merchant and exact payment amount before asking for a transfer. Enter the received
   receipt/bank reference (1–64 characters), check receipt verification and confirm once.
6. To withdraw it, choose **Remove branch QR selection**, check approval and save. Removal
   clears selection, not storage. Reload after conflicts or uncertain saves; do not retry blind.

Cashiers need live branch `catalog.menu.read` to read the approved QR; they do not need
organization-wide Media read/manage rights. Managers require configuration permission at
upload start, completion, preview, ready check and approval. Guest and gallery-reader
capabilities cannot manage or read this bank-QR delivery API.

## Activation and failure behavior

`Pos:UseBranchPromptPayQr` defaults to false; legacy `PromptPayQrImagePath` remains supported
in that profile. Opt-in profiles require `Services:MediaApi` and matching new Restaurant,
Media and Customer BFF binaries. The local stopped stack can opt in explicitly:

```powershell
pwsh -NoProfile -File scripts/first-draft-stack.ps1 -Action Start -RunId <retained-run-id> -EnableBranchPromptPayQr -ConfirmTestResources
```

Enable Media first if not already enabled; validation uses `-Action Validate -EnableMedia
-EnableBranchPromptPayQr`. Build-enabled Start is required; stop owned services and close
POS before refreshing. Existing enabled Omise state/secrets inherit unchanged. Do not reset
the stack, change custody IDs, delete recovery or reuse old source-bound candidate evidence.
The packaged client contains the flag, never bank credentials. Repackage/install the matching
profile while preserving protected orders, shifts, preferences and recovery.

POS refreshes every 30 seconds while signed in on manual PromptPay Payment, with a maximum
60-second image lease and a fresh read before a new confirmation. Refresh/expiry clears the
display while unavailable. Replacement clears new receipt verification. There is no disk
cache, offline bank-image fallback or locked-gallery token for this image. New confirmations
remain disabled without a current image, original order/shift and required receipt proof;
inline guidance explains the missing prerequisite. Reference/checkbox edits update readiness.

Approval and delivery checks cross services and are not a distributed transaction. A change
after the last server check can leave a previously issued image visible until its maximum
60-second lease expires. New confirmation refreshes current authority/selection; there is
no instantaneous withdrawal or payee-verification guarantee.

An uncertain original payment retains its entire saved request, amount, method, reference,
receipt proof and idempotency key. It can be verified without a current image and must not
trigger another transfer. Lock, cashier change, order change or concurrent command cannot
reuse new proof. Cash and sandbox-card recovery keep their existing workflows.

## Verification and troubleshooting

Run the complete `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` gate on Windows.
It includes PromptPay policy, HTTP authorization/CSRF, PostgreSQL concurrency/rollback,
WPF readiness/recovery, profile guards and nine browser scenarios across phone Chromium,
phone WebKit and desktop. These synthetic tests do not certify an actual bank QR or payment.

If the branch list is empty, check active THB hierarchy and live configuration permission.
If upload fails, check Media/storage/scanner availability, PNG bounds, signed-upload origin
and quota; do not paste signed URLs into logs. Reload after a branch-version conflict.
If POS is missing the refresh button, verify the installed opt-in profile and matching release.

Services `nexaconnect-customer-bff`, `nexaconnect-restaurant` and `nexaconnect-media` retain
the shared correlation foundation. Filter structured events by `correlationId` and messages
containing `PromptPay`; inspect status/denial/dependency categories. Never log image bodies,
tokens, cookies, signed URLs, bank references or provider secrets.

See [API](../API/Branch-PromptPay-QR.md), [persistence](../Database/Branch-PromptPay-QR.md)
and [ADR-054](../Architecture/Decisions/ADR-054-branch-promptpay-qr.md).
