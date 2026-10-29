# QR6 — integrated verification and phone-test candidate

The candidate gate additionally requires isolated `omise-profile-guards` for additive local
sandbox capability. These validate lifecycle, credential isolation and packaging without
provider traffic. Existing evidence is not relabelled; prepare a fresh candidate for changed
source. Actual Omise mixed-mode/operator acceptance is separate from this synthetic gate.

QR6 adds a repeatable local source/security/financial/QR verification gate and prepares a
fresh diagnostic candidate. It does not deploy, install, sign, push, freeze or accept a
release. Dedicated-server, installer, dual-display, real-phone and original-user acceptance
gates remain pending until their actual prerequisites/evidence exist. Never reuse the
earlier Phase8 or first-draft artifact/UAT binding for these changed inputs.

## Security and compatibility

Frontend pins patched Zod4.6.7 and the form-validation workspace requires its4.x peer.
This addresses the existing [collection resource-consumption advisory](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5),
whose patched version is4.6.6. Both production/full npm audits remain mandatory and
time-dependent; there are no advisory suppressions. Shared financial parsers use `z.guid()`
to preserve the existing .NET GUID contract rather than silently applying Zod4's stricter
RFC UUID version/variant rules. Scope equality, exact financial values, safe integer/date
checks, field errors and existing permission/CSRF boundaries remain in place. See the
[official GUID/UUID distinction](https://zod.dev/api#uuids) and
[migration guide](https://zod.dev/v4/changelog).

No service migration, permission, payment contract or runtime topology is added by QR6.
Current schemas include Order22, Restaurant4, Catalog4, Inventory5, Kitchen3, POS23 and
Reporting22; the full catalog remains209 tables. QR defaults remain disabled. Apply
[QR5 rollout](QR5-Collection.md) and matching POS admission binaries before isolated
enablement. Keep THB currency separate from the branch's Asia/Singapore timezone.

## Local verification command

Use Windows, PowerShell7.4+, supported Node22.12+ or24+, .NET10 SDK, local Docker and the
existing restored .NET/browser dependencies. Prepare fixture images `postgres:17-alpine`
and `rabbitmq:4-management`; the runner uses `--pull never`. From `src/Frontend`, install
the locked tree with `npm ci --ignore-scripts` and Playwright Chromium/WebKit. Return to
the repository root, then run:

```powershell
./scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure
```

Run this sequentially in the checkout. Do not edit executable/config/test inputs or run
another frontend install/publication until it finishes. It captures shared Git revision,
dirty flag, Git-clean non-Markdown input hash and the actual migration catalog before
running all unit tests with current-user DPAPI, architecture, QR HTTP/transport, hosting
and candidate guards, Windows POS build, frontend type/unit/build, both dependency audits,
QR phone-emulation/desktop and financial preparation/cutoff/seal/settlement browser suites.
QR9 adds 39 bounded operator-evidence guards; the candidate gate now requires 21 guards.
After the matching local activation, use [QR9 diagnostics and checklists](QR9-Acceptance.md)
to collect actual-device observations separately from these automated checks.
The exact inventories must pass without skips or retries. Private logs/JSON/TRX stay in
a generated current-user-restricted `.runstate/qr6/<run-id>` directory. Environment options
are restored. A failed/incomplete run cannot prepare a candidate.

The owning fixture is independently reusable:

```powershell
./scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure
```

It now requires seven cases and creates only uniquely named local PostgreSQL/RabbitMQ
containers with random credentials and loopback ports. Both are removed on completion or
failure; receipt success requires cleanup. The joined journey applies full owning
Restaurant/Catalog/Order/Inventory/Kitchen/POS/Reporting schemas. Actual table entry/session,
Catalog and Restaurant price reads feed Order quote/submission/price consent/admission,
then original Inventory/Kitchen recovery and cashier settlement. It exercises independent
visitors/tables, quote guessing, entry revocation, concurrent retries, committed dependency
response loss, original paid receipt, and event-derived cash/reporting deduplication.
The sixth case verifies branch order-history keyset paging, exact tenant/restaurant/branch
isolation and parameterized filters; the joined journey checks original QR readiness before
claim and read-only claimed/Paid history. The seventh case creates an isolated local
Authorization database and verifies that the optional Media grants are limited to the
intended manager/organization, idempotent, non-financial and preserve explicit denies
and revoked overrides. Current minima are 1,052 unit and 31 QR/history/Media
HTTP tests, including the order-list and Media authorization boundaries. The gate also
requires 22 candidate guards and the optional-profile guard runner. See
[local Media verification](First-Draft-Media.md). Historical dated
counts below remain evidence for their original inputs.

The unit minimum includes nine staff Media storage-origin/CSP cases: unsafe or
non-canonical origins fail startup, loopback HTTP is restricted to local test/development,
and only the staff policy gains the configured origin. Guest `/order` stays self-only.

Publication failure is injected through an unavailable loopback connection endpoint;
the real outbox retains its original financial events and retries after its real30second
lease. Publisher-confirmed RabbitMQ duplicate deliveries feed the actual owning projection
Application/repositories and produce one cash/sale/payment effect. This verifies broker
transport and projection behavior, not a production broker process restart or hosted
consumer deployment. Authorization/Directory decisions and some transport ports are test
adapters; existing HTTP/BFF tests cover authentication/tenant/CSRF/credential boundaries.
The workflow recovery fixture still uses an explicit clock advance to cross its initial
retry delay. It is not a real OIDC/WPF/phone/hosted acceptance test.

## Generated candidate and phone records

After an unchanged-source successful interval, the runner hashes the two compiled SPA
inventories and Windows POS assembly/runtime inventories, copies the owning cleanup
receipt, and writes `verification.json`. The candidate tool rechecks source, schemas,
compiled outputs and receipt hash before creating write-once `candidate.json` and
`phone-test.json`. Candidate guards reject missing/failed gates, stale receipts, changed
source/schema/artifacts or missing broker/cleanup proof. The safe candidate projection
contains counts and hashes, never private logs, guest secrets, bank references or tokens.

This is a local diagnostic integrity binding, not signed artifact provenance. Dirty
working-tree candidates are labeled and remain ineligible for release. These Debug/build
outputs are not an installer or hosting bundle. `releaseReady` is alwaysfalse; the tool
does not ingest edited attestations as automated proof. Phone records start with all
12cases pending per device, with model/OS/browser/environment and evidence IDs unset.
Rebuild/reverify after input changes; do not overwrite or attach old evidence to a new
candidate. Existing [release binding/freeze](../../scripts/release/README.md), clean-source
remote CI, signing and confirmed-host requirements still apply independently.

Use [the actual-phone checklist](QR6-Phone-Test.md) with either the prepared
[QR7 local-PC profile/guest edge](QR7-Pilot.md) after LAN/trust/device facts are ready, or
a matching deployed/configured release after dedicated-server prerequisites are ready.
Bind the selected environment and its applicable configuration/artifact evidence before
testing; do not treat this local candidate as hosted readiness.

## Executed local verification — 25 October 2026

Complete unchanged-input run: `da894004-7eeb-4f9e-8c64-4f8f519c3539`.
Its owner-restricted directory is
`.runstate/qr6/da894004-7eeb-4f9e-8c64-4f8f519c3539/` and contains `verification.json`,
`owning.json`, `candidate.json` and the all-pending `phone-test.json`.

| Gate | Measured result |
| --- | --- |
| Full .NET unit/current-user DPAPI | 991 passed, none skipped |
| Architecture | 6 passed |
| QR HTTP/transport | 22 passed, none skipped |
| Hosting/configuration guards | 47 passed |
| Candidate evidence guards | 20 passed |
| Windows POS build | Passed |
| Frontend type/unit | Passed; 78 tests passed |
| Both portal production builds | Passed; existing bundle-size advisory remains |
| Production and full npm audits | Passed; zero reported vulnerabilities |
| QR Chromium/WebKit phone emulation and desktop | 54 passed |
| Financial preparation/cutoff/seal/settlement browser contracts | 8 + 8 + 22 + 7 passed |
| Owning PostgreSQL/RabbitMQ | 5 passed; cleanup and broker recovery verified |

Compiled inventory binding covers two files in each SPA and21 POS assembly/runtime
files. Candidate binding is
`d97dc0d446649e4deb11c18369b65c4833615255bf6368e8abbac72dac108bf1`.
The recorded source revision is `dfed9e102a252c6e019a6546cab3f6d0a50d2361`, with
`dirty=true` and input hash
`3dabf23aa29a7cbe627bf7041b2b16adbe0c0575bb38b703af0285836fdae454`.
The full gate verified unchanged source through execution and candidate preparation.
This records a working-tree candidate, not clean-source CI or a signed release.

Both device records have12pending cases each. `releaseReady=false` and
`productionVerified=false`; hosted configuration, signed artifact binding, physical
phones, WPF/PDF/display/OIDC observations, server operation/recovery, clean remote CI
and user acceptance remain pending. No commit, push, install or target deployment
was performed. The prior QR5 dependency blocker is resolved for these patched/tested
inputs; its dated failed audit receipt remains historical.

Documentation changes cover QR6 operating/phone guides, dependency policy, form-validation
and affected component READMEs, release tools, roadmaps and earlier QR launcher compatibility
notes. Root README, project overview and both canonical system/restaurant architecture
summaries describe the new verification/evidence boundary; runtime ownership/schemas stay
unchanged. The documentation-maintainer audit reviews these against code and evidence.
