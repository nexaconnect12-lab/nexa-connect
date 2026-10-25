# Keycloak Client Matrix

Native POS branch history reuses the public `nexaconnect-pos` customer bearer and live
branch `order.read`; tenant/application and stable `sub` remain required. A separate live
`order.manual-payment.confirm` decision plus enabled collection controls only the QR
selection hint. No new client, role, scope, mapper or grant is added. Guest capabilities
and workload-only identities cannot list staff history. See
[history authorization](../API/Branch-Order-List.md).

Phase 5 introduces no client, token, mapper or permission grant. Gallery approval uses
branch `restaurant.configuration.manage` plus organization Media-read authority for selected
assets. POS delivery reuses its customer bearer and live branch `catalog.menu.read` to retrieve
only approved public product content, without organization-wide Media listing authority.
The existing original-operator lock/resume boundary remains. See
[gallery authority](../API/Screensaver-Gallery.md).

Windows POS Phase 1 resolves its operator through the configured authority's authenticated
UserInfo endpoint after PKCE redemption/refresh, retaining only subject/authority alongside
existing protected session metadata. Locked resume forces `prompt=login` and rejects a
different operator. The public `nexaconnect-pos` client, `openid` scope and callback are
unchanged; no secret, grant or mapper is added. See [ADR-038](../Architecture/Decisions/ADR-038-pos-authenticated-resume.md)
and [upgrade/verification prerequisites](../Deployment/Next-Iteration-Phase-1.md).

The cash-close portal acceptance launcher assigns its generated OIDC users the existing coarse `customer-user` realm entry role. Scoped cash-review assignments remain authoritative; operational `cash-close` discovery requires `pos.cash-review.read`, not day-close permission. No new realm role, client, mapper or product grant is introduced.

Slice 6 changes no identity clients, roles, mappers or permissions. The [local restore drill](../Deployment/First-Draft-Release.md) backs up the quiesced generated Keycloak database with roles and checks original-archive DDL restoration and separately compares data/sequence dumps and owner/permission statements; same-account DPAPI credentials and BFF key-ring copies are checked separately. Login against a restored Keycloak graph is not verified; fresh PKCE after original-stack resumption remains an independent check.

Reporting's customer reporting, end-of-day, cutoff and sealed reconciliation entry filters now admit `customer-user` alongside existing customer entry roles. Live Directory membership, branch ownership and operation-specific product decisions remain mandatory. Draft manager/accountant permissions are product assignments, not realm-role authority; cashier reporting/variance approval remains denied. See [manager acceptance](../Deployment/First-Draft-Manager.md).

POS manual-tender admission requires Order client credentials with `azp=nexaconnect-order-service`, audience `nexaconnect-api` and `preferred_username=service-account-nexaconnect-order-service`; customer and other workload tokens are denied. The realm template adds the access-token-only `order-service-account-username` user-property mapper (`username` → `preferred_username`), without new clients or grants. Persisted realms need a reviewed mapper update and fresh Order token before rollout; restarting Keycloak does not re-import them. Order derives cashier identity from authenticated customer context. Windows installation/validated primary startup registers the fixed `nexaconnect-pos://oauth/callback` for public client `nexaconnect-pos`; secondary or failed-recovery startups do not alter registration. See [admission](../API/Terminal-Admission.md), [realm lifecycle](../../docker/keycloak/README.md) and [terminal installation](../Deployment/First-Draft-Windows-POS.md).

New product `tenant-admin`, `store-manager` and `cashier` assignments include existing `order.cancel` authority, matching Authorization migration 8. Keycloak clients, realm roles, tokens and audiences are unchanged; cancellation still requires live owning-service authorization.

Draft setup creates four customer-user realm identities with product scopes: branch cashier, branch kitchen-operator, restaurant store-manager and branch accountant. Product grants remain independent of realm entry roles; only the manager receives the bounded subject late-cash limit. See [draft setup](../Deployment/First-Draft-Provisioning.md).

The [local first-draft stack](../Deployment/First-Draft-Test-Stack.md) imports a generated private realm using the existing client template, verifies seven workload API audiences and creates only an identity-level `draft-smoke` customer-viewer. It uses no production identities or secrets. The separate slice 2 provisioner supplies product membership, cashier/Kitchen/manager/accountant grants and financial limits; an authenticated smoke session does not authorize product operations. Existing POS public PKCE and Customer BFF cookie/client separation remain intact.

