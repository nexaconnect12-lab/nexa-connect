# QR5 Windows cashier collection

QR5 implements original accepted QR Order collection and cash/manual PromptPay payment.
Hosted deployment, real phones, clean-source remote CI, signing and physical/PDF acceptance
remain pending. No commit/push/deployment is implied by local verification.

1. Complete [QR4 setup](QR4-Acceptance.md). Apply Order22 before current binaries, even if
   QR flags remain disabled. Verify [schema/grants/recovery](../Database/QR-Collection.md).
2. Deploy matching POS service and Windows POS binaries. Existing exact Order-workload POS
   admission adds optional `expectedShiftId`/`expectedStoreId`; old POS services must not be
   used because they cannot enforce QR custody's exact shift. Order rejects a missing or
   mismatched admittedShiftId/admittedStoreId response proof. No new credentials or role.
3. Keep all prior QR prerequisites and set Order `QrCollection__Enabled=true` only on an
   isolated configured host. Default is false; pilot artifacts remain disabled pending
   hosted readiness. Keep collection available while retained collection work exists.
4. Sign in to Windows POS, open your shift and clear any current cart/recovery. In **QR
   collection**, enter the guest's `QR-...` reference or original Order UUID and select
   **Load original QR order**. It checks scope/readiness without side effects, then retains
   original operation before claiming. Pending/rejected/paid/other-branch references cannot
   start another sale. Full UUID input is supported; table-wide lookup/combined bills are not.
5. Review original table/items/pricing and choose cash or manual PromptPay in Payment.
   Cash requires your open THB drawer. PromptPay requires the configured local QR image,
   explicit receipt verification and reference. Confirm only the exact received amount.
   Accepted QR4 Orders remain unpaid until this action succeeds.
6. After timeout, lock/resume or restart, authenticate as the original cashier and use
   **Verify original QR collection** or **Verify payment**. Retained UUID/settlement/tender
   fields must remain unchanged. Never use Send order, recreate the bill, collect again,
   clear SQLite or switch Windows profiles to bypass recovery. Definite invalid references
   fail before local custody is retained; uncertain claim/payment retains recovery.
7. Paid loads the same immutable receipt through authorized staff lookup; Windows PDF
   printing/reprinting preserves Paid. Guest status refresh displays paid and its safe
   original receipt while the original visit is live. Customer display clears details.
   Receipt/print failure does not authorize another payment. Existing POS and Reporting
   consumers must be enabled; broker publication is not proof of downstream projection.

Custody has no automatic transfer/release. If the original terminal/profile/cashier/shift
cannot resume, preserve all evidence and reconcile; no new operational override is added.
Remote shift closure/revocation after admission can race a payment commit because admission
remains a point-in-time observation. Existing financial guards and historical projection/
late-work custody apply. See [contract/telemetry](../API/QR-Collection.md).

## Verification

Run the full unit suite (Windows DPAPI acceptance opt-in where supported), QR HTTP/transport
and architecture tests, then `./scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure`.
The four-case owning runner now includes QR5 within the comprehensive QR acceptance case:
full Order/Inventory/Kitchen/POS/Reporting schemas, live POS-owned admission, immutable custody,
concurrent claim/payment replay, wrong cashier/shift/scope, revocation, original receipt,
cash and manual PromptPay projections, sale Reporting deduplication and downgrade guards.
Restaurant/Catalog/Authorization ports and clock-crossing recovery remain synthetic fixture
inputs; no retained database, actual provider, hosted broker or device is certified.

Frontend type/unit/build and QR Chromium/WebKit/desktop browser gates cover reference-to-paid
receipt rendering. Windows builds verify compilation; interactive WPF/OIDC/monitor/print and
physical iOS Safari/Android Chrome acceptance remain separately pending under QR6.

## Local verification record — 24 October 2026

| Gate | Observed result |
| --- | --- |
| Full .NET unit suite, current-user Windows DPAPI enabled | 991 passed, none skipped |
| QR HTTP/transport integration selection | 22 passed, none skipped |
| Architecture tests | 6 passed |
| Owning disposable PostgreSQL QR runner | 4 passed; cleanup verified |
| Frontend type check and unit suite | Passed; 76 tests passed |
| Both production portal builds | Passed; existing large-bundle advisory remains |
| Chromium/WebKit phone emulation and desktop QR matrix | 54 passed |
| Pilot hosting/configuration source checks | 47 passed |
| Windows POS build | Passed, zero warnings/errors |
| Full npm dependency audit | Failed: one existing high-severity Zod advisory |

The final PostgreSQL receipt is
`.runstate/qr-entry/3e3095dfcb314e0ca85d7563dd7b0e98/verification.json`:
four cases, `passed=true`, `cleanupVerified=true`, `productionVerified=false`.
Within its comprehensive owning acceptance case, twelve concurrent claims and twelve
concurrent payment attempts retain one original binding/settlement; reused settlement
identity on another Order conflicts without custody. Collection audit-outbox failure
rolls back its binding. Restart-style exact payment replay after shift closure preserves
receipt/cash/sale identity. Cash produces one movement; manual PromptPay on a new drawerless
shift produces no additional cash movement. Reporting receives original sale evidence
directly in the fixture; this does not test actual broker delivery or production readiness.

`npm run audit:all` reported existing `zod@3.23.8` under
[GHSA-mrhx-c7xc-m3w5](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5), affecting
versions below4.6.6. npm suggested4.6.7 as a breaking upgrade. No dependency or lockfile
was changed in QR5. A reviewed shared validation-library migration and financial/UI
regression are required before QR6 release readiness; the dependency/security gate is
not passed. Earlier QR4 zero-vulnerability evidence remains dated historical evidence.

Documentation updates cover the QR collection API/schema/operations and ADR048, current
QR1–QR4 compatibility notes, manual settlement/receipt/admission contracts, identity,
deployment configuration, roadmaps and affected component READMEs. Root README,
`AI/architecture/project_overview.md`, canonical Project Architecture and Restaurant POS
Architecture changed to describe original collection custody and safe receipt disclosure.
The documentation-maintainer audit independently reviewed these against implementation.
