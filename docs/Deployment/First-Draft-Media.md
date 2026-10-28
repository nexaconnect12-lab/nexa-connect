# Local first-draft product-image uploads

The optional `-EnableMedia` profile adds the existing Media service to a local Windows
draft. Default drafts retain their ten-service inventory. The explicit profile retains
Media migration 4, a separate Media runtime database login, private MinIO storage and a
ClamAV scanner. It does not add photos to the guest QR menu or publish any LAN Media route.

## Activation

Finish POS work and close its window. Stop the owned QR edge first if one is active.
For a retained draft, stop applications before enabling the additive profile:

```powershell
./scripts/first-draft-stack.ps1 -Action Stop -RunId <run-id>
./scripts/first-draft-stack.ps1 -Action Start -RunId <run-id> -EnableMedia
./scripts/provision-first-draft.ps1 -RunId <run-id> -ConfirmTestResources
```

For a fresh disposable draft use Start with `-EnableMedia -ConfirmTestResources`.
Once enabled the profile persists through Start/Stop/Restart; omitting the flag does not
disable or delete it. Existing realm, ports, scope IDs, business volumes, stock, orders,
financial records and protected POS recovery are retained. Enablement requires a stopped
draft and build-enabled Start. QR source changes still require fresh candidate/activation
and matching POS packaging under [QR7](QR7-Pilot.md); preserve old observations as history.

Build-enabled Start also writes the generated loopback Media URL into `Services:MediaApi`
in both published and retained POS settings when the persisted Media profile is enabled.
Default/off profiles write an empty value. Older settings without the property are supported;
an enabled profile with a missing Media URL or invalid/non-loopback inventory fails closed.
Refresh and install the matching POS package after rebuilding. An older installed release
with a blank `MediaApi` does not initialize gallery refresh, even when portal approval succeeds.

The first ClamAV start can take several minutes to obtain/load definitions and needs
internet access and sufficient Docker memory. The launcher waits up to 15 minutes for
the opt-in infrastructure. Failed scanner/storage startup does not disable scanning or
silently make an upload ready. Stop retains the new object/signature volumes; explicitly
confirmed Reset deletes only the generated project's resources, including those volumes.

Back up Media metadata/jobs and its object volume together with the protected configuration
and key rings. The existing ten-database first-draft restore drill covers the base profile;
it does not certify recovery of this optional Media database/object volume. A full Media-aware
restore rehearsal remains follow-up work before relying on uploaded images operationally.

## Identity and data ownership

The upload controller now admits the current `customer-user` realm role as well as
existing customer roles, then enforces the same live organization membership and Media
permission decisions. This is not an upload grant for all customer users.

The development realm template includes the Media client's explicit `nexaconnect-api`
audience mapper. For a retained owned local realm, Media-enabled Start adds that exact
mapper only when missing, rejects a conflicting mapper and verifies fresh workload-token
audience before application startup. It changes no user role, secret or production realm.
Catalog still checks the token's audience and exact Media client identity.

For this owned Development fixture only, `--media-access` provisioning creates explicit
organization-scoped `media.asset.read` and `media.asset.manage` overrides for the retained
store-manager subject. It requires an active manager assignment and a validated local
`nexa_review_it_<uuid>_authorization` database. It grants no new role or financial authority,
does not change cashier grants and never overwrites an existing deny or revoked override.
These are independent explicit Media permissions; revoke them explicitly when retiring
the local test manager. The original completed reference setup remains a no-op and does
not replenish stock or restore revoked business roles.

Media database initialization uses validated generated names, parameterized psql values,
quoted identifiers, expected migration ownership and a non-superuser runtime role. It
creates only the missing owning database/role and applies existing migrations; no new
business schema is introduced. Runtime migration-history mutation remains revoked.

MinIO and ClamAV bind generated **127.0.0.1-only** ports. The storage console is not
published. DPAPI retains separate root provisioning and bucket-scoped application keys;
Media never receives the root credentials. The private bucket denies anonymous access and
allows direct browser uploads only from the exact local staff BFF origin. Signed URLs
remain short-lived capabilities and must not be copied into reports. This local HTTP
loopback storage setup is not a public/production TLS configuration.

