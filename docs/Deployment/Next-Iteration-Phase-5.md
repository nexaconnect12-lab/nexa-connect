# Phase 5 — approved media gallery and distinct screensavers

The retained local draft can now explicitly enable the complete Media graph with `-EnableMedia`; use [local Media setup](First-Draft-Media.md). POS gallery configuration and physical acceptance remain separate.

Implemented in source on 23 October 2026 on top of the retained Phase 4 working tree.
No prior changes, installer, active release, terminal recovery or UAT identity were reset.
[ADR-040](../Architecture/Decisions/ADR-040-branch-approved-screensaver-gallery.md) records
ownership/lease decisions; [the API contract](../API/Screensaver-Gallery.md) defines authority.

## Configure and use

1. Deploy compatible Restaurant, Media, Customer BFF/SPA and POS binaries. Restaurant
   migrations 1–3 and Media 1–4 remain sufficient; no new migration or grant is added.
2. Configure Restaurant `Services__Media` to trusted Media, and Media `Services__Restaurant`
   to trusted Restaurant, with existing Directory/Authorization addresses. Media still needs
   its own database, private S3-compatible storage, healthy scanner, Catalog owner validation
   and variant worker. Existing service TLS/authentication and customer audience apply.
3. Set terminal `Services:MediaApi` to the trusted Media origin (HTTPS; HTTP only on loopback).
   It defaults empty so existing POS configurations retain neutral fallback without network
   requests. For the local draft, build-enabled Start with the persisted `-EnableMedia`
   profile now publishes the generated loopback endpoint automatically into saved and
   published POS settings. Refresh/install the matching package; an older installed release
   can still have an empty endpoint and skip gallery initialization. Do not put workload/admin
   credentials in terminal or portal configuration.
4. Use a Customer identity admitted to existing Media management with Media read/upload
   authority as needed, menu-read branch discovery and branch configuration-manage authority.
   Upload product images, complete scanning, and wait for ready processed display variants.
5. In **Media management → Approved screensaver gallery**, select the branch and **Load gallery**.
   Select up to six images for each screen, edit public-safe names/text, reorder with **Move up**,
   set interval, then approve for public display. A ready original without its display variant
   cannot be approved yet. Empty selections remove media. A conflict/unconfirmed outcome clears
   editable proof and requires reload; no automatic write retry occurs.
6. Start POS on its private staff screen and assign Phase 4 monitor roles. While signed in
   or locked with a valid UX4 gallery reader, its independent timer refreshes once a minute.
   See [UX4 rollout](POS-UX4-Locked-Gallery.md). Leave POS untouched
   for the automatic five-minute idle lock (there is no manual Lock button): staff
   sees its playlist across the assigned monitor with authentication controls initially hidden.
   Tap/click or press a key to reveal a centered panel, then choose Resume/Authenticate
   separately. Escape or Back to screensaver hides the panel and remains locked; it stays
   visible during authentication so cancellation is available. Customer sees its own full-display playlist with optional public captions,
   no transaction or unlock controls. Images preserve aspect ratio and crop edges to fill.
   Gallery activity does not count as staff activity. Only original-operator
   authentication resumes work; slideshow clicks do not authenticate.

The staff saver uses full physical monitor bounds, including the taskbar area, and restores
the preceding bounds/chrome on resume. It never spans monitors. Without assigned roles,
it uses the single monitor or the unique monitor containing the window; ambiguous spans
and overlapping/invalid geometry retain safe windowed lock. Topology/DPI/unsafe-movement
events retain the existing neutral safety lock and require review/restart. Fullscreen is
application presentation, not an OS kiosk or screen-security boundary.

The wake gesture never authenticates, activates a business control or extends cashier
activity. Held/repeated wake keys are consumed through release. A new lock starts hidden;
slideshow refresh/rotation does not dismiss an already open panel. With no usable image,
neutral branding remains visible until the panel is revealed.

A minimized staff window stays minimized when lock rendering runs; it is not raised to
capture invalid minimized coordinates. After the operator restores it, the next render
can enter fullscreen. Resume also restores a previously maximized window's state.

Gallery authorization resolves the active owning restaurant before checking the existing
permission at organization/restaurant/branch scope. This corrects a 403 for a valid
restaurant-scoped manager caused by a missing restaurant scope in the decision request.
No role or permission is added. Missing, foreign or inactive branches/parents remain denied,
and live permission denials still apply. If loading fails, correlate the BFF 403 with
Restaurant's `Gallery configuration authorization denied` event before changing grants.

