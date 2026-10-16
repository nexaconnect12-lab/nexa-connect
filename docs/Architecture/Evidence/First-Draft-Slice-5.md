# First-draft slice 5 evidence

Verified locally on 15 October 2026 from base
`0be60f23ee9f9997b29b62250e95003451b49f85`, initially clean on Windows.
Implementation and evidence are uncommitted. No release/CI, production, real transfer,
physical printing or real next-day staff acceptance is claimed.

Implemented [manager acceptance and operating guidance](../../Deployment/First-Draft-Manager.md),
normal-clock mixed-tender/drawer/review evidence, Singapore historical acceptance and
actual settlement CSV proof. Corrected four Reporting entry filters to admit the draft's
`customer-user` identities while preserving live tenant/product decisions. Updated
Order-to-POS test-host configuration for terminal admission. No schema change, new grant,
production clock override, manual refund or offline finalization.

## Verification

- Unit: **833 passed, zero skipped**, including Windows DPAPI; isolated artifacts under
  `.runstate/slice5-build`, log `.runstate/slice5-unit.log`.
- Architecture: **6 passed, zero skipped**, log `.runstate/slice5-architecture.log`.
- PowerShell guards: **19 manager**, **32 lifecycle**, **18 terminal** checks passed.
- Node cutoff settings/reporter: **5 passed**; modified browser scripts passed syntax checks.
- Normal-clock run `9b394cec955c4f6e93e3ce418910760c`: **7/7**, cleanup verified.
  Fresh composite child `6effa8439e994c33a31b4a04035c4c26` also passed **7/7**, cleanup verified.
  Receipts: `.runstate/first-draft/<run-id>/checkout-verification.json`.
  Real cash/fictional PromptPay checkout, recovery, owning counts, manager/accountant reads,
  cashier denial, cash-only drawer attribution, stale closure, concurrent/replayed variance
  review and unchanged original sales/refund evidence passed. No financial transitions seeded.
- Combined retry `d4a33bfcd315460cb03d88a9bd19c476`: **passed**. Its normal-clock
  child `d8cd04d159ad425fa4c09a3ba387a34d` passed **7/7**. Historical Singapore child
  `35a0652a19524143b8806d1d5f405284` passed **18/18 distinct browser cases** with no
  skips/retries and **6/6 Authorization persistence cases**. Both cleanup receipts are
  true and both source revisions match the base above. Aggregate receipt:
  `.runstate/first-draft-manager/d4a33bfcd315460cb03d88a9bd19c476/verification.json`;
  historical receipt: `.runstate/cashier-day-cutoff/35a0652a19524143b8806d1d5f405284/verification.json`.
  Browser summary is under `src/Frontend/test-results/cashier-day-cutoff-live/<run-id>/summary.json`.
  The corrected main-branch revocation, denied scope, actual UI CSV download, source outage,
  delayed publication and recovery passed. Failed earlier attempts remain failed evidence.

Historical run `6b9a0b07fef44aaeac2668cae3e3670c` passed 11/18 browser cases before
Slice 4's terminal admission correctly rejected the old fixture's second sale against a
closed shift/drawer. The fixture now opens a real second shift/drawer at a later historical
hour and closes/reviews it through owning APIs; two drawers, two movements and three review
history entries are verified. Production admission was not weakened.

Composite `e34fadc6877949758fab9ba8a070ab79` correctly failed after historical child
`26defae51c9a4a8a8b9e54c46c7e0b22` passed 17/18 cases and all six Authorization
persistence cases. The last CSV case reached accountant revocation after adding the
correction-test branch: the old fixture updated two grants while requiring exactly one.
The fixture now restricts accountant read revocation/restoration to the main test branch.
Both failed historical runs verified cleanup and remain failed evidence.

Builds during live acceptance passed; the existing frontend bundle-size advisory remains.
The new live CSV case exports the original settlement with zero correction items; the
existing separate correction-branch case verifies a nonzero late-cash posting/projection.
It does not establish a live CSV download containing nonzero correction detail. Unit
coverage of correction CSV semantics is separate from these executed browser observations.
Local private child logs, fixtures and browser diagnostics are not distributable evidence.
Only generated projects and retained child process handles are cleaned up; unrelated
projects and the user's retained Windows recovery are untouched.

## Documentation and remaining acceptance

Updated the root README; AI project overview; system and restaurant architectures;
November phase plan; manager guide and this evidence; draft-stack, cutoff-acceptance and
late-correction runbooks; customer Reporting, EndOfDay, cutoff, seal and settlement-export
API documents; Identity client matrix; Reporting, Frontend, financial acceptance tool,
first-draft browser and cashier Docker READMEs.

The overview and both canonical architectures describe the current acceptance coverage
and role entry behavior. Database documentation and ADR-036 were reviewed without changes:
persistence ownership, schema and the existing clock-isolation architecture are unchanged;
no new durable architectural decision requires an ADR.

The required documentation-maintainer audit and followup passed, including the final
branch-scoped fixture correction and manager instructions. Final `git diff --check` passed.

The historical calendar is distinct from the **pending real next-day solo drill**.
**Interactive Windows/PDF acceptance remains pending** from Slice 4. The cash-close report
still needs the configured Store UUID. Automation does not establish real transfers,
manual-tender refunds, offline operation or fiscal certification. Release hardening and UAT
remain slices 6–7.
