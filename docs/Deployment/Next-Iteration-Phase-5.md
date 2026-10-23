# Phase 5 — approved media gallery and distinct screensavers

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
   requests. Do not put workload/admin credentials in terminal or portal configuration.
4. Use a Customer identity admitted to existing Media management with Media read/upload
   authority as needed, menu-read branch discovery and branch configuration-manage authority.
   Upload product images, complete scanning, and wait for ready processed display variants.
5. In **Media management → Approved screensaver gallery**, select the branch and **Load gallery**.
   Select up to six images for each screen, edit public-safe names/text, reorder with **Move up**,
   set interval, then approve for public display. A ready original without its display variant
   cannot be approved yet. Empty selections remove media. A conflict/unconfirmed outcome clears
   editable proof and requires reload; no automatic write retry occurs.
6. Start POS on its private staff screen and assign Phase 4 monitor roles. While signed in
   and active, its independent gallery timer refreshes once a minute. Lock as usual: staff
   sees its playlist beside Resume/Authenticate; customer sees its own playlist, no transaction
   or unlock controls. Gallery activity does not count as staff activity. Only original-operator
   authentication resumes work; slideshow clicks do not authenticate.

## Cache and failure semantics

- Media issues a maximum five-minute lease after live scoped reads. POS refreshes only while
  signed in and active. A long lock or outage can therefore expire into neutral branding.
- One DPAPI-protected current-user file lives under
  `%LOCALAPPDATA%\NexaConnect\GalleryCache\screensaver-gallery-v1.bin`. It includes scope,
  approved public selection, hashes/images and lease; no bearer, URL or secret. Ciphertext
  and its atomic temporary replacement are each limited to 24 MiB. Encoded image bytes cap
  at 16 MiB; decoded images are limited to twelve at 1280×1280 each.
- Complete bundle validation and full WPF decode happen off the UI thread before installation.
  Cache writes are serialized, atomic and generation-fenced against sign-out/stale loads.
  Downloads have a total deadline and response-size bound; incomplete downloads do not install.
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
Never edit recovery files to enable gallery. The retained first-draft stack does not automatically
start/configure the complete Media provider graph; Phase 6 owns the hosted composition.

In Phase 9 test actual upload/scanning/processing/approval to the terminal, two different
playlists and ordering, original-cashier lock/resume, customer touch, active/idle/absolute
expiry, removal/deletion/permission revocation within the lease, internet loss, corrupt/interrupted
cache/download, restart and native monitor safety. Reconcile an original pending payment after
resume without duplicate effects. Record actual observations against the frozen new candidate.
Next: Phase 6 — dedicated-server pilot deployment and operations.
