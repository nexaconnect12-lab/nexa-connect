# QR1 — secure table entry and individual visitors

QR1 source provides staff QR issuance/rotation/revocation and a context-only `/order`
landing for mobile browsers. Guests do not sign in. Staff accepts future requests before
Kitchen and payment remains at the cashier; those workflows are QR2–QR5, not QR1 features.
Read the [roadmap](../Planning/QR-Ordering-Roadmap.md), [contract](../API/QR-Entry.md),
[identity boundary](../Identity/QR-Guest-Entry.md) and [persistence](../Database/QR-Entry.md).

## Configuration and isolated use

1. Keep the retained first draft and Phase 8 artifacts unchanged. Build a new candidate.
   No new candidate is automatically frozen, installed or deployed by QR implementation.
2. Apply Restaurant migration 4 using the existing migration owner and backup policy,
   then grant narrowly scoped QR table privileges as documented. Existing financial
   service versions and realm permissions do not change.
3. Restaurant: set `QrEntry__Enabled=true`. Customer BFF: set `QrGuest__Enabled=true` and
   `QrGuest__PublicOrigin` to its exact public HTTPS origin, e.g. `https://customer.example.com`.
   Both flags default false. Enabled BFF startup rejects invalid/non-HTTPS origins.
   Use [the environment example](qr-entry.env.example) as a per-component template.
4. Use existing direct HTTPS `Services__Restaurant`, Directory/Authorization settings,
   server-held staff sessions and production certificate/key/session configuration.
   Do not publish Restaurant itself to guest networks. The trusted proxy must retain
   the public HTTPS host/scheme; do not enable arbitrary forwarded-header trust to bypass
   Origin/host checks. Use actual certificates trusted by phones before device testing.
5. An authorized owner/admin selects Configuration → Table QR entry, chooses its branch
   and creates an ordering table label such as A1. Save/print the shown QR using trusted
   local tooling. The capability appears once in that view; never use external QR sites.
   Reload loses the display; deliberate rotation recovers a new QR, never the old secret.
6. Scan with a phone camera. The HTTPS `/order#<opaque>` page removes the fragment, exchanges
   it once and shows branch/table context. It explicitly says menu ordering is unavailable.
   Refresh restores only that visit's context; End this visit ends that visitor's grant.
7. Rotation/revocation ends all older visits for the table on their next request. A stale
   page is not proof of ongoing authority; the next slice must revalidate every operation.
   Existing floor-plan records are not edited by QR1's staff-assigned ordering labels.

No QR1 process reserves stock, dispatches Kitchen work, accepts an order, collects a
tender or alters a shift/day-close record. Do not test real money through the landing.
Both cookie expiry and authoritative service reads apply; an ended/expired visit cannot
be restored from a query/header or a staff session. A new scan creates a new visitor.

## Failure/recovery and release limits

Missing scope/permission returns denial, never a cross-tenant fallback. Lost admin issue
response requires list/version reload and explicit rotation. Stale writes conflict;
no automatic mutation retries are issued by the panel. Invalid/expired/revoked entry
shows a bounded recovery message. Offline/unavailable dependencies fail closed. Session
admission quotas return 429/Retry-After; wait rather than rotating to bypass them.
The process-local BFF counters share NAT clients and reset on restart; SQL budgets remain
cross-instance. This does not certify public-internet abuse resistance or production load.

Restore policy: disable guest traffic, clear restored visitor hashes under authorized
maintenance, rotate printed entries and verify current ownership before reopening.
Keep QR audit and original financial/Windows recovery evidence. Destructive QR migration
rollback requires explicit backup verification and removes only the three QR tables.

Service names/debugging queries: `nexaconnect-restaurant` / `nexaconnect-customer-bff`,
filter `QR` and validated `CorrelationId`. Never export entry/session URLs, tokens, cookies,
headers, bodies or raw private fixture logs. Operator/server specs, real phone scans,
public TLS/proxy, clean CI and release/signing/UAT observations remain pending.

## Verification

Run publication/verification sequentially in one checkout; BFF publishing reinstalls the
shared frontend dependencies. With restored .NET/npm dependencies and local test images:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --filter 'FullyQualifiedName~QrEntry'
dotnet test tests/Integration/NexaConnect.IntegrationTests --filter 'FullyQualifiedName~QrGuestBoundaryTests|FullyQualifiedName~QrGatewayTransportTests|FullyQualifiedName~QrRestaurantBoundaryTests'
./scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure
cd src/Frontend
npm run check
npm test
npx playwright install chromium webkit
npx playwright test --config playwright.qr-entry.config.mjs
npm run build
```

The current [QR6 persistence runner](QR6-Integration.md) uses generated local PostgreSQL
and RabbitMQ containers and five required cases, including the joined original-order
journey. It checks the actual test receipt and exact cleanup. Private logs/receipts stay
under owner-restricted `.runstate/qr-entry/<run-id>`. Locally cached `postgres:17-alpine`
and `rabbitmq:4-management` images are required (`--pull never`); no retained draft/server
is targeted. The dated QR1 results below remain historical.
Browser fixtures cover desktop and Android Chromium/iPhone WebKit emulation, not physical
iOS Safari/Android Chrome or public TLS certification. Record measured results below.

23 October 2026 local implementation verification:

- Both Restaurant and Customer BFF compiled without warnings/errors.
- Full unit suite: 957 passed, none skipped, including actual current-user Windows DPAPI
  and QR Domain/application/transport/migration-catalog checks. Architecture suite: 6 passed.
- Final selected HTTP regression: 20 passed, none skipped: 11 QR owning-service/BFF/
  adapter checks and 9 existing gallery/Kitchen staff-boundary checks. Coverage includes
  anonymous/staff separation, live membership/tenant/CSRF, capability-header isolation,
  disabled routes, exact origin, safe errors, secure cookies, expiry, full bounded lists,
  credential-free guest forwarding and the anonymous landing CSP.
- Owning PostgreSQL gate: 1 comprehensive passed case with verified generated-container
  cleanup, receipt `.runstate/qr-entry/a3d0b194791c49169804d9a19d948553/verification.json`.
  It covers concurrent admission/writes, tenant/visitor isolation, restart reads, expired/
  suspended/revoked access, logout/start abuse-budget retention, audit/outbox rollback and
  destructive empty downgrade/re-upgrade. Failed earlier fixture setup was corrected and
  is not counted as passed evidence.
- Frontend TypeScript, all 76 unit tests and both portal production builds passed.
  Production and full dependency audits each reported zero vulnerabilities at execution.
- QR browser matrix: all 12 cases passed without retries/skips in Android-style Chromium,
  iPhone-style WebKit and desktop Chromium. Browser HTTP responses are explicit fixtures;
  owning HTTP/PostgreSQL checks are separate. No physical camera/phone pass is inferred.
- `.github/workflows/qr-entry-verification.yml` defines repeatable CI for QR Domain,
  HTTP, owning persistence and the browser/build/audit matrix. A remote CI execution was
  not performed as part of these local checks.

The QR documentation audit reviewed the implementation, addressed admission-limit/
response-bound/disabled-route drift and checked all affected summaries and links.
Both canonical architectures and the project overview were updated; ADR-005 remains
applicable unchanged. Root/component READMEs, QR API/identity/database/deployment docs,
environment example, operational-branch contract, database inventory and roadmaps agree.
Final `git diff --check` passed. This is local source verification, not a clean release
freeze, hosted deployment, actual phone acceptance or the user's consolidated second test.
