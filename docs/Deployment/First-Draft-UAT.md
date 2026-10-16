# First-draft acceptance and handover (slice 7)

Use one Windows PC, Microsoft Print to PDF, synthetic THB transactions and
`Asia/Singapore`. One person tests four separate identities. This guide and the local
recording tool support acceptance; actual Windows/PDF and next-day observations are
still required. The target remains 30 November 2026. Reserve one working day plus the
following Singapore morning for the drill, and time before 30 November for fixes and
retesting. An unresolved blocker prevents acceptance and requires a revised test date.

## Prepare the exact candidate

1. Review and commit the intended candidate. Run the [release gate](First-Draft-Release.md)
   from that clean checkout and retain successful CI for the same commit. The gate now
   includes `test-first-draft-uat-guards.ps1`. Earlier Slice 6 receipts describe earlier
   executable inputs and cannot satisfy the new candidate. No CI dispatch is automatic.
2. Start/provision a retained [owned draft](First-Draft-Test-Stack.md), following the
   [provisioning guide](First-Draft-Provisioning.md). Keep its RunId. The disposable
   gate's Reset datasets and their realm configurations are retired, not a user install.
   For both tenders, start with `-PaymentMethod promptpay_manual -QrImagePath` pointing
   to an existing controlled fictional test QR image. The POS tender selector supports
   Cash as well as PromptPay; the configured method selects the default. Open the cash
   session before choosing Cash. Keep that configuration fixed for both tests.
   Keep this environment through the real next-day test. Check live readiness; a retained
   `Ready` marker alone does not prove dependencies are running.
3. [Package and install](First-Draft-Windows-POS.md) that draft's Windows POS. Packaging
   now records `sourceInputHash` and refuses changed inputs during publication. Older
   packages remain installable under the existing integrity checks, but cannot bind to
   new UAT records without the source hash. The runtime remains framework-dependent
   win-x64, requiring .NET 10 Windows Desktop. Installation, callback registration and
   launch are explicit operator steps. Preserve the protected recovery profile; a scope
   conflict requires investigation, never deleting it to make a new draft work.
4. Create and bind a session using the printed IDs, without angle brackets:

```powershell
./scripts/first-draft-uat.ps1 -Action New
./scripts/first-draft-uat.ps1 -Action Bind -UatId <uat-id> -RunId <retained-run-id> -TerminalReleaseId <package-id> -ReleaseRunId <release-gate-id>
./scripts/first-draft-uat.ps1 -Action Report -UatId <uat-id>
```

`New` works before packaging and creates all 16 cases pending. `Bind` requires a
provisioned owned Ready run, a hash-verified package matching retained POS scope, and a
successful local release receipt. HEAD, dirty flag and executable input hash must match
all three. Binding is immutable. Later commands revalidate package inventory, binding
file hashes, source identity and draft state. Source/build/scope changes require a new
session and retest. Hashes detect accidental drift; editable local metadata is not a
signature or independent certification. Backend running-binary identity and installed
active POS selection must be checked in `setup`; binding does not attest them.

Do not change source while testing. Start/Restart with a rebuild from the frozen checkout
when retained backend binaries might predate it; do not use `-NoBuild` to admit stale code.
No UAT command starts a backend, collects money, launches POS or reads login secrets.

## Roles and solo execution

| Identity | Use | Expected boundary |
| --- | --- | --- |
| `draft-cashier` | Windows POS, shift/drawer, checkout, receipt | Provisioned branch/terminal only |
| `draft-kitchen-operator` | Kitchen browser profile | Provisioned branch/station execution |
| `draft-store-manager` | Separate manager browser profile | Scoped cash review and day close |
| `draft-accountant` | Separate accountant browser profile | Financial reads; mutations denied |

Retrieve passwords only through `get-first-draft-login.ps1` locally, outside transcripts.
Use separate browser profiles, not tabs sharing cookies. Follow the existing automated
contention and cross-tenant denial fixtures from the release gate; do not create another
operating branch or grant privileges merely to test a denial. Inspect the exact-candidate
receipt for those measured results, then record your review as an operator attestation.

## Staff script and expected results