## Cache and failure semantics

- Media issues a maximum five-minute lease after live scoped reads. UX4 uses a separate
  expiring gallery-only reader while locked; expiry/outage can still produce neutral branding.
  Only an active authenticated cashier can obtain or replace that reader.
- One DPAPI-protected current-user file lives under
  `%LOCALAPPDATA%\NexaConnect\GalleryCache\screensaver-gallery-v1.bin`. It includes scope,
  approved public selection, hashes/images and lease; no bearer, URL or secret. Ciphertext
  and its atomic temporary replacement are each limited to 24 MiB. Encoded image bytes cap
  at 16 MiB; decoded images are limited to twelve at 1280×1280 each.
- Complete bundle validation and full WPF decode happen off the UI thread before installation.
  Cache writes are serialized, atomic and generation-fenced against sign-out/stale loads.
  Downloads have a total deadline and response-size bound; incomplete downloads do not install.
- Media keeps PNG delivery under 2 MiB per image and 16 MiB total. Detailed compressed photos
  that expand beyond the PNG cap are encoded at full size, then at most twice more at
  three-quarter dimensions (1280 → 960 → 720 for a maximum side). This changes only the
  delivery copy and preserves aspect ratio within pixel rounding; stored originals/variants
  remain intact. A remaining oversize/invalid image fails delivery without relaxing limits.
- Successful refresh replaces rather than merges content, including empty/missing/deleted
  selections. Explicit 401/403/404 and malformed bundle/decode clear approval. Dependency
  outages retain only an unexpired prior lease. Expired files are unusable and replaced or
  removed by subsequent refresh/clear; cache expiry never creates an authorization grant.
- Sign-out clears memory and schedules serialized file removal. If file deletion is prevented
  by the OS, memory remains clear and the protected file still cannot outlive its lease.
- Staff authentication remains visible when media is missing; customer falls back to neutral
  **Please wait**. Display-topology safety still closes customer output and masks staff work.
  No media operation submits/replays orders, renews cashier activity or mutates financial recovery.

This is not offline checkout, immediate global revocation, public hosting or an OS kiosk mode.
Physical monitor/touch/DPI and real provider/identity acceptance remain separate.

## Verification

