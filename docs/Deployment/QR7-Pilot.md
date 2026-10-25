# QR7 — current-PC pilot preparation and actual acceptance

For QR9 use [structured acceptance tooling](QR9-Acceptance.md): read-only live TLS diagnostics and per-device bounded checklists linked to these same immutable activation/observation records. No device passes are inferred from diagnostics.

The native [Orders tab](POS-Order-List.md) reads all persisted branch Orders and selects an
eligible QR row through the existing original collection workflow. Install matching
Order/POS source in the same pilot scope and refresh its source-bound candidate/activation;
new list code does not update an older running terminal or certify actual phone/payment/
PDF acceptance. The guest-only edge route allow-list is unchanged.

QR7 prepares tooling and an unconfigured prerequisite plan. Unknown LAN/certificate/device
facts block activation until supplied and checked; starting the actual listener is an
explicit later operation. Phone/cashier/PDF observations, server specification-dependent
work and release freeze remain separate gates. Test fixtures use generated local/loopback
resources and do not certify the user's devices or network.

## Prepared state and prerequisites

Run the complete local gate and prepare a new pilot after installing the existing locked
SDK/npm/Playwright/Docker prerequisites described by [QR6](QR6-Integration.md):

```powershell
./scripts/test-qr7.ps1 -ConfirmDisposableInfrastructure
```

It runs plan/path/profile/certificate guards, real loopback TLS/edge HTTP boundaries,
a disposable QR-enabled first-draft lifecycle with real OIDC/restart/cleanup, then the
complete QR6 regression/candidate gate sequentially. It creates a fresh protected
`.runstate/qr7/<pilot-id>/plan.json`. Only the disposable fixture profile is started;
the prepared actual pilot has no listener, certificates, trust changes or observations.

Alternatively, after a fresh successful QR6 gate:

```powershell
./scripts/qr-pilot.ps1 -Action Init -CandidateDirectory .runstate/qr6/<new-run-id>
./scripts/qr-pilot.ps1 -Action Status -PilotId <pilot-id>
```

Use the actual IDs printed by the commands. A plan pins its candidate and generates a
fresh `stackRunId`; it cannot adopt a retained normal draft. The reviewed same-scope QR
source-refresh procedure below is the exception for an existing QR-enabled pilot. Candidate checks revalidate
source, migration catalog, compiled inventories and owning receipt. QR6 proof has a 24-hour
freshness limit. If preparation has expired when facts become available, rerun verification
and create a fresh plan; preserve/reconcile any already active original work independently.
Source/config/artifact changes require a fresh matching binding, not edited evidence.

When ready, fill the private plan's exact `publicOrigin`, PC `listenAddress`, both device
model/OS/browser versions and, only after actual installation/testing, its three trust
booleans. Use an explicit unprivileged HTTPS port, for example
`https://qr.example.test:8443`; this example is not a configured DNS name. DNS must resolve
exclusively to the chosen PC RFC1918 IPv4 on the test LAN. Do not use `localhost` on phones,
wildcard/public/link-local listeners, public port forwarding or an untrusted TLS bypass.
DNS/router/firewall setup is manual and must be scoped to the intended private network.
Do not open private service/database/identity management ports to phones.

## Optional local test certificates

After the real origin/address is confirmed in the plan:

```powershell
./scripts/qr-pilot.ps1 -Action Certificates -PilotId <pilot-id>
```

This writes a 14-day, exact-SAN server leaf/private key and a 30-day public test CA into the
protected directory, refuses overwrites, and prints only the public CA SHA256 fingerprint.
The CA signing key is discarded. No trust store, DNS, firewall or listener is modified.
Only `client-ca.cer` is distributed to devices; never copy/share the private key or private
pilot/stack directories. Verify the fingerprint before installing/trusting this test CA
through the Windows/iOS/Android device's normal administration flow. Confirm trust on each
actual device before setting that plan flag. Installing a CA does not itself pass a case.
Remove temporary test trust when the pilot is retired. Real device trust behavior remains
an actual observation, not something the generator can certify.