The existing POS workload client explicitly maps its service-account `username` to access-token `preferred_username` using `pos-service-account-username`. API audience issuance and credentials-only flows remain unchanged. Persisted realms need a reviewed mapper update and a fresh token; optional `profile` scope and restart import cannot supply this prerequisite. See [Keycloak configuration lifecycle](../../docker/keycloak/README.md).

Authorization 13 grants live pos.day-close.finalize to tenant-admin/store-manager defaults, without accountant/cashier grants. Finalize also requires live pos.day-close.read. Durable recovery delegates an already-authorized POS intent to its existing nexaconnect-pos-service client-credentials identity; each internal source endpoint requires authenticated exact azp plus preferred_username=service-account-nexaconnect-pos-service and the configured API audience. Customer bearer sessions cannot drive source decisions. Keep its existing secret restricted to POS; no new identity client or secret is added. Revocation prevents new customer commands while durable recovery finishes recorded intent. See [settlement security and rollout](../Deployment/Day-Close-Settlements.md).

Source day seals reuse existing owner financial-read authority to retain/read scoped immutable evidence; that authority does not grant POS coordination. The separate manager seal coordinator requires `pos.day-close.prepare`, retained reads require `pos.day-close.read`, and Reporting additionally requires `reporting.sales.read` plus live owner reads. Accountants remain read-only at the coordinator. No client, audience, claim, scope, secret, role grant or monetary approval limit changes. See [seal authorization and routes](../API/Day-Close-Seals.md).

The [joined cashier-to-cutoff gate](../Deployment/Cashier-Day-Cutoff-Acceptance.md) uses existing POS PKCE and Customer BFF clients with cashier, two manager and accountant subjects in a disposable realm. Accountant uses the existing branch role; no product grant/client is expanded. Read/prepare/source permission and membership revocations are exercised through current scoped authority. Generated identity credentials and exact PKCE callback remain run-owned and never enter uploaded evidence.

The [retained day-cutoff evidence workflow](../API/Day-Close-Cutoffs.md) reuses the Customer BFF session and customer bearer. POS requires live `pos.day-close.read`/`pos.day-close.prepare`, Reporting requires `reporting.sales.read`, and source retention/read requires the owner's existing financial read permissions. Source capture persists an actor-bound observation under read authority; it grants no POS coordination or settlement approval. Accountant assignments remain POS read-only. No Keycloak client, audience, scope, secret or role grant is added; hierarchy/calendar reads retain their existing dedicated workload identities.

Pre-payment cancellation reuses the public `nexaconnect-pos` client and stable user `sub`; no client, scope, redirect, or secret changes. Authorization migration 8 grants branch `order.cancel` to tenant-admin, store-manager, and cashier roles. Order requires a live decision and stores its decision UUID with the immutable cancellation operation. Trusted workload identity cannot invoke the operator route. See [cancellation contract](../API/Order-Cancellation.md).

Paid receipt retrieval reuses the WPF POS public client and existing `order.read` permission. The Order receipt route always requires a customer bearer plus live branch authorization and deliberately does not accept the usual trusted-workload shortcut. No client, role, scope or secret is added. See [receipt contract](../API/Paid-Order-Receipts.md).

[Authoritative pricing](../API/Order-Pricing.md) reuses `nexaconnect-order-service`: Restaurant's branch-pricing endpoint requires the service-workload policy and exact `azp`, organization context and `nexa_connect` application. No new client, secret or product permission is introduced. Customer quote/place retains `order.place`; branch tax edits retain existing Restaurant configuration permissions.

| Client | Owner | Client type | Enabled flow | Local redirect URI |
|---|---|---|---|---|
| `nexaconnect-web-bff` | NexaConnect | Confidential | Authorization Code | `https://localhost:7100/signin-oidc` |
| `nexaconnect-admin-bff` | NexaConnect | Confidential | Authorization Code | `https://localhost:7200/signin-oidc` |
| `nexaconnect-mobile` | NexaConnect | Public | Authorization Code + PKCE S256 | `nexaconnect://oauth/callback` |
| `nexaconnect-pos` | NexaConnect | Public | Authorization Code + PKCE S256 | `nexaconnect-pos://oauth/callback` |
| `platform-admin-bff` | Shared platform | Confidential | Authorization Code | Shared-platform value; local fixture uses `https://localhost:58627/signin-oidc` |
| `platform-directory-admin` | Shared platform directory | Confidential service account | Client Credentials | None |
| `nexaconnect-reporting-service` | Reporting | Confidential service account | Client Credentials; Restaurant branch scope and business calendar only | None |
| Workload clients (`nexaconnect-*-service`, including Kitchen and Media) | Owning workload | Confidential | Client Credentials | None |

