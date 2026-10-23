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
dotnet test tests/Integration/NexaConnect.IntegrationTests --filter 'FullyQualifiedName~QrGuestBoundaryTests|FullyQualifiedName~QrGatewayTransportTests'
./scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure
cd src/Frontend
npm run check
npm test
npx playwright install chromium webkit
npx playwright test --config playwright.qr-entry.config.mjs
npm run build
```

The persistence script uses only a generated local PostgreSQL container/database/schema,
checks the actual test receipt and verifies exact cleanup. Private logs/receipts are under
owner-restricted `.runstate/qr-entry/<run-id>`. It requires locally cached
`postgres:17-alpine` (`--pull never`); it never connects to a retained draft or server.
Browser fixtures cover desktop and Android Chromium/iPhone WebKit emulation, not physical
iOS Safari/Android Chrome or public TLS certification. Record measured results below.

Implementation verification: pending final execution/audit record.