Keep a private synthetic-order ledger with order/receipt/operation UUIDs, exact expected
prices and tender/change/variance. Reuse the same operation on ambiguous response loss.
The default seeded menu is rice 80, noodles 95 and tea 30 THB, with zero tax/service
charge; use the actual frozen configuration if altered. Do not invent expected values
after viewing an incorrect result. Run these cases in order; run `clean-ci` before handover.

| CaseId | Action and expected result |
| --- | --- |
| `setup` | Check live stack readiness, retained branch/menu, THB/Singapore and four profiles. Rebuild retained backend from frozen source. Compare installed `Status` release ID to bound package ID. Check .NET Desktop runtime, callback and PDF printer. All belong to this candidate; no incompatible retained recovery is discarded. |
| `cashier-session` | Sign in through PKCE; exercise configured idle lock, renewal, fresh sign-in and sign-out using the Windows guide. Locked/expired sessions cannot send protected commands; no token enters evidence. |
| `cash-shift` | Open shift/drawer with a recorded fictional float. Confirm correct store/terminal and repeat/recovery behavior; only one intended open result exists. |
| `cash-checkout` | Buy rice + tea: 110 THB under defaults, receive fictional 200, change 90. Verify one Paid receipt, one inventory effect and cash movement 110. Repeating the original request never recollects. |
| `promptpay-checkout` | Select PromptPay in the frozen QR-enabled package. Buy noodles for 95 THB, explicitly confirm a fictional transfer with a fictional reference. Exactly one Paid receipt/tender exists, no provider call or cash-drawer increase. Do not scan/pay a real bank QR or edit the installed package. |
| `kitchen` | In the separate Kitchen session, prepare/complete the corresponding order. State progresses legally, lines match and repeated commands create no duplicate work. |
| `cancel-conflict` | Cancel an unpaid placed order; verify Kitchen cancellation before inventory release. Review the exact-candidate coordinated conflict/replay gate. A Paid order cannot be cancelled into refunded cash or duplicate stock. |
| `receipt-pdf` | Print and reprint the Paid receipt through Windows to PDF. Open the saved files and inspect readable identity, items, currency, totals, tender/change and original receipt identity. Spool submission alone is insufficient. |
| `print-recovery` | Cancel the Windows print dialog and exercise the documented failed-print path. Paid remains Paid; retry is reprint, never another collection. Inspect the resulting saved PDF. |
| `restart-recovery` | Follow the Windows interruption checklist before/after send, including ambiguous response loss. Close/reopen without deleting recovery; inspect original order/receipt. Same operation IDs, no duplicate money/stock/Kitchen effect. Review automatic interruption evidence separately. |
| `cash-variance` | Count fictional drawer cash, deliberately record a known variance, close drawer then shift. Manager reviews current decision/history. Expected cash includes cash only; PromptPay is separate. Reporting can lag without changing source cash. |
| `accountant-denial` | Accountant can read scoped financial evidence; actual coordinated mutation attempts are denied and state unchanged. Review exact-candidate live-revocation/contention evidence; hidden buttons alone are insufficient. |
| `tenant-denial` | Review automated real-OIDC foreign-scope/tenant mutation denial from the same release candidate. No financial state changes. Record this as review of measured evidence, not a new manual request you never sent. |
| `next-day` | Start on a recorded Singapore business date; return after actual midnight. Follow the manager guide: resolve blockers, fresh capture/cutoff, seal, reviewed approval, prepare and durable finalization, load progress/receipt. Restart and reread. Stale versions block; immutable results survive; no clock changes, SQL backdating or historical fixture substitutes. |
| `ledger-csv` | On the settled date compare immutable receipt, Reporting ledger and bounded CSV. Original sales/tenders agree; correction detail stays separate and is not added twice. Missing delivery/proof blocks readiness. Preserve CSV privately. |
| `clean-ci` | Inspect successful required CI from a clean checkout of this exact SHA, including executed case counts and cleanup. Index URL/run IDs privately. Earlier base-commit CI or skipped tests do not qualify. |

For full commands, screens and recovery steps use the [Windows checklist](First-Draft-Windows-POS.md),
[checkout guide](First-Draft-Checkout.md) and [manager guide](First-Draft-Manager.md).
Every required case must pass for the bound package. Missing test QR configuration is a
setup blocker; resolve it before freezing the package rather than editing it mid-test.