The staff BFF also sets `Bff:MediaUploadOrigin` to that exact storage origin. Its CSP allows
only that additional `connect-src` origin, never a wildcard. Omitted configuration keeps
the original self-only policy. HTTP is accepted only for loopback in Development/Test;
other environments require an exact HTTPS origin without a path, query or credentials.
The guest `/order` CSP always remains self-only. Hosted operators must configure the
browser-reachable storage origin separately from the private Media API origin and align
storage CORS and TLS; local enablement does not configure public image delivery.

## Test and use

```powershell
./scripts/test-first-draft-media-guards.ps1
./scripts/test-first-draft-media.ps1 -RunId <run-id>
```

The second command uses a fresh headless browser context and real local OIDC. It uploads
one generated PNG through the actual portal, signed PUT/CORS, Catalog owner validation,
storage, enabled scanner, database and processing worker. It checks original bytes, both
variants, unsigned-storage denial, wrong-tenant denial and cashier read/write denial,
then deletes only its generated asset and verifies eventual object removal. It creates
no orders or payments. No screenshots, browser state, tokens, URLs or credentials are
included in its bounded protected receipt. On failure, cleanup attempts only that test
asset; inspect retained pending/deletion jobs if cleanup could not finish.

The current runner also reads the configured branch's approved screensaver gallery through
the authenticated BFF and expects 200 with matching organization/branch IDs. An unavailable
branch must return 403. These read-only checks cover the manager's exact restaurant scope;
they do not save a gallery or prove delivery to POS. Restaurant resolves the active owning
restaurant before the existing branch permission check, without adding any grant.

For manual testing open the retained PC staff portal's `/#media`, sign in as
`draft-store-manager`, select the NexaConnect workspace and enter an existing Catalog
product UUID. Local reference product IDs are in the protected run's `reference.json`
`products` array; these are not order UUIDs. Select JPEG/PNG/WebP up to 10 MiB, upload,
refresh until processing is Ready and verify Original/Thumbnail/Display. No public
`usenexaconnect.com` DNS or deployment is activated by this profile.

Original readiness and asynchronous variant readiness are separate. Allow the worker to
finish both variants before checking Thumbnail/Display; the automated runner polls their
actual metadata rather than treating the original's Ready status as completed processing.

## Observability and acceptance limits

Service name is `nexaconnect-media`. Follow the browser request's validated correlation
ID into the BFF and Media structured logs. Safe failure queries include `Customer media
authorization denied`, `Media owner validation failed` and `Media completion dependency
failed`; failure logs retain category/organization only, without provider exception text,
signed URLs, object keys or content. Review durable Media processing/deletion queues for
retries. Existing [Media service documentation](../../src/Services/NexaConnect.Services.Media/README.md)
and [gallery setup](Next-Iteration-Phase-5.md) remain authoritative.

The QR6 gate includes Media role boundaries, optional-profile guards and an isolated owning
database case for narrow grants, idempotency, tenant separation, and preserved denies/
revocations. Target NAS/server deployment, actual phones, POS gallery/dual-display/PDF
acceptance and release readiness remain separate; upload success is not evidence that
guest QR menus display product photos.

## Disposable functional verification

The Media-enabled fixture `20c03729761c4b85bdb72d59ae0db699` completed the real-browser
lifecycle. Its protected `media-acceptance-3613987f6bf841e5a5a9d51fe86eb69b.json` records
real OIDC, portal upload/CORS, staff storage-origin CSP with guest self-only CSP,
original-byte checksum, both generated variants, unsigned-storage denial, wrong-tenant
denial, cashier read/write denial and eventual deletion. The test ran the profile's
configured scanner/storage/worker graph and did not create financial transactions.

This is disposable functional evidence, not actual phone/POS-gallery acceptance.

## Retained local activation evidence

On 27 October 2026, candidate `eaec08c1-0ce2-478f-8246-9648135e0dbd` passed the full
source-bound QR6 gate, including 1,052 unit tests, 31 HTTP cases and seven owning-database
cases with verified cleanup and broker recovery. The retained draft
`c30b628ab23141f389119daa565f894f` then enabled Media. Its protected receipt
`media-acceptance-f7ae6b6901dc43a39474c51dcd7d10a8.json` passed the same real-browser
upload, CSP/CORS, checksum, two-variant, tenant/cashier denial and deletion checks.