## Rules

The [joined Payment Review browser acceptance](../../src/Frontend/e2e/payment-review-live/README.md) creates a disposable `nexa-review-it-<run-id>` realm with normal Customer BFF/workload clients and distinct reader/resolver accounts. The Customer BFF token includes subject and API audience; the Order workload token includes the API audience for downstream compensation. Authorization migration 5 assigns accountant read-only and store-manager resolution rights. The expanded local joined suite passed 10/10 on 2026-09-02. No new production client or authentication flow is introduced.

- Register exact redirect and post-logout redirect URIs for every environment; do not use broad wildcards.
- Store confidential-client secrets in the environment's secret manager and rotate them.
- BFFs use refresh tokens only from server-side authentication tickets. Successful renewal replaces the stored access/refresh tokens; rejected renewal clears the session and requires interactive login.
- Public clients never receive a client secret and must require PKCE S256.
- Password/direct-access grants and implicit flow remain disabled.
- Full scope is disabled. Assign only explicit client scopes and role scope mappings.
- Create one service-account client per concrete workload rather than sharing a machine credential.
- Do not use NexaConnect-managed configuration as the production source of truth for `platform-admin-bff`; the checked-in realm entry is a local integration fixture only.

The checked-in development realm implements the four NexaConnect interactive clients, the `nexaconnect-api` bearer-only audience, dedicated POS, Catalog, Order, Inventory, Kitchen, Payment, Media, and Reporting workload clients, a local `platform-admin-bff` client with the `roles` scope enabled for role-claim testing, and the `platform-directory-admin` service account. POS, Catalog, Order, Payment and Reporting carry explicit client-level API audience mappers because their tokens cross service boundaries. The administration account has only `view-users`, `manage-users`, and `view-realm` from Keycloak `realm-management`. Each workload client uses a separately managed secret. Production registration and ownership of the platform clients remain with the shared platform.

The local realm also defines the Phase 2 platform roles (`platform-owner`, `platform-admin`, `platform-support`, `platform-auditor`) and customer roles (`customer-owner`, `customer-admin`, `customer-manager`, `customer-user`, `customer-viewer`). Product roles such as cashier or store manager remain separate. The legacy `system-admin` role is retained only for compatibility with older non-portal administration paths and must not be assigned as a substitute for the platform role model.

Portal boundary: `nexaconnect-web-bff` is the current Customer Portal session boundary, while `nexaconnect-admin-bff` is the NexaConnect product administration boundary. The ecosystem-wide Product Owner Portal uses `platform-admin-bff`, owned by the shared platform; the checked-in development client is only a local integration fixture. Do not reuse customer cookies, audiences, scopes, or secrets for Product Owner sessions. A future rename from `nexaconnect-web-bff` to `nexaconnect-customer-bff` must be deployed as an explicit client migration with overlapping redirect URIs only for the approved transition window.

The WPF POS client opens Keycloak in the system browser and receives the authorization response through the exact `nexaconnect-pos://oauth/callback` custom scheme. Its installer or device-enrollment procedure must register that scheme for the deployed executable; do not use a wildcard or a second redirect URI. Callback forwarding is restricted to the current Windows user, bounded in size, and state-validated for a single sign-in attempt. The POS client registration explicitly emits the stable Keycloak `sub` in access tokens; memberships and product role assignments must use that value rather than `preferred_username`.

