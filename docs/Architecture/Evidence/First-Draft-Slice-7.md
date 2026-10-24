# First-draft slice 7 implementation evidence

Implemented locally on Windows on 16 October 2026 from clean base
`4d9e4734adba222958cf1eccedaa07709a9d8a36`. Changes remain uncommitted.
The [acceptance and handover guide](../../Deployment/First-Draft-UAT.md) defines the
solo Windows/PDF drill, role matrix, expected outcomes, private evidence index,
recovery procedures, exclusions and next iteration priorities.

## Verification

- `test-first-draft-uat-guards.ps1`: 44 checks passed using synthetic metadata only.
  Covers missing/invalid cases, unbound passes, source drift and typed identities,
  retired/wrong-scope drafts, mismatched/legacy packages and release receipts,
  Singapore midnight/future timestamps, blocking categories, resolution/retest,
  minor backlog, safe report fields and final acceptance always pending the user.
- `test-first-draft-terminal-guards.ps1`: 18 checks passed.
- `test-first-draft-release-guards.ps1`: 31 checks passed.
- `test-first-draft-guards.ps1`: 32 lifecycle checks passed.
- `test-first-draft-manager-guards.ps1`: 19 checks passed.

The actual CLI smoke created session `2708b5f9bcbb4cd9881c0751ec0d315f`
against executable-input SHA-256
`504b8c79c7b84f558fab290888cb216ee05377c162d0b7647e68ea085e270078`
with the base commit above and `dirty: true`. `Report` returned all 16 cases pending,
18 blockers (unbound package, dirty candidate and 16 missing case results),
`readyForUserDecision: false` and `acceptance: pending-user-decision`. A passed
observation without `ConfirmObserved` was rejected; a confirmed passed observation
without binding was also rejected. The record hash stayed unchanged after both
rejections. No fabricated observation or acceptance result was saved. This pending
session demonstrates tooling behavior; it is not a bound user candidate or test result.

The release runner includes the new UAT guard suite. New terminal manifests bind the
same executable input hash and reject source changes during publication. This is local
metadata integrity, not signed provenance or independent attestation. No service/domain,
financial transition, schema, role or HTTP behavior changes in this slice.

## Remaining acceptance work

No installed user terminal, saved receipt PDF, actual next-day result, new successful
full release gate, clean-checkout CI for these changes or final acceptance is claimed.
The earlier Slice 6 gate remains evidence for its recorded inputs, not these new inputs.
The full backend/frontend/database/broker matrix is not repeated for local UAT tooling;
the final frozen candidate must rerun it before binding. No new package is created from
a retired test realm. A matching retained environment/package/release binding is pending.

The current readiness is **blocked pending candidate freeze and user execution**.
Prepare the final clean build and its CI, retain a QR-enabled one-branch environment,
install its verified package, then execute the 16 cases and real next-day drill. Fix and
retest observed blockers on the replacement candidate. Acceptance remains the user's
decision, targeted by 30 November; unresolved blockers require a revised date.

Both architecture summaries and root/planning/deployment documentation describe these
changes. Restaurant-POS architecture and ADR-005 were reviewed without change: bounded
contexts, financial invariants and offline scope are unchanged. No new ADR is needed
for local acceptance recording.