The refreshed pilot `edf35a4f98de424095f6618333249eac` serves the existing
`https://192.168.0.191:8443` origin. Local diagnostics verified trusted TLS 1.3,
guest HTTP 200 and staff-route HTTP 404. Matching POS release
`3bb459998d684acd965d261015965e1f` is installed and responds. The protected
`.runstate/local-media-activation.json` and `.runstate/local-media-preservation.json`
record source verification, unchanged scope/reference/existing URLs, and five unchanged
protected recovery files checked before POS launch.

For manual upload testing, open `https://127.0.0.1:63417/#media` as
`draft-store-manager`. An existing local reference product is
`b1a3a05b-0fe2-4dc7-9680-fc95fb01c4f1`.

These checks establish local activation only. The refreshed pilot's 24 device cases
remain pending; earlier scan observations remain historical. Actual user phone,
POS gallery, PDF and business workflows, production deployment and release acceptance
are not certified by these receipts.

### Gallery restaurant-scope correction

Later on 27 October 2026, candidate `4e448d5f-0d45-4f86-b404-082c123e821b`
passed the complete QR6 gate with 1,054 unit tests, 31 HTTP cases and seven owning cases
with verified cleanup and broker recovery. It supersedes the preceding local activation
for the gallery authorization correction. The same retained draft now uses pilot
`edefdcd844bc4e53b7c9aefffbcb3426` and matching responding POS release
`954ab0d0e9ad4f9e90b5162b8673702d`.

Its protected `media-acceptance-876cf1a4cdc8429cbeb5d1db8c20b091.json` passed the
real-OIDC gallery read for the configured branch (200), unavailable-branch denial (403)
and the upload/security/deletion checks above. `.runstate/gallery-scope-ui.json` separately
records use of the actual **Load gallery** button with real OIDC and no certificate bypass;
no gallery Save was submitted. `.runstate/gallery-scope-preservation.json` records unchanged
approved selections, scope, reference data and existing URLs, plus all five protected
recovery files checked before launch. `.runstate/gallery-scope-activation.json` records
source verification, local HTTPS success and the responding matching POS.

Refreshed local edge diagnostics again verify trusted TLS 1.3, guest 200 and staff-route
404 at the same origin. The current pilot's 24 device cases remain pending. These checks
prove the local gallery-loading correction without certifying phone workflows, saved
gallery delivery to POS, PDF or business acceptance, or production readiness.

### POS endpoint and bounded PNG delivery corrections

Candidate `e174d74f-734e-4e84-a0f0-15176d4141f1` subsequently passed the complete QR6
gate with 1,057 unit tests, 31 HTTP cases and seven owning cases with verified cleanup and
broker recovery. It includes both automatic POS Media endpoint publication and adaptive PNG
delivery. A memory-only probe of the previously failing approved photo measured a
2,261,366-byte PNG at 1280×853, reduced to 1,271,411 bytes at 960×639 by the corrected
encoder. The other two approved photos retained their dimensions. Stored images remain
unchanged.

The retained draft now uses pilot `3debaa66f3cb4360bf2a38821cbbb848` and installed,
responding POS release `898e9490157b4e2883e09024f75ec0a6`.
`.runstate/pos-gallery-activation.json` records matching source and installed Media endpoint
verification. A read-only request using the real cashier and public POS client's PKCE flow
returned HTTP 200 with the exact scope, two staff slides, two customer slides and three
PNG images with verified SHA-256 checksums; see `.runstate/pos-gallery-delivery-after.json`.
This verifies delivery to a POS-authorized client, rather than native display rendering.

The retained browser receipt `media-acceptance-47311e1268ab40f188ddeaa03e5617b9.json`
also passed gallery reads and the upload/security/deletion lifecycle.
`.runstate/pos-gallery-preservation.json` records unchanged approved selections, scope,
reference data, existing URLs and five protected recovery files checked before launch.
Local diagnostics again verify trusted TLS 1.3, guest 200 and staff-route 404 at the same
origin. The current pilot's 24 device cases remain pending.

After the operator resumed/authenticated, `.runstate/pos-gallery-native-cache.json` recorded
an authenticated POS and a 3,611,910-byte protected gallery cache updated after launch.
The user then closed POS. Reopening the matching release while the approved cache lease
remained valid displayed a staff slide beside **Workspace locked** and **Resume/Authenticate**.
Two direct visual observations twelve seconds apart showed distinct approved staff images,
confirming native locked-cache rendering and slide rotation. The bounded receipt is
`.runstate/pos-gallery-visual.json`; no authentication was automated.

