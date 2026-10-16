# First-draft slice 6 evidence

Implemented and verified locally from initially clean base
`b2d0f308445eeab8ff9c261a7e1085fa11f5d76f` on Windows, 15–16 October 2026.
See the [release gate and recovery guide](../../Deployment/First-Draft-Release.md).

The complete local gate passed. Changes are uncommitted; no clean release, CI for these
working-tree changes, production acceptance, actual next-day drill or interactive
Windows/PDF acceptance is claimed.

## Successful candidate

Run **`95ec49bc75de48f491ba9ec9f0ea68d6`** passed on 16 October 2026.
Receipt: `.runstate/first-draft-release/95ec49bc75de48f491ba9ec9f0ea68d6/verification.json`.
Its `candidate.json` retains executable paths/hashes and base revision; input SHA-256
`b5020eac5ed6f602c9799419b5368f1e03fa8ee68eeb107fcd657dd497988112` stayed unchanged
through verification. `build-artifacts.json` records actual published binaries/assets.
The supplemental `runtime.json` records SDK 10.0.400, Node v24.19.0, npm 11.17.0,
PowerShell 7.6.5 and Docker server 29.6.2. Schema targets remain Platform 3,
Authorization 15, Restaurant 3, Catalog 4, Inventory 5, Kitchen 3, Order 19,
Payment 18, POS 23 and Reporting 22; application compatibility remains 0.38.0.

| Gate | Executed result |
| --- | --- |
| Unit including opted-in Windows DPAPI | 834 passed, zero skipped |
| Architecture | 6 passed, zero skipped |
| Anonymous Payment health HTTP | 2 passed, zero skipped |
| Frontend | TypeScript, 76 tests in 22 files, both portal builds passed |
| Dependencies | Fresh locked install and production/full audits: zero vulnerabilities |
| Script/browser guards | 31 release, 32 lifecycle, 18 terminal, 19 manager and 5 browser settings/reporter checks passed |
| Source/seal/PostgreSQL + Payment DB readiness | 123 passed, zero skipped; `88af37285fc44671af7cc63830847147` |
| Settlement Reporting PostgreSQL/RabbitMQ | 9 passed, zero skipped; `a651fd115ceb4e6480c81411971fd9a0` |
| Restricted settlement replay | 6 passed, zero skipped; `4b63b03596f642b184dbb854ea8405f8` |
| Cash-close portal | 5 unique browser + 6 Authorization persistence cases; `4f6f8155904a4700bca277378d8a1e06` |
| Normal-clock checkout/Kitchen/manager/restore/resume | All 9 checks true; `fb434689b6644474ade521be5cd87d84` |
| Historical Singapore cutoff/settlement/CSV | 18 unique browser + 6 Authorization persistence cases; `c24310f19db6461c85419dee83109781` |
| Alert rules | Cash-close Prometheus validation and synthetic evaluation passed; external receiver not verified |

All child receipts identify this source revision and verify cleanup. Browser gates had
no skips/retries. The final aggregate records `passed=true` and `inputUnchanged=true`.
Existing frontend bundle-size and dependency deprecation advisories remain nonblocking.

Restore **`6f0b4b1283f54aab9c15859b2d9fb0c5`** resides beneath the normal run's
`restore-drill/` directory. All **11 databases** have `dataAndSequencesMatch=true`,
`ownershipAndPermissionsMatch=true`, `schemaRestoreVerified=true`. Six configuration
files matched, same-account protected credential decryption succeeded, and clones were
removed with cleanup verified. The original graph resumed; fresh identities read retained
review/receipts and replayed the original settlement. Its source checkout cleanup passed.
This is original-archive DDL restoration, exact data/sequence comparison and ownership/ACL
statement comparison; independent CHECK semantic equivalence and byte-identical schema
dumps are not claimed. Cloned application startup, broker/Redis restoration and POS local
database portability remain unverified. No user's existing POS journal was copied/purged.
Private dumps, credential/key copies, browser diagnostics and raw service logs remain local;
only allowlisted receipts/counts belong in distributable evidence.

## Failed attempts and fixes

- `7b0dc38c3016404490197d30912d1e30`: Unit 833, Architecture 6 and health HTTP 2
  passed without skips. The new wrapper's PowerShell parameter shadowed the npm script
  variable; corrected parameter naming. The attempt remains failed.
- `e23fc0227ffc48c5b5db535a3a7f3d63`: the same .NET gates, frontend check, 76 tests,
  both portal builds, both dependency audits (zero vulnerabilities), 123 PostgreSQL,
  9 settlement Reporting and 6 restricted replay broker cases and alert fixtures passed.
  Its checkout `9f79e7a9bb464ec4ad3fa294ca267440` reached actual manager reconciliation,
  then the new restore comparison failed at its first database. Shell quoting retained
  pg_dump's per-dump random guard. PowerShell normalization now removes only random
  restriction guard lines; tests prove equivalent guards match and changed SQL differs.
  Restore `655595e324df4b15b9a48ca95c9d88e5` and outer checkout verified cleanup; failed.