POS browser sign-in has a client-side three-minute wait limit and an explicit Cancel sign-in action. Cancelled/superseded callbacks cannot install credentials; cashier shift/payment state is retained. During operator activity the client renews its five-minute access token one minute before expiry, replaces rotated refresh credentials in Windows-protected storage, and never extends the recorded ten-hour absolute session boundary. Five minutes without key, click, or touch input locks authenticated actions. Idle and absolute locks replace the online token set with a persisted nonsecret lock marker, preserve operational recovery across restart, and require interactive Keycloak authentication with `prompt=login`. Rejected refresh uses the same locked state, while a transient failure retries only while the current access token remains valid. These client controls do not weaken Keycloak's 30-minute SSO idle or ten-hour maximum session limits.

The Cash Review workspace reuses this same `nexaconnect-pos` user token. Authorization migration 7 provisions `pos.cash-review.read`/`resolve` for tenant-admin and store-manager assignments and read only for accountant assignments. No new Keycloak client, confidential secret, direct-access grant, or additional token audience is required.

Existing realms need an explicit reviewed update for the POS workload API audience mapper and a fresh workload token (or cached-token expiry). Restarting Keycloak does not re-import an existing realm; token validation is unchanged.

Reporting's workload token permits Restaurant branch authorization-scope and business-calendar lookup through `BranchScopeReader`; generic `ServiceWorkload` APIs continue to exclude it. It emits the `nexaconnect-api` audience and uses the distinct `NEXACONNECT_REPORTING_SERVICE_CLIENT_SECRET`. Customer report authorization still uses the customer bearer and a full organization/restaurant/branch decision. Provision this client and mapper explicitly in existing persisted realms before deploying corrected Reporting reads; see [Reporting configuration](../../src/Services/NexaConnect.Services.Reporting/README.md).

The [hosted refund acceptance gate](../Deployment/Hosted-Refund-Acceptance.md) uses generated disposable identity/infrastructure and existing service-owned schemas. Simulator evidence and the credentialed Omise test-account gate are separate from production readiness. The acceptance-only password-grant client and fixture approval limits do not change production provisioning.

Payment's confidential workload client now explicitly emits the nexaconnect-api audience for Order/Restaurant ownership lookup during refunds. Existing persisted realms require a reviewed audience-mapper update; restarting Keycloak does not re-import this change. The client remains credentials-only, with its existing distinct secret and unchanged endpoint authorization policies.

The cashier-to-day-close launcher confines its public POS callback adjustment to the generated disposable realm: that client uses one exact loopback callback for real S256 PKCE sign-in, with password grants still disabled. Cashier/manager subjects remain separate, tokens stay in test-process memory and manager portal commands use BFF cookies/CSRF. Default role grants are unchanged; the shared Inventory audience-mapper prerequisite below also applies to persisted production realms. See [acceptance identity boundaries](../Deployment/Cashier-Day-Close-Acceptance.md).

Inventory's existing confidential workload client must explicitly emit the nexaconnect-api audience when reading Restaurant branch scope. The realm template and validation guard now enforce that mapper; JWT validation, client identity, secret and role grants are unchanged. Persisted realms require an explicit reviewed mapper update, followed by a new token (restart Inventory or let its cached token expire); Keycloak restart does not re-import existing realms. The [joined cashier acceptance](../Deployment/Cashier-Day-Close-Acceptance.md) exposed this prerequisite through actual Inventory-to-Restaurant HTTP; production rollout remains separate.
## Phase 7 Windows installer

The [private EXE installer](../Deployment/Next-Iteration-Phase-7.md) retains the public
`nexaconnect-pos` client, exact `nexaconnect-pos://oauth/callback`, PKCE, existing scopes
and current-user callback pipe. It embeds no secret and grants no role/permission.
Callback takeover from another install path requires explicit operator approval; uninstall
removes only its own exact command. Hosted profile and retained protected cashier/realm
bindings must agree; unsupported realm/account transfers require original-installation
reconciliation. No MSIX package identity or identity-provider migration is applied here.

## Local pilot POS credentials

The optional QR-only `Pos:UseIsolatedLocalPilotStorage` setting keeps `tokens.bin` and its
original-cashier lock marker in the same scope-hashed `POS-Pilots` directory as recovery
and outbox. It retains current-user/device DPAPI, existing public PKCE client/callback,
UserInfo/operator checks and single-instance callback ownership. The original default
terminal's credentials are neither read for adoption nor replaced. Hosted profiles reject
the option; no client, role, audience or permission changes. See
[ADR-050](../Architecture/Decisions/ADR-050-local-pilot-pos-storage.md).