The centered wake-prompt refinement passed eleven prompt-state cases, the isolated native
layout probe and the full source-bound QR6 gate (1,081 unit tests). Matching retained POS
observation verified initially hidden controls and a first Return revealing the centered
panel while still locked, without starting authentication. Live touch/pointer, dismissal,
held-key and cancellation observations remain separate. See
[bounded prompt rollout evidence](First-Draft-Media.md#staff-wake-to-authenticate-follow-up).

The fullscreen presentation change has source-level geometry tests and an isolated native
WPF probe under `.runstate/fullscreen-native-probe.private.log`: two staff fullscreen/restore
cycles, minimized-window preservation, maximized-state restoration, rejection of a one-pixel
escape through the actual window-message hook, and customer
landscape/portrait/small-layout fill and clear checks passed. The probe accesses no financial
state. Fullscreen candidate `facb29f8-f2fe-4ec8-a7ab-127a80c4fa30` subsequently passed
the full QR6 gate (1,070 unit tests) and the matching retained POS was installed. Direct
observation verified the complete 2560×1440 neutral lock surface, compact staff controls,
and restored signed-in window chrome after independent operator authentication. Its cache
was expired at startup, so fullscreen approved-photo appearance and the physical customer
display remain unverified. Live cashier bundle delivery still passes. See
[bounded fullscreen rollout evidence](First-Draft-Media.md#fullscreen-presentation-follow-up).

On 27 October 2026, the restaurant-scope correction passed eight `BranchGalleryTests`
unit cases and the expanded actual PostgreSQL gallery persistence case. The protected
owning receipt under `.runstate/gallery-persistence/6c5ab84d362c451cb9a71e311410fd15`
records one passed case and verified cleanup. It exercises the active hierarchy lookup,
wrong organization/missing branch, suspended branch/parent, and the existing version,
pricing and audit/outbox invariants. Corrected candidate
`4e448d5f-0d45-4f86-b404-082c123e821b` subsequently passed the full QR6 gate and the
retained real-OIDC gallery read and actual **Load gallery** button checks. No gallery Save
was submitted, approved selections remained unchanged, and physical POS gallery acceptance
remains pending. See [bounded rollout evidence](First-Draft-Media.md#gallery-restaurant-scope-correction).

The later POS endpoint/PNG corrections passed six `GalleryDeliveryTests` cases and the
complete source-bound QR6 gate (1,057 unit tests). Retained read-only delivery using the real
cashier and public POS PKCE client returned the approved bundle with verified PNG checksums,
and the installed POS Media endpoint is verified. After user authentication populated the
cache, reopening the matching release within its lease visibly rendered two distinct staff
slides beside the lock/resume controls. Native locked-cache rendering and rotation are
observed; the automatic idle-to-lock transition and physical customer display remain unverified. See
[delivery evidence and limits](First-Draft-Media.md#pos-endpoint-and-bounded-png-delivery-corrections).

Recorded verification: **910 unit tests** (including 22 gallery cases and isolated
Windows DPAPI fixtures), **6 architecture checks**, **76 frontend unit tests**, **7 gallery
browser cases**, **10 UI-foundation browser regression cases**, **3 gallery BFF boundary
integration cases**, **6 Kitchen BFF boundary regression cases**, and **1 actual PostgreSQL
persistence case** passed without skips. The PostgreSQL case used a newly owned disposable loopback
container and actual Restaurant migrations, then verified cleanup. It covers tenant isolation,
one concurrency winner, shared branch version, preservation of pricing/selection and atomic
rollback when outbox insertion fails. No retained application database was used.

TypeScript, both portal production builds, Restaurant/Media/BFF and native POS builds pass.
All four .NET builds reported zero warnings and zero errors.
Browser suites are synthetic BFF contracts; BFF tests use isolated test sessions, not real
Keycloak. Media tests use synthetic storage/ports plus actual bounded image encoding. Existing
ClamAV/S3 acceptance is not re-certified here. Vite still reports existing large-bundle advisories.

```powershell
$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE='1'
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore
dotnet test tests/Architecture/NexaConnect.ArchitectureTests/NexaConnect.ArchitectureTests.csproj --no-restore
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CustomerGalleryBoundaryTests
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CustomerKitchenBoundaryTests
# Persistence case requires an explicitly owned loopback database named nexa_gallery_it_<32 hex>:
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~BranchGalleryPersistenceTests
# From src/Frontend:
npm run check
npm test
npm run build
npm run test:e2e:gallery
npm run test:e2e:ui-foundation
```

The private runner under ignored `.runstate/phase5-gallery/test-db.ps1` creates/removes its
exact fixture container and generated test credentials. Without
`NEXACONNECT_GALLERY_INTEGRATION_DB`, the persistence case skips; that is not acceptance.
Do not point it at a shared or retained database. Its schema name is generated and quoted.

Off-screen developer renders under `.runstate/phase5-gallery/preview/images` use actual
customer XAML and stripped staff XAML, synthetic artwork and no App startup, window placement,
authentication, callback registration or recovery reads. Customer 800×600 and staff
1280×850/1000×680 gallery layouts were visually inspected; selected image/text and Resume/
Authenticate bounds passed. A synthetic portal approval screenshot was also reviewed. These
artifacts do not prove physical use. Selection tests found/fixed accessible option rendering
and repeat-selection behavior; no timeout/retry increase was used.

## Rollout and remaining user acceptance

Keep the prior release/recovery/UAT evidence. Deploy compatible feature binaries and settings
before enabling `MediaApi`. Rollback can disable that optional setting and restore prior clients;
the extra Restaurant JSON key is ignored by older binaries and no database downgrade is needed.
Never edit recovery files to enable gallery. The retained first-draft stack defaults to
no Media provider graph; its explicit `-EnableMedia` profile supplies local upload testing
as described in [local Media setup](First-Draft-Media.md). Phase 6 still owns the hosted
composition. Build-enabled publication now wires POS `MediaApi` for that profile, while
actual delivery, approval, lock rendering and physical display acceptance remain separate.

In Phase 9 test actual upload/scanning/processing/approval to the terminal, two different
playlists and ordering, original-cashier lock/resume, customer touch, active/idle/absolute
expiry, removal/deletion/permission revocation within the lease, internet loss, corrupt/interrupted
cache/download, restart and native monitor safety. Reconcile an original pending payment after
resume without duplicate effects. Record actual observations against the frozen new candidate.
Next: Phase 6 — dedicated-server pilot deployment and operations.
