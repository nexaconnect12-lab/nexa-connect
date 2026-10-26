# QR9 — real-device pilot acceptance and defect follow-up

QR9 implements read-only TLS/network diagnostics and structured operator acceptance
records on top of QR7's existing candidate/activation and append-only observations.
It does not execute guest submissions, staff decisions, payments or money transfers.
Actual phone, cashier, customer-display and saved-PDF observations remain required.

## Prepare the exact candidate

Run `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure`, then configure/start a fresh
QR7 activation for that candidate using [QR7](QR7-Pilot.md). For a retained QR pilot,
preserve its branch/terminal scope, business volumes, original POS recovery and exact
certificate files during the documented source-refresh procedure. No reset is needed.
The complete gate now includes 39 QR9 evidence guards and 21 candidate guards in addition
to existing application/browser/owning-service verification. These guards are synthetic
tests, not actual-device acceptance.

Use the printed pilot ID in place of `<pilot-id>`:

```powershell
./scripts/qr-acceptance.ps1 -Action Diagnose -PilotId <pilot-id>
./scripts/qr-acceptance.ps1 -Action Prepare -PilotId <pilot-id>
./scripts/qr-acceptance.ps1 -Action Report -PilotId <pilot-id>
```

`Diagnose` validates the immutable QR7 plan/file binding, exclusive private-address DNS,
exact SAN, current server-authentication chain to the distributed CA, default Windows TLS
trust and live leaf fingerprint. It then checks `/order` returns 200 and a staff route
returns 404. It writes a unique protected diagnostic with public certificate hashes,
validity, bounded status values and candidate/activation bindings. It never logs HTTP
bodies, QR tokens, cookies, credentials or exception text, installs trust, changes router/
firewall settings, or bypasses TLS. `localChecksPassed` covers only those local checks;
candidate prerequisites and device trust remain separate. A stale candidate is reported
by Diagnose; Prepare/Record/Report require a fresh matching configured candidate.

`Prepare` creates `.runstate/qr7/<pilot-id>/qr9/session.json` and
24 JSON templates, one per device/case. Repeating Prepare resumes a partial pack for the
same binding, preserves existing template UUIDs/facts and fills only missing templates.
It refuses a different activation. All new template checks start `null`, with result
`blocked`. Preparing a template records no observation and grants no acceptance.

## Safari certificate investigation

1. Check the phone's date/time and open the exact configured `/order` URL directly.
   An old `.190` QR or different hostname does not match the `.191` certificate.
2. Compare the diagnostic's public root SHA256 with the public `client-ca.cer` you
   installed. Distribute only that public root; never send the private key/pilot folder.