Existing certificate/key/public-root PEM files may instead be supplied at those exact
names, under the same protected directory. The server leaf must have its matching private
key, exact origin SAN, server-auth EKU, valid lifetime and chain to the distributed test CA.
The Windows edge reloads its leaf through PKCS12 for the non-ephemeral TLS key behavior
described by [.NET maintainers](https://github.com/dotnet/runtime/issues/93319); it does
not install a certificate into a trust store.

## Isolated QR stack and configuration

Use only the `stackRunId` and exact origin in this plan:

```powershell
./scripts/first-draft-stack.ps1 -Action Start -RunId <planned-stack-run-id> -QrPilotOrigin https://<confirmed-host>:8443 -ConfirmTestResources
./scripts/provision-first-draft.ps1 -RunId <planned-stack-run-id> -ConfirmTestResources
./scripts/qr-pilot.ps1 -Action Configure -PilotId <pilot-id>
```

This creates the existing run-owned service databases, migration targets, realm/roles,
consumers and loopback applications. Only the explicit new QR profile enables the QR
flags. Its origin cannot be added to or switched on a retained normal draft. Existing
schema discovery applies Order 22 / Restaurant 4 and all current owning targets. Product
provisioning creates reference branch/menu/stock/terminal and scoped cashier/Kitchen/
manager accounts; no financial transition is seeded. THB currency and Asia/Singapore
branch timezone remain distinct. Read credentials only through the existing private
[login helper](First-Draft-Provisioning.md); never paste passwords/tokens into reports.

`Configure` requires confirmed facts/trust, current private PC address/DNS, exact owned
Ready/provisioned stack listeners/processes, matching source/server inventories and scope.
It publishes the edge, derives the BFF public-certificate pin from that stack's protected
local certificate, writes bounded public configuration and a write-once activation hash.
Activation binds candidate, plan/device facts, scope, server/edge files, certificate/key
and configuration. It does not open a listener or assert phone acceptance.

Staff uses the stack's loopback Customer Portal for QR administration/review and `/kitchen`.
Issue the table QR there after confirming branch grants. Its URL uses the planned HTTPS
guest origin. The table capability belongs in `/order#<opaque-token>`, never query strings,
access logs or reports. Windows POS uses the same provisioned scope and original protected
cashier recovery within its selected data profile. QR publication explicitly enables
`Pos:UseIsolatedLocalPilotStorage=true`, putting that pilot's recovery/outbox/tokens in
`%LOCALAPPDATA%\NexaConnect\POS-Pilots\<scope-hash>` rather than opening an earlier default
terminal's data. Default/hosted clients retain their original directory. See
[current-account Windows setup](First-Draft-Windows-POS.md) and
[storage ownership](../Database/POS-Local-SQLite.md).
Follow [QR5 payment/receipt rules](QR5-Collection.md); no replacement
Order, combined table bill, guest payment or custody transfer is introduced.

## Refreshing source for the same QR pilot scope

An already configured QR pilot may need updated POS binaries while its branch, issued QR
entries and original financial state must be retained. Stop only its owned edge, then
stop/rebuild the exact existing QR stack without `-NoBuild`:

```powershell
./scripts/qr-pilot.ps1 -Action Stop -PilotId <old-pilot-id>
./scripts/first-draft-stack.ps1 -Action Stop -RunId <existing-qr-stack-run-id>
./scripts/first-draft-stack.ps1 -Action Start -RunId <existing-qr-stack-run-id>
./scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure
./scripts/qr-pilot.ps1 -Action Init -CandidateDirectory .runstate/qr6/<new-candidate-id>
```

Do not reset the retained stack, re-provision completed reference data, edit old activation
hashes, or copy terminal recovery. Publication preserves its scope and adds the explicit
isolated POS setting for this QR profile. This is an operator-reviewed local refresh,
not normal-draft adoption, a hosted update or automatic release approval.

Before configuring the new plan, verify that the existing stack is Ready/provisioned,
still has the same explicit `qrPilotOrigin`, exact realm and organization/restaurant/
branch/store/terminal IDs, and matches the fresh source/server inventory. In the new
unconfigured `plan.json`, select this exact reviewed QR `stackRunId` instead of its unused
generated ID and enter the unchanged origin/address plus actual device facts. Keep old
plan/activation/observation files immutable; the new plan starts with all cases pending.
Never reuse a normal draft or select a scope just to escape unresolved original work.

When the HTTPS origin, actual PC and certificate identity/lifetime remain unchanged, copy
only `certificate.pem`, `private-key.pem` and `client-ca.cer` between the same Windows
owner's protected old/new pilot directories. Verify the public CA fingerprint and exact
SAN before recording still-valid installed trust. Keep the private key private; do not
copy stack secrets, old activation/observations or any POS recovery. Changed origin,
expired/mismatched certificates or unconfirmed device trust require normal certificate
preparation and device installation again. `Configure` and `Start` revalidate the complete
new binding. Original QR entries/visitors remain service-owned and retain normal expiry;
the same-origin refresh does not issue replacements or mark device cases passed.

## Later activation and recording

Only after the actual prerequisites are ready:

```powershell
./scripts/qr-pilot.ps1 -Action Start -PilotId <pilot-id>
./scripts/qr-pilot.ps1 -Action Report -PilotId <pilot-id>
```

Start rechecks current proof, file hashes, DNS/private address, stack ownership and a free
port; removes inherited application/hosting/telemetry overrides; launches a hidden owned
dotnet process in Development; restores the caller's original environment, including
absent values; and verifies trusted HTTPS plus listener PID.
It does not bypass certificates or change firewall/trust. Failed startup stops only its
recorded process and preserves evidence. Successful startup still leaves all device cases
pending. The edge permits only guest `/order`, known assets and exact guest QR operations.
Staff routes, direct services, arbitrary queries, bearer/tenant/staff cookies, redirects
and unsafe backend errors are denied or stripped. Backend TLS requires the original exact
certificate pin, exact `localhost` SAN and lifetime. The edge constructs the outbound HTTP
Host from its fixed PublicOrigin to preserve the BFF's exact-host guard; backend TLS
identity is checked against the fixed BackendOrigin independently. It does not forward an
arbitrary Host or globally trust a self-signed upstream.

Execute [the QR6 phone/cashier checklist](QR6-Phone-Test.md) separately on iOS Safari and
Android Chrome. Each actually observed result requires explicit confirmation and an
opaque UUID evidence ID:

```powershell
./scripts/qr-pilot.ps1 -Action Record -PilotId <pilot-id> -Device ios-safari -CaseId camera-scan-https -Result passed -EvidenceId <evidence-uuid> -ConfirmObserved
./scripts/qr-pilot.ps1 -Action Report -PilotId <pilot-id>
```

Allowed results are passed/failed/blocked; never record an unobserved pass. Records are
append-only and bind the original configured candidate/device facts. Reports take the
latest observation per case, omit arbitrary notes/headers/financial bodies, label all
results operator attestations and keep `releaseReady=false`/`productionVerified=false`.
Automatic loopback tests do not write actual passes. The 12 cases per device start pending.

Stop only the guest edge with `-Action Stop`; its ownership guard uses PID/start time and
the exact executable/content root, never process-name-wide termination. This does not stop
Order recovery or erase sessions/orders/payments. Resolve original uncertainty through
staff/POS, then use the existing scoped first-draft Stop lifecycle to retain stack data.
Never reset/delete a retained pilot to clear financial recovery. Dedicated-server/cloud,
signed installer, dual-screen physical behavior, clean-source CI/freeze and final user
acceptance remain the separate existing roadmap gates.

## Diagnostics

Service `nexaconnect-qr-pilot-edge` uses shared structured JSON/correlation/optional OTLP.
Query `QR pilot route or host denied`, `mutation origin denied`, `admission limited` or
`upstream unavailable`, then follow validated `CorrelationId` into the Customer BFF.
The edge normalizes untrusted paths/queries/host telemetry, suppresses verbose HttpClient
logs, and never logs credentials/cookies/bodies or QR capabilities. Runtime/private
diagnostics, exported keys and raw operator configuration stay owner-restricted and
outside source control. See [boundary](../API/QR7-Pilot-Edge.md) and
[ADR-049](../Architecture/Decisions/ADR-049-local-qr-pilot-activation.md).

## Executed preparation verification — 25 October 2026

Complete unchanged-input run `ad853996b11c43558ed231a7090d3fc5` passed. Its private
receipt is `.runstate/qr7-verification/ad853996b11c43558ed231a7090d3fc5/verification.json`.

| Gate | Measured result |
| --- | --- |
| QR7 plan/path guards | 25 passed |
| QR7 profile/certificate tooling guards | 17 passed |
| Edge HTTP boundaries and real loopback TLS/pinning | 18 passed |
| Disposable QR profile lifecycle | All 10 checks passed; cleanup verified |
| QR6 .NET unit / architecture / HTTP | 991 / 6 / 22 passed |
| QR6 hosting / candidate guards | 47 / 20 passed |
| POS build; frontend type check and both builds | Passed |
| Frontend unit / QR browser emulation | 78 / 54 passed |
| Financial browser contracts | 8 + 8 + 22 + 7 passed |
| Owning PostgreSQL/RabbitMQ | 5 passed; cleanup and broker recovery verified |
| Production and full npm audits | Passed |
| Actual pilot preparation | Unconfigured; 10 prerequisites and all 24 device cases pending |

The lifecycle receipt is under
`.runstate/first-draft/f323f17389684de0a948c7d15da77e2e/`; it includes actual local OIDC,
BFF/Redis and Kitchen restart, full stop/start persistence and retained QR profile checks.
Its disposable stack was reset with verified cleanup. These observations concern local
automation, not physical phone, WPF/PDF or LAN acceptance.

The QR6 candidate at that execution, `423807b3-1a20-4120-9ddb-fde0779a24f3`, had binding
`4cc8c9f3e30a4aca0729857eef9e32b12020f57a0419e760c5f3a0306ea8a82b`.
The shared source revision is `d4e5ce88d77f053afab7d3bfd84fdcc633533bc4`,
`dirty=true`, with non-Markdown input hash
`d9d89e94c65f3ab2f929017bfb85be8a1d20cb99be23c44d4848ff48934c9164`.
This is working-tree diagnostic evidence, not clean-source CI or signed provenance.

At preparation time, pilot `5943984053f842cbb4d5f9a39c7a940d` contained only protected
`plan.json` and `acceptance.json`. Its report then confirmed `configured=false`,
`releaseReady=false` and `productionVerified=false`. Missing facts were the LAN origin/private address,
Windows/iOS/Android trust, both phones' device facts and the three certificate files.
That preparation run created no actual pilot certificates, activation, listener or observed passes.
The 24-hour candidate freshness rule still applies; rerun preparation when prerequisites
are ready if this binding has expired. Prior QR6 dated evidence and Phase 6–9 deferred
hosting/hardware/signing/CI/freeze gates remain separate.

## Same-account POS startup verification — 25 October 2026

The subsequent all-branch Orders implementation has its own
[candidate, package and activation evidence](POS-Order-List.md#local-candidate-evidence--25-october-2026).
The startup verification below remains evidence for its earlier source candidate.

The retained `.191` QR stack `c30b628ab23141f389119daa565f894f` was rebuilt in place
without Reset. Its realm and all five provisioned scope IDs remain unchanged. Its POS
configuration now explicitly selects isolated local storage. Installed release
`eaea6081e46e40a8b71ef5b86da39974` passed the actual current-Windows-account executable
preflight with exit 0; before this fix the unrelated default terminal scope caused exit 2.
The launched `NexaConnect · Cashier` window was visible and responding. The three original
default recovery files retained their pre-update hashes. Private bounded receipts are
`pos-ui-preflight.json` and `legacy-recovery-after.json` under the original protected
`.runstate/qr7/c7838516cddc4ad7aaa6ba01b1fd37cf/` directory.

Final QR6 run `9d31ae26-4a67-412c-a3e7-c3640fe6335a` passed against source revision
`d4e5ce88d77f053afab7d3bfd84fdcc633533bc4`, `dirty=true`, input hash
`d9a7afcbb1a3e9ee383be1b58a15e5c7a7253133319a2806aa59b11c7132fd4e`.
Its private verification/candidate/owning receipts record:

| Gate | Measured result |
| --- | --- |
| .NET unit, including local pilot scope/restart/DPAPI preservation | 1004 passed; none skipped |
| Architecture / QR HTTP | 6 / 22 passed |
| Hosting / candidate guards | 47 / 20 passed |
| Frontend unit / QR browser emulation | 78 / 54 passed |
| Financial browser contracts | 8 + 8 + 22 + 7 passed |
| Owning PostgreSQL/RabbitMQ | 5 passed; cleanup and broker recovery verified |
| POS build, frontend type check/builds and both npm audits | Passed |
| Terminal package/storage guards | 35 passed, including JSON-object settings and recovery-root overlap |

New immutable pilot `29b3db313be24d47a9a27722cc969d4e` is configured against this final
candidate with the same reviewed QR stack/scope and exact `.191` certificate, CA and key.
The old activation and records remain intact. Its report has no unresolved prerequisite,
but all 24 device cases still start pending and `releaseReady=false`/
`productionVerified=false`. Trust/device facts remain operator reports; this update does
not certify the previously reported Safari certificate issue as resolved.

The restored edge's final trusted-HTTPS preflight returned 200 for `/order` and its
JavaScript asset, while the staff-session route returned 404. The installed cashier window
remained responding and the three original recovery files were rechecked unchanged.
The bounded receipt is `.runstate/qr7/29b3db313be24d47a9a27722cc969d4e/pos-startup-fix-verification.json`.
This is a PC-side HTTPS check, not an observed phone acceptance result.

This evidence establishes same-account POS startup and original-file preservation, not
cashier PKCE/login, shift/order/payment/recovery/PDF or physical-phone acceptance. The
SQLite schema remains 2 and the business migration catalog is unchanged. Intermediate
candidate `3058a32f-d31a-4106-a516-db4bd48ba267` predates the final JSON-object package
guard correction and is not the current source proof. Earlier QR7 evidence remains
attached to its original source. Clean CI, signing and actual-device gates remain separate.
