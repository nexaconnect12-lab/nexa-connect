# POS UX4 — continuous locked screensaver playback

Implemented and activated in the retained local pilot on 28 October 2026. Human/device
acceptance remains separate and pending as described below.

## Deployment

Deploy matching Media, Restaurant, Platform Directory, Authorization and Windows POS. No
schema migration, role, grant, OAuth client or workload client secret is added. The new
gallery-reader capability is itself secret. Keep Media's persisted Data Protection
key ring and existing workload identity with API audience `nexaconnect-api`. Keep internal
routes private. Restaurant uses existing `Services:PlatformDirectory`/`Services:Authorization`;
Media uses `Services:Restaurant`. Dedicated adapters disable redirects, propagate validated
correlation and bound responses. See [ADR-051](../Architecture/Decisions/ADR-051-locked-gallery-readers.md).

The optional terminal `Services:MediaApi` remains required; empty disables gallery initialization.
After upgrade, authenticate as the original cashier and wait one minute to bootstrap a reader.
A locked upgrade with no reader cannot renew images: an existing approved lease may display
until expiry, then falls back to neutral until authentication. Readers last at most
24 hours, replaced only by an active authenticated cashier, starting within the final hour.
They cannot extend a cashier session or permit selling while locked. No DNS/certificate or
financial profile change is required.

## Cache and failures

The five-minute image lease and existing 24 MiB protected image cache remain unchanged at
`%LOCALAPPDATA%\NexaConnect\GalleryCache\screensaver-gallery-v1.bin`. Separate DPAPI readers
live at `%LOCALAPPDATA%\NexaConnect\GalleryReaders\<scope-hash>\reader-v1.bin`, with a 16 KiB
ciphertext read/write limit. Hash scope includes authority, Media endpoint, organization,
restaurant, branch, store and terminal; payload also binds original subject. A bounded
extra byte detects oversized input. Linked
paths are rejected and failed reads remain neutral. No SQLite schema changes.

Sign-out, explicit 401/403/404 and malformed bundle/decode clear memory and serialized caches.
Generation fences reject stale async results; removal queues before later writes. OS deletion
failure leaves protected files bounded by original expiry/operator binding. Restart needs a
verified original-operator marker. Do not copy these files between Windows accounts.

Healthy locked refresh uses no cashier access/refresh token. Outages retain only the prior
unexpired image lease, then neutral branding; reconnect restores current images while the
reader remains valid. Live membership/menu-read revocation and asset withdrawal are bounded
by the existing five-minute issued lease. OIDC logout/account disable alone is not reader
revocation. Changed selection replaces old content; unchanged playlists retain rotation.
Display-safety lock pauses downloads. Reader expiry requires fresh cashier authentication.

## Verification and pending acceptance

The corrected source-bound candidate `1fbf11a4-7799-418b-aeba-52d5ca53206b` passed the
complete local gate on 28 October 2026: 1,150 unit tests, 6 architecture checks, 47 HTTP
tests, 47 hosting checks, 88 frontend tests, 57 QR browser cases and 45 financial browser
cases. Guard suites and seven owning persistence cases passed, with cleanup/broker recovery
verified. Windows density/input fixtures and isolated fullscreen/centered-prompt probes
also passed. Independent documentation audit resolved the strict reader-cache bound.
Package `b25bf5fe8bda47379b2855d7aedd532f` was installed into the existing current-user
DraftTerminal after the operator confirmed closure. Retained draft
`c30b628ab23141f389119daa565f894f` rebuilt without reset and reached Ready with all 12
services. Fresh pilot `19a95f7b46034c55bd0d8c74e81e9f5b` uses the unchanged trusted
certificate and `https://192.168.0.191:8443` origin. HTTPS diagnostics passed, including
guest 200 and staff-route 404. New phone observations remain pending.

All six recovery/preference fingerprints matched before launch, with unchanged references,
scope and service URLs. Installed configuration returned Standard density and automatic
keyboard off. Live local cashier PKCE issued a gallery-only reader and returned two staff
slides, two customer slides and three checksum-valid PNGs through delegated authorization,
without a cashier bearer on the image request. POS reopened as the exact installed release
with a responsive native window; no interactive native sign-in, sale or payment was submitted.
Long locked/native playback and physical dual-screen acceptance remain pending. This is a
dirty-source local candidate, not a signed production release or completed human UAT.

Automated coverage: `GalleryReaderTests`, `PosGalleryReaderTests` (Windows with
`NEXACONNECT_POS_DPAPI_ACCEPTANCE=1`), `GalleryReaderBoundaryTests`, existing gallery tests,
POS build and `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure`. The HTTP gate includes
all 16 new reader/delegation boundary cases.

After activation, authenticate, wait one minute, then allow idle lock. Check both role
playlists for over ten minutes, including multiple slides at a 60-second interval. Change
the approved selection and verify refresh. Interrupt service access and check neutral
fallback within five minutes; reconnect before reader expiry and verify locked recovery.
In a disposable acceptance scope, revoke membership/menu-read or withdraw images and check
bounded withdrawal. Restart locked and verify original-cashier resume, preferences and
protected recovery. Physical two-monitor and human acceptance remain pending until observed.

Service names: `nexaconnect-media`, `nexaconnect-restaurant`, `nexaconnect-platform-directory`,
`nexaconnect-authorization`, `nexaconnect-pos-client`. Filter structured logs by correlation ID
and `Gallery reader`, `Gallery authority`, `Gallery membership`, `Gallery permission`;
client operations are `gallery.reader.issue` and `gallery.reader.read`. Request logging covers
policy rejections. Never log/share readers, headers, bodies, subjects, image bytes or tokens.