- `71f5fde0effc4254b7dd2100b8fcd0a8`: Unit 833, Architecture 6, health 2 and frontend
  gates passed; PostgreSQL child `632285b8f2de489d98c86fc4cc383e12` failed two tests.
  At shortly after UTC midnight, existing fixtures moved creation time to tomorrow's
  boundary plus one hour, violating `updated_at_utc >= created_at_utc`. The seal and
  approval fixtures now use the exact exclusive day boundary, preserving next-day
  assertions at all execution hours. Production constraints are unchanged. Matrix
  cleanup verified; the failed run is not replaced by the earlier afternoon pass.
- `80321489319c4548b9c5741c28e71e4e`: Unit 834, Architecture 6, health 2, frontend 76,
  builds/audits, PostgreSQL 123, broker Reporting 9/replay 6 and cash-close browser 5 plus
  Authorization 6 passed. Normal child `9d7da04500894e6f99dfad7cc49a26a1` passed checkout,
  Kitchen/recovery and manager checks before restore `87e0260dda9445528e49f46cc469de69`
  failed Kitchen after five earlier databases matched. Private Kitchen SQL dumps each
  have 783 lines; the sole differing statement is redundant AND parentheses in
  `ck_kitchen_audit_actor`, normalized by PostgreSQL during restore. No data difference
  was observed in those dumps. The verifier now compares data/sequences and owner/grant
  statements separately, and requires stop-on-error original-archive schema restoration.
  It does not remove CHECK expressions or claim independent semantic verification of
  them. All 31 release guards pass, including changed data, owner/grant and absent
  authority rejection. Clone and outer draft cleanup verified; the attempt stays failed.

- `fc2a503efc2948be94c956f2e8b5a531`: all prefix gates passed again. Restore
  `57f3bab767a346dfb458c3d9793636d0` for normal child
  `7e4b6de0c2474ad09ed71abdbfad4f1a` verified **all eleven databases**, including
  data/sequences, owners/permissions and successful schema restoration. The configuration
  stage then hit PowerShell strict-mode scalar collection enumeration (`Count` on one
  FileInfo). Explicit outer array capture fixes single-file enumeration; all 31 release
  guards still pass. Clone and source cleanup verified. Configuration and full candidate
  acceptance were not claimed from that failed attempt.

## Base-commit CI observation

Read-only GitHub inspection found nine successful workflows for the base commit:
frontend compatibility, day cutoff, hosted refund, end of day, financial completeness,
cashier day close, day-close preparation, cashier cutoff and day seal. The
[cash-close workflow](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38751770350)
failed its first portal browser case (`real snapshots propagate close, approval and
late-settlement invalidation`) by timeout. Its separate recovery/alert job passed.
Local reproduction `843266642d05406e88f8164b90175adc` also failed by timeout, with
cleanup verified. The browser remained at a disabled branch selector: its discovery
request returned 403 because the old fixture created identities without realm entry
roles. They now receive `customer-user`, while product assignments remain unchanged.
The selector also incorrectly used `day-close`, requiring unrelated `pos.day-close.read`
(not present in its pinned Authorization 7 baseline). Added `cash-close` discovery under
existing `pos.cash-review.read` and switched the panel; exact POS store authorization
still runs independently. Unit coverage checks exact-scope permission and revoked access.
The release runner now includes all five cash-close browser cases and six Authorization
persistence cases, not merely the newer historical day-close suite.
Fixed local execution `f2fed5877056426fbb8466ee86cd6dc4` passed all five unique browser
cases and all six Authorization persistence cases, with exact cleanup. This controlled
reproduction validates the fix locally; the remote failed run remains failed. No retry,
skipped case or base-commit success is claimed as CI for the local changes.

## Documentation review

Updated the root README, AI overview, canonical system and restaurant architectures;
release guide/evidence, November plan, manager/stack/seal/cash-close/observability runbooks;
operational-discovery and cash-close API contracts; database guidelines and Identity matrix;
Docker first-draft/PostgreSQL/Keycloak/Alertmanager READMEs; Restaurant, Customer BFF,
Frontend/customer-portal, first-draft/cash-close browser, cash-close fixture and integration
test READMEs, the Unit README and current settlement-reporting/cutoff browser counts.
Documentation-maintainer audits passed against the implementation, timestamp/discovery
fixes and final restore/configuration enumeration changes. The final evidence audit on
16 October 2026 checked aggregate and child receipts, TRX counters, both browser summaries,
the 11-database restore receipt, runtime/build-artifact inventory and bounded completion
log results. Counts, source identity, cleanup and stated recovery limits agree.

ADR-001, ADR-002 and ADR-035 were reviewed unchanged: no new ownership, schema or durable
architectural decision. Existing telemetry/correlation boundaries remain; the added
discovery capability uses the existing live exact-scope authorizer and safe events.

## Remaining gates

Final local candidate execution and documentation/evidence audits passed.
Clean-checkout CI for a committed Slice 6 candidate, clone application-graph startup, broker/Redis restore,
production receiver delivery, actual next-day solo testing and interactive Windows/PDF
acceptance remain separate. No POS local database is copied or made portable.
