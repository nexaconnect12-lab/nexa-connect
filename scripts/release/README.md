# Next-iteration release binding and freeze

Node 24+ and PowerShell 7.4+ tools prepare a new candidate independently of the retained
first-draft release/UAT records. Nothing here commits, pushes, deploys, installs POS,
submits to the Store or accepts the product. See [Phase 8](../../docs/Deployment/Next-Iteration-Phase-8.md)
and [ADR-043](../../docs/Architecture/Decisions/ADR-043-next-iteration-release-evidence.md).

From repository root, with normal current-user permissions:

```powershell
node scripts/release/next-release.mjs init .runstate/my-next-candidate
./scripts/test-next-iteration.ps1 -Profile Source
./scripts/test-next-iteration.ps1 -Profile Joined -ConfirmDisposableInfrastructure
node scripts/release/next-release.mjs add-verification .runstate/my-next-candidate .runstate/next-iteration-verification/<run-id>/verification.json
node docker/pilot/hosting.mjs build .runstate/my-hosting-bundle linux-x64
./scripts/package-pos-installer.ps1 -CompilerPath C:/tools/Inno/ISCC.exe -OutputDirectory .runstate/my-installer -PrivateUnsigned
node scripts/release/next-release.mjs bind .runstate/my-next-candidate .runstate/my-hosting-bundle .runstate/my-installer
node scripts/release/next-release.mjs status .runstate/my-next-candidate
```

`Source` is a fast independent diagnostic run. `Joined` repeats Source in the same
unchanged-source interval and then runs actual disposable checkout/Kitchen/local SQL
restore, settlement reporting/replay, historical Singapore cutoff/CSV and gallery
persistence. Both produce generated owner-restricted private logs plus bounded
`verification.json`. Ingest the successful Joined receipt once. Failed runs, skipped
tests, retries, incomplete inventories and source changes cannot pass. Do not edit
executable/configuration inputs while these run. Install Chromium and restore repository
dependencies first; the gate does not install them. It requires Windows for DPAPI;
Linux CI exercises complementary owning-service and portal cases.

Run verification and artifact publication **sequentially in one checkout**. BFF publish
targets install/build the shared `src/Frontend` workspace; concurrent hosting builds,
joined fixtures or dependency installs can replace `node_modules` while another gate
uses it. Complete one command and its cleanup before starting the next. Restore locked
dependencies after an interrupted competing install and rerun the affected evidence;
do not reuse the failed run as a passing receipt.

`Joined` requires a running local Docker daemon and locally available fixture images,
including `postgres:17-alpine` and `rabbitmq:4-management`; the settlement and gallery
fixtures use `--pull never`. Prepare those images before starting the verification
interval. The real-OIDC fixture launchers also require their documented Keycloak/proxy
images and available loopback ports. These are disposable local prerequisites and do
not authorize access to the dedicated server or retained first-draft infrastructure.

Shared `source.mjs` binds HEAD, actual dirty boolean and SHA-256 of sorted Git-clean blob
identities for tracked/nonignored non-Markdown files, including packaging/workflows/root
deployment settings. Git clean filters make identities portable across line-ending
conventions. Source identity is an integrity binding, not a signature. Markdown evidence
may be updated after execution; clean Git is still mandatory to freeze. Old Phase 6/7
artifacts have no shared binding and must be rebuilt. Existing individual producer fields
remain intact. Bundle inventory comparison supports legacy ordering without ignoring
missing/extra/changed bytes. No source stamp can be manually waived by the freeze tool.

Do not freeze unsigned test output. For the actual release, use the signing-required
installer build and existing trusted publisher credentials described by Phase 7. Freeze
rechecks Windows Authenticode for every payload PE and setup; a receipt claiming signed
does not establish it. Windows is therefore required for signed release freeze.

After confirmed server prerequisites, actual settings/certificates and artifacts exist:

```powershell
node scripts/release/next-release.mjs configure .runstate/my-next-candidate C:/private/plan.json C:/private/settings
node scripts/release/next-release.mjs template .runstate/my-next-candidate C:/private/observations.json
# Record actual results/evidence IDs/times in the template, without notes or secrets.
node scripts/release/next-release.mjs attest .runstate/my-next-candidate C:/private/observations.json
node scripts/release/next-release.mjs ci .runstate/my-next-candidate <github-run-id>
node scripts/release/next-release.mjs status .runstate/my-next-candidate
node scripts/release/next-release.mjs freeze .runstate/my-next-candidate
node scripts/release/next-release.mjs prepare-uat .runstate/my-next-candidate .runstate/my-second-test
```

Configuration validation reuses Phase 6 production guards and binds actual plan, each
service config/PFX and proxy PEM bytes by hash; raw settings/keys/passwords are not copied.
Private binding records contain local paths, so protect the run directory. Windows Init
applies the existing owner/SYSTEM ACL; Linux uses private file modes. Restricted sandbox
tokens may be unable to access that owner-only directory: run the tool normally rather
than weakening the ACL. Symlinked input/output paths are refused.

The operator template has eight exact IDs: hosted login/denial, remote POS/Kitchen,
reboot/outage, off-server full-graph restore, alert delivery, measured capacity, installer
roundtrip/callback and interrupted-installer recovery. `pending/passed/failed/blocked`
are explicit. Nonpending observations require a UUID and current UTC time after candidate
creation. Candidate/artifact/configuration bindings must match. Only allow-listed fields
persist; arbitrary notes/URLs/attachments/headers are dropped, pending evidence must be
null. These remain **operator attestations**, not measured automation or signatures.

CI is read through authenticated `gh api` on the configured GitHub origin. Only the
completed exact-HEAD workflow_dispatch of `next-iteration-release-verification.yml`
qualifies; all Windows, Linux and joined jobs must actually succeed. PR merge checks,
other workflows, missing/skipped jobs, a different SHA or truncated job listings fail.
Freeze and UAT preparation requery the recorded run/attempt; no local CI boolean creates
remote proof. CLI errors never forward raw API bodies. The tool does not dispatch CI or
push code. An unavailable network/credential remains a gate, not a fabricated pass.

All records use fresh paths and cannot be overwritten. For changed source/artifacts,
settings or observations create a replacement candidate and rerun affected evidence;
do not edit history to reuse IDs. `status` is read-only and returns safe blocker codes.
`freeze` writes `frozen.json` only after all gates pass; blocked calls leave no frozen/UAT
output. The manifest binds source/schema/artifact/configuration/evidence digests and
always retains `acceptance=pending-user-test`. UAT generation revalidates those bindings
and creates fifteen pending cases; it never updates the original sixteen-case draft UAT.

Verification:

```powershell
node --test scripts/release/next-release.test.mjs docker/pilot/hosting.test.mjs
```

No new HTTP service, BFF route, adapter, worker or database contract is added. Existing
financial gates use actual owning commands/idempotency/tenant/version/audit/outbox rules;
historical fixtures and SQL restoration are explicitly not real next-day user evidence
or a restored hosted application graph.