The automatic idle-to-lock transition was not observed in this check, and the physical
customer display remains unverified. For that separate test, resume/authenticate, stay active
for at least one minute to allow refresh, then leave POS untouched for its automatic
five-minute idle lock. There is no manual Lock button. Inspect both playlists after the lock
appears. The observed reopening does not imply indefinite cache validity: the existing
five-minute maximum lease and neutral fallback still apply. Phone, PDF/business and
production acceptance remain pending.

### Fullscreen presentation follow-up

The later fullscreen presentation change fills each assigned staff/customer display with
aspect-preserving edge cropping. Staff retains a compact resume/sign-out overlay; customer
images have optional public captions and no controls. Physical monitor placement restores
the prior staff window on resume and retains topology/DPI/movement safety fencing.
The preceding rendering receipts establish the earlier layout only.

Fullscreen candidate `facb29f8-f2fe-4ec8-a7ab-127a80c4fa30` passed the complete QR6
gate with 1,070 unit tests and seven owning cases with verified cleanup and broker recovery.
The retained draft now uses pilot `c1fcce7e0f344c2bb1d943e376ba7ebe` and matching installed
release `4c9bdb531cf14dfc9d02ed8ce696e85b`. `.runstate/fullscreen-activation.json`
records source verification, responding POS, observed fullscreen staff lock surface and
compact resume controls. The 2560×1440 native lock surface had no window chrome; the
approved cache had expired, so this observation verifies neutral fullscreen fallback,
not a fullscreen approved photo. The operator independently resumed authentication;
the signed-in view and restored title-bar controls were observed without automated login.

`.runstate/fullscreen-gallery-delivery.json` separately records a read-only real-cashier
public-POS-PKCE request returning 200, matching scope, two staff slides, two customer slides
and three checksum-verified PNGs. The native probe records two placement/restore cycles,
minimized-window preservation, maximized-state restoration, unsafe-move rejection and three
customer layout/clear checks. `.runstate/fullscreen-preservation.json` records unchanged
scope, reference data, URLs, approved selections and five recovery files checked before launch.

The refreshed edge verifies trusted TLS 1.3, guest 200 and staff-route 404. Fullscreen
approved-photo appearance and the physical customer display remain unverified, and the
current pilot's 24 device cases remain pending. These local observations do not certify
production readiness, OS kiosk behavior or other business/device workflows.

### Staff wake-to-authenticate follow-up

The subsequent staff prompt refinement starts each lock with authentication controls hidden.
Tap/click or press a key to reveal a centered panel; the first gesture does not authenticate.
Choose Resume/Authenticate separately. Escape or Back to screensaver hides the panel while
remaining locked; active authentication keeps cancellation available. Slideshow renders
preserve an already open panel. Customer presentation remains control-free. The preceding
fullscreen receipts certify their earlier layout.

Candidate `5064c443-fcee-4d85-aea0-b6295b9e3758` passed the complete QR6 gate with
1,081 unit tests, all 57 QR browser cases and seven owning cases with verified cleanup and
broker recovery. An earlier unchanged attempt timed out in a WebKit collection-visibility
check; the focused retry and this complete fresh gate passed without changing QR code or
test limits. Eleven prompt-state cases and the isolated native layout probe cover wake-key
repeats, Escape/Back, authentication-in-progress visibility and centered layouts.

The retained draft now uses pilot `b88fbcc8be7b4d15a642b6d3e53ddcb0` and installed
release `f647c275bb9e47f583c71b8feef12920`. `.runstate/lock-prompt-activation.json`
records direct observation of initially hidden controls on the full neutral lock surface.
One Return key revealed the centered panel with **Resume/Authenticate**, while the workspace
remained locked and authentication did not start. The cache lease had expired, so this
does not establish a new fullscreen-photo observation. No authentication dialogue was automated.

`.runstate/lock-prompt-preservation.json` records unchanged scope, reference data, URLs,
approved selections and five protected recovery files checked before launch.
`.runstate/lock-prompt-gallery-delivery.json` records live read-only cashier/POS-PKCE bundle
delivery (200, exact scope, two slides per role and three checksum-verified PNGs). The new
edge again verifies trusted TLS 1.3, guest 200 and staff-route 404.

Live pointer/touch wake, Escape/Back dismissal, held-key behavior and cancellation were not
observed in this native check; their state/layout tests are separate evidence. Physical
customer display and the current pilot's 24 phone cases remain pending. No production or
full business/device acceptance is implied.