3. On iPhone, installing a profile and enabling TLS trust are separate steps. Follow
   Settings → General → About → Certificate Trust Settings and enable full trust for
   the exact installed root. See [Apple's current instructions](https://support.apple.com/en-sg/102390).
4. Close/reopen Safari and retry the exact origin, then scan a newly displayed table QR.
   A Windows pass does not prove iOS trust. If Safari still warns, stop and record the
   `camera-scan-https` case as failed/blocked with `noCertificateWarning:false`; retain
   the exact error privately for investigation. Do not bypass the warning or turn off TLS.

The existing pilot root is `NexaConnect QR7 c7838516cddc4ad7aaa6ba01b1fd37cf`, public
SHA256 `714821cbf6609aeb1d56484d0975a7ebfc02842c6da3dc0a8065b1d76cf69be4`.
Its current leaf expires 8 November 2026 at 16:40 Singapore time. After expiry, prepare
and trust a fresh certificate/activation; never extend validity by editing evidence.
On 26 October 2026 the user confirmed that both devices load the exact guest origin
without a certificate warning. This resolves the reported warning for that direct URL;
camera scanning, table context, fragment removal and complete workflow cases still require
their own observations. The 25 October records below retain their original pending state.

## Execute and record observations

Follow all 12 cases in [the phone/cashier checklist](QR6-Phone-Test.md) on both iOS Safari
and Android Chrome. Start with HTTPS/table entry, then independence/menu/pricing, original
submission/recovery, staff acceptance/Kitchen, cash and manual PromptPay on separate
Orders, lock/resume/network interruptions, revocation, original receipt/PDF and reconciliation.
Observe actual owning effects before checking a box; do not infer a ticket, payment,
inventory deduction or reporting projection merely from a success message.

Edit a copy of the relevant generated template. Preserve its UUID, device and case ID.
Set each bounded named check to `true`, `false` or `null` (not observed). Set result to
`passed`, `failed` or `blocked`. A pass requires every check true; failed/blocked requires
at least one false/null. Do not add notes, payment amounts/references, request bodies,
screenshots, credentials, QR secrets or arbitrary fields. The checklist is an explicit
operator attestation, not an automated verification of those business effects.

```powershell
./scripts/qr-acceptance.ps1 -Action Record -PilotId <pilot-id> `
  -ObservationFile <completed-template.json> -ConfirmObserved
./scripts/qr-acceptance.ps1 -Action Report -PilotId <pilot-id>
```

The recorder validates freshness/bindings, persists immutable bounded evidence and links
its UUID to the existing QR7 observation. Retrying the identical file resumes an interrupted
write or returns replay without duplicating that observation. Reusing the same evidence ID
with changed facts fails. For an actual retest, use a new UUID; latest QR7 observation
wins, including a later failure. Reports count only linked, validated QR9 checklists;
legacy QR7 passes without such evidence remain pending in QR9. All 24 passes set
`operatorCasesComplete:true`, while `releaseReady` and `productionVerified` remain false.

Stop on a defect, retain the original order/payment recovery, fix it within owning service
boundaries and prepare a new matching candidate/activation before retesting. Do not recreate
an order, transfer collection custody, delete recovery or mark a case passed to unblock it.
No public deployment, remote CI, signed-installer, physical-hardware or release-freeze
acceptance is supplied by this tool. Dedicated-server work still awaits specifications.

## Prepared local pilot — 25 October 2026

The complete gate passed for candidate `644e4f13-592c-4203-b02a-18a7fa68f34c`, revision
`d4e5ce88d77f053afab7d3bfd84fdcc633533bc4`, dirty source, canonical non-Markdown hash
`623450430039dfba5b4808987ba1b241d8f5fe25597d14fdc96f6b89b6111317` (1,842 files).
Measured results: 1,043 unit, 6 architecture, 27 HTTP, 88 frontend unit, 57 QR browser,
45 financial browser, 47 hosting, 21 candidate and 39 acceptance guards; all six owning
journeys passed with cleanup and broker recovery. POS/frontend builds, type checks and
both dependency audits passed. Two negative PowerShell action/path checks also passed.

Retained stack `c30b628ab23141f389119daa565f894f` is Ready. POS release
`d656987d2e8b4af28bef4b327203428c` passed installed preflight (exit 0), then opened a
responsive Cashier window. Closed pilot recovery hashes matched after installation;
the original default profile's three recovery files remained byte-identical. Its legacy
package input binding separately matched current inputs; that digest scheme differs
from the canonical QR6 source inventory.

Activation `0b2b0dbf51754dc298e470cd2c4097a4` keeps the same branch scope, origin and exact
certificate files. Frozen device warnings retain the unresolved Safari report and pending
Android acceptance; carried installation attestations are not new device verification.
Diagnostics passed exact SAN/server-authentication chain, normal Windows trust, the exact
live leaf over TLS1.3, guest 200 and staff-route 404, with no candidate prerequisites.
The pack prepared successfully twice, retaining 24 templates. Its protected
`qr9-rollout-verification.json` and `diagnostics/qr9-*.json` record these bounded checks.
No actual-device observation was written: **0 passed, 24 pending**. QR9 actual acceptance
is incomplete, and release/production readiness remains false.

Start with the phone check at [the guest origin](https://192.168.0.191:8443/order), then
issue/scan the table QR from the authorized [staff portal](https://127.0.0.1:63417/#configuration).
Use [staff QR review](https://127.0.0.1:63417/#qr-requests),
[Kitchen](https://127.0.0.1:63417/kitchen) and POS Orders for the remaining workflow.
The loopback links are PC-only; phones use the guest origin. That activation's templates are under
`.runstate/qr7/0b2b0dbf51754dc298e470cd2c4097a4/qr9/`.

```powershell
$qr9 = Get-Content .runstate/qr9-current.json -Raw | ConvertFrom-Json
./scripts/qr-acceptance.ps1 -Action Report -PilotId $qr9.pilotId
```

This candidate's freshness expires after 24 hours. If source/configuration/artifacts,
certificate validity or prerequisites change, preserve recovery and prepare a fresh
matching candidate/activation before recording more observations.

The 26 October continuation found the same source content and compiled artifacts after
commit `1b155335f9c694d4a8e6a073000f2e0e98a5f595`, but revision/dirty metadata differed
from the earlier proof. That historical proof must not be edited or reused as current.
Use a fresh verified candidate/activation and the protected `qr9-current.json` pointer;
new device-case observations belong to that activation. Direct URL trust confirmation
alone does not pass the complete camera-scan case or any payment case.

Fresh candidate `df870ba5-bfa9-40e5-afbc-47f3f77174c4` passed on 26 October against
that committed revision (`dirty:true` after this guide update), with the same canonical
content hash and 1,842 files. All required counts remain passing: 1,043 unit, 6 architecture,
27 HTTP, 88 frontend unit, 57 QR browser, 45 financial browser, 47 hosting, 21 candidate,
39 acceptance and six owning cases with cleanup/broker recovery; builds and dependency
audits also passed. This refresh does not establish clean-source remote CI.

After the operator confirmed POS was closed, the same retained stack was rebuilt and
matching POS release `33e2b362c6074271b4b3b7174edf5c0d` installed. Closed-profile recovery
hashes matched, preflight returned 0 and the reopened Cashier window responded. The
original default profile's three recovery files remained unchanged.

Fresh activation `96651bc1c59440a38f62794e8633cd21` now binds the new candidate with the
same branch scope and exact certificate files. Normal Windows TLS/exact live leaf and
guest 200/staff 404 checks passed with no candidate prerequisites. At refresh it had 24
templates and zero complete device cases passed; the direct-URL confirmation was recorded
separately from the then-pending camera-scan case. `qr9-current.json` now identifies this
activation, while `.runstate/qr9-refresh-pending.json` retains `status:refresh-complete`.
Its protected `qr9-rollout-verification.json` records bounded refresh evidence.

The user subsequently confirmed the complete `camera-scan-https` case on both iPhone
Safari and Android Chrome: camera entry at the exact origin without a certificate warning,
correct restaurant/table, fragment removal and no staff sign-in. Both were recorded as
explicit operator observations in this activation, with evidence IDs
`14a06ded-e7f3-4d94-a160-ea73f2e4fddf` (iOS) and
`7bddc56f-f7b5-416c-85ba-c0e97ebf6e12` (Android). The report now has **2 passed, 22 pending**;
release/production readiness remains false. Separate guest histories and original receipt
isolation are the next checks; no submission, Kitchen or payment result is inferred.