## Record observations and defects

Generate a private evidence reference with `[Guid]::NewGuid().ToString('N')`. Index its
sanitized description, expected/observed synthetic values, timestamps and safe record
UUIDs in the protected session's `private-notes.md`. Attachments stay private and are
never automatically copied. Do not put passwords, tokens, payment details, unrestricted
personal data, raw logs or protected POS recovery into shared reports. UUID references
are indexes, not evidence checksums. Review any material manually before sharing it.

```powershell
./scripts/first-draft-uat.ps1 -Action Record -UatId <uat-id> -CaseId receipt-pdf -Status passed -EvidenceId <evidence-uuid> -ConfirmObserved
./scripts/first-draft-uat.ps1 -Action Record -UatId <uat-id> -CaseId next-day -Status passed -EvidenceId <evidence-uuid> -BusinessDate 2026-11-02 -StartedAtUtc '2026-11-02T02:00:00.0000000+00:00' -ConfirmObserved
./scripts/first-draft-uat.ps1 -Action Defect -UatId <uat-id> -CaseId cash-checkout -Category financial -Severity blocker
./scripts/first-draft-uat.ps1 -Action Resolve -UatId <uat-id> -DefectId <defect-id> -ConfirmObserved
./scripts/first-draft-uat.ps1 -Action Report -UatId <uat-id>
```

The next-day dates above are an example, not a completed drill. Record your actual
start time in UTC round-trip format. The tool stamps observation time itself and rejects
future dates, reversed intervals or a result that does not cross actual Singapore
midnight. It cannot prove you operated the UI; this remains your explicit attestation.
`failed` and `blocked` also require `-ConfirmObserved` and an evidence UUID. `pending`
clears a case's current observation. Failed/blocked observations can be recorded before
binding; passed observations cannot. Records use exclusive session locks and atomic saves.

Financial, security, data-loss and unrecoverable defects always block, regardless of
severity. High/blocker usability defects block; minor usability requests appear separately
in `minorBacklog`. Resolve only after checking the remedy, then record a later passing
retest of the affected case. Code fixes change candidate identity and require a new
session; index the prior defect in private notes and rerun acceptance for the replacement.
No waiver or automatic acceptance exists.

## Handover and support

The owner-only `.runstate/first-draft-uat/<id>` contains `record.json`, `report.json` and
private evidence notes. Report output includes source SHA/hash, exact run/package/release
IDs, each case's current status/evidence UUID/time, blockers and minor backlog. It reads
no notes or attachments. Results are labeled operator-attested; the bound local release
receipt retains measured automation. The original package is at
`.runstate/first-draft/<run-id>/terminal-packages/<package-id>` with file hashes; the
release receipt and binary inventory remain in `.runstate/first-draft-release/<release-id>`.
Deliver that verified package with these linked runbooks through the intended private
test channel. Do not distribute secrets, the entire `.runstate` tree or a retired realm.

`readyForUserDecision` is true only with every required case passed, a clean unchanged
candidate, valid current binding and no unresolved blocker or missing retest. The tool
always writes `acceptance: pending-user-decision`; the user records the final decision
and date privately. A malformed record fails closed; drift produces a blocked report.

For ambiguous checkout, inspect the original order and resume the same operation; never
collect again to clear an error. For unavailable dependencies, stop testing mutations,
check live readiness and bounded correlation-ID queries in component runbooks, then
resume retained work. Stop preserves volumes; Reset destroys the owned backend and
credentials and invalidates its UAT binding. Neither replaces POS recovery procedures.
Use the [isolated backup/restore guide](First-Draft-Release.md), never restore over the
sole acceptance environment or copy DPAPI state to a different account/device.

Carry minor usability requests into a prioritized next-iteration backlog. Keep manual
refunds, provider/fiscal integration, SGD localization, multi-branch expansion, physical
printer/drawer/scanner certification and full offline operation explicitly excluded.
Manual refunds have no supported financial workflow: escalate; negative cash movements
or late corrections are not substitutes. Local alerting has no external notification
receiver. First complete blocked acceptance and exact-candidate CI, then prioritize
the user's observed usability issues before expanding scope.
