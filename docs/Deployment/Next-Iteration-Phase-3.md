# Next-iteration Phase 3 — direct Kitchen display entry

Implemented in source on 21 October 2026 from `348bd27`, following the
[roadmap](../Planning/Next-Iteration-Roadmap.md). This is a focused entry within the existing
Customer Portal/session, reusing the Kitchen-owned queue. No new ticket store, service,
permission, schema or payment workflow is introduced.

## Open and use

1. Open **`<Customer Portal origin>/kitchen`**. `/kitchen/` also works. Use the retained
   draft's actual `urls.Bff` origin: its port is allocated by the launcher. Normal development
   entry is `https://localhost:51829/kitchen` (BFF with published SPA). For frontend-only
   development, Vite serves `http://localhost:5175/kitchen` with `CUSTOMER_BFF_URL`
   configured; its `/bff` proxy does not change the BFF's registered OIDC callback origin.
   Use the published same-origin BFF for real sign-in/return verification.
   These URLs expose the new entry after the updated candidate is published; this source
   change does not update the retained first-draft installation.
2. Sign in with an authorized Customer identity. The path/query/hash destination passes
   through the existing BFF login challenge. Select an accessible `nexa_connect` workspace
   if no valid workspace is selected. Product Owner roles do not grant Customer access.
3. Select an authorized **Kitchen branch**, optional menu-derived **Kitchen station**, then
   **Refresh queue**. Reload/login returns to the display but deliberately resets branch/station;
   it never restores an old ticket snapshot or mutation command.
4. Use **Start → Ready → Complete**. Read-only operators cannot transition. Preparation can
   precede payment; Kitchen completion is not payment proof.
5. **Enter fullscreen** requests browser fullscreen; **Exit fullscreen** or Escape leaves it.
   Unsupported/rejected fullscreen shows guidance and keeps the normal window usable.
   This is not OS kiosk mode.
6. **Return to manager portal** opens `/#kitchen`; manager Kitchen navigation offers
   **Open Kitchen display** back to `/kitchen`. Both entries use the same queue implementation.
7. **Sign out** removes operational content synchronously and aborts panel requests before
   top-level logout navigation. The loading message does not assert server logout succeeded.

Workspace switching while Kitchen is selected clears its queue before the protected selection
POST completes. A failed selection requires another authorized choice. Other manager workflows
retain their existing state until selection succeeds. Branch/station changes discard tickets/pagination;
tenant changes unmount the old panel. Read denial (401/403) removes previous tickets; transport
failure retains stale, locked evidence. Successful explicit refresh clears old failure guidance.
No transition is automatically retried after conflict or response loss. No new browser storage
holds queue, credentials, selection or mutation state. See [Kitchen contracts](../API/Kitchen-Queue.md).

## Deployment and boundaries

Publish the existing `NexaConnect.CustomerBff`; its build target includes the SPA in `wwwroot`.
The existing `MapFallbackToFile("index.html")` serves direct `/kitchen` loads and refreshes
through existing security/CSP middleware. The anonymous shell grants no ticket access:
session, discovery, catalog and ticket APIs retain live membership/resource/CSRF checks.
No new BFF endpoint or adapter is introduced; URLs and discovery options are not grants.

A reverse proxy must forward the Customer origin's paths, including `/kitchen`, `/assets/*`,
`/bff/customer/*`, `/signin-oidc` and `/signout-callback-oidc`, to the Customer BFF without
stripping `/kitchen` or redirecting it to `/`. Preserve HTTPS/public-origin callback settings.
Separate static hosting must serve the SPA for `/kitchen` and `/kitchen/` and route BFF/OIDC
paths under that same Customer origin; do not rewrite API failures to HTML. Dedicated-server
TLS/proxy configuration remains Phase 6 pending the user's server details.

Kitchen remains online-only; `Services__Kitchen` must point to the actual trusted host. Existing
service names `nexaconnect-customer-bff` and `nexaconnect-kitchen` retain validated correlation
and safe structured events. Query `{service_name="nexaconnect-customer-bff"} |= "Kitchen BFF"`
and `{service_name="nexaconnect-kitchen"} |= "Kitchen operator"`, then match correlation ID.
Never capture tokens, cookies, bodies or ticket lines in diagnostics.

Rollback the Customer SPA/BFF candidate using the retained release process; no schema downgrade
is required. Existing `/#kitchen` navigation and service contracts remain compatible. This phase
does not install, activate, deploy or reset the retained draft, recovery or UAT records.

## Verification

From `src/Frontend`:

```powershell
npm run check
npm test
npm run build
npm run test:e2e:kitchen
npm run test:e2e:ui-foundation
npm run test:e2e:cash-close
npm run test:e2e:payment-review
```

From the repository root:

```powershell
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~CustomerKitchen|FullyQualifiedName~KitchenQueueHttpTests"
```

Executed: TypeScript checks, both portal production builds, **76 frontend unit tests**,
**19 Kitchen browser cases**, **10 shared-shell browser cases**, **4 cash-close browser cases**,
**5 Payment Review browser cases**, and **18 selected backend integration cases**, all passing
without skips. Desktop and compact Kitchen screenshots were
visually reviewed. Browser cases exercise the actual React
UI against synthetic BFF responses; login handoff is synthetic, not a real Keycloak exchange.
Six new in-process BFF cases verify shell/API separation, direct fallback paths and protected
OIDC return state including external-URL rejection. Other selected backend cases cover existing
CSRF, authorization, adapters and lifecycle with test dependencies, not deployed PostgreSQL/broker.

The delayed-logout test initially failed while Playwright inspected a document during an
intentionally pending navigation. It now observes DOM removal before navigation using a
test-only MutationObserver/binding, retaining the removal assertion. Sign-out explicitly flushes
rendering before anchor navigation. No test timeout is extended.

Explicit synthetic screenshots live under ignored `src/Frontend/test-results/kitchen`;
automatic failure screenshots, trace and video remain off. Existing Vite large-bundle advisories
remain; a passing build is not hosted performance acceptance.

## Pending consolidated acceptance

In Phase 9 use the frozen replacement candidate and chosen device: actual OIDC return/refresh,
browser history, denied roles/tenants, scope/sign-out changes during slow calls, two competing
operators, dependency loss/recovery, fullscreen/Escape, actual resolution/DPI, touch and readability.
Confirm a POS order reaches this same queue without inferring payment success from preparation.
Record observations separately from synthetic checks. Hardware selection, LAN/offline execution,
OS kiosk management and public deployment remain outside this phase.

Next: Phase 4 — staff and customer Windows POS displays.
