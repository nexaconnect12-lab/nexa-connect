# Shared photo library and contextual picker

This first implementation slice follows the user's iOS Photos direction using the existing React/Ant Design portal. **Media management → Photo library** starts with a responsive thumbnail grid and switches to **Details** without discarding the current search/page. Filename search and **All photos / Ready** are server-paged in 24-photo windows. Refresh resets to page 1. Views reset to thumbnails on a new workspace or newly opened picker; view preferences are not persisted yet.

The details list shows filename, type, size, upload time, status and actions. A grid photo opens a larger detail preview. Authorized users can explicitly download a ready original or confirm deletion through the existing lifecycle routes. Deleting may leave menu/screensaver placeholders until a manager replaces the reference; it does not clear those references automatically. Denial, timeout, processing and empty states do not invent demo photos.

## Upload, then select

1. In **Media management → Upload photos**, choose up to ten JPEG, PNG or WebP files, up to 10 MiB each. Click **Upload photos** once. The browser uploads one file at a time and reports each file's result. Each attempt has a 60-second timeout and uses the existing checksum, scanning and quota checks. Check the library before resubmitting an unconfirmed file. Use **Refresh library** to see when processing finishes.
2. Open **Menu management**, select the branch, **Load menu**, select an item and open **Image**. This editor selects photos from the shared library. Upload and branch approval are separate actions.
3. Click **Choose from photo library**. The popup starts with ready thumbnails; search/page or switch to Details. Select a photo, then **Use selected photo**. **Cancel** leaves the editor's selection unchanged.
4. Check the branch/item preview, explicitly approve public menu display, then **Save menu image**. A failed preview cannot enable approval. Conflict or uncertain save requires authoritative reload; the popup does not bypass Catalog authorization, CSRF or expected-version rules.
5. For a screensaver, open its approved gallery, select the branch and use the same popup for staff/customer slides. Existing same-role photos are excluded and each role remains limited to six. Ordering/text edits and explicit gallery approval/save remain separate from picking; customer and staff playlists do not merge.

Private product thumbnails use same-origin BFF routes and no-store responses. Bank PromptPay QR images are purpose-separated and never appear in this photo library. Menu selection remains Catalog-owned, gallery approval Restaurant-owned and bytes/lifecycle Media-owned. See [API/security](../API/Media-Library.md) and [persistence](../Database/Media-Library.md).

## Deployment and verification

Deploy matching Media, Customer BFF and Customer Portal builds. The shared upload follow-up uses the existing Media4/Catalog9 schemas, permissions, object storage and scanner. The [optional local Media profile](First-Draft-Media.md) must be enabled and healthy. Local activation requires finishing cashier work and closing POS before services restart. The LAN pilot deployment is recorded below. The retained activation later in this guide records the earlier browse-and-picker slice.

### Physical LAN pilot activation — 6 November 2026

After the operator confirmed POS closure, the existing physical LAN pilot was backed up
and refreshed. The comparison retained all 501 original business rows across 203 checked
tables and all 16 checked scope/key-ring files. Cashier sign-in, order history and terminal
authorization checks passed. The manager portal at `https://127.0.0.1:56464/` passed a live
check of the multiple-photo control, the authorized library read and the menu selection
picker. The menu editor offered photo selection only. The live check recorded zero media
writes. Photo upload, processing and on-terminal display remain operator acceptance steps.
This activation applies to the portal and LAN services. The staged native installer remains
version 0.8.4.

Relevant verification: `MediaLibraryTests` and `CustomerMediaLibraryTests`; HTTP `MediaLibraryBoundaryTests` and `CustomerMediaLibraryBoundaryTests`; generated PostgreSQL `MediaLibraryPersistenceTests` through `scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure`; frontend unit/type checks and `npm run test:e2e:gallery` from `src/Frontend`. The full source-bound candidate gate remains `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure`. Tests cover organization/purpose/revocation, bounded bodies, literal-search pagination, picker cancellation/draft approval, view switching, deletion confirmation and workspace reset. Browser fixtures are simulated; real upload/storage, iPhone/Android and installed-target operator acceptance remain separate.

### Source verification — 3 November 2026

Final candidate `9c8f99ff-80fb-48ed-8866-c5127169ecbd` passes the complete source-bound gate:
1,446 unit, seven architecture, 147 HTTP, 99 frontend unit and 161 browser cases (including
41 gallery/menu/library cases); native build, print-layout, scanning and session fixtures;
14 owning persistence cases and four drawer-authority cases, with verified cleanup. Both
npm dependency audits report zero vulnerabilities. The final Git-filtered source inventory
and the native package inventory were independently checked against current inputs.

An earlier candidate was correctly rejected by the source guard after the opt-in Phase 8
lifecycle harness was corrected for the new picker/library selectors. That rejected run is
not the final source evidence. The corrected harness passes syntax checking but its joined
real-stack upload lifecycle was not executed for this slice.

### Initial retained local activation — 3 November 2026 (historical)

This initial binding is superseded by the later [Customer Portal banner refinement](../../src/Frontend/apps/customer-portal/README.md). The following identifiers and observations remain historical; do not transfer their acceptance results.

After user-confirmed POS closure, matching 23-file release
`a73388b10c6545539c49e7d9d9732a08` was installed on the retained run, with fresh QR pilot
`f50662a0611d4b87ae0df021823b49bd` and supervised host
`edf49e2df7b64b37b5173ee74da69231`. The host runs independently under the current-user
scheduler; automatic sign-in startup remains off. POS reopens responsively.

Preservation checks confirm nine protected local files and 159 business tables unchanged;
credential values, configuration, certificate bytes, reference scope and URLs remain the same.
Read-only real authenticated checks return the cashier menu (three items), manager library
(14 ready photos) and a bounded no-store PNG thumbnail (125982 bytes); anonymous library
access is denied. Trusted HTTPS portal and guest ordering return 200, while the guest
Kitchen path returns 404. These checks issue no upload, assignment, approval, financial or
hardware command and do not certify the new joined upload lifecycle.

A real-manager installed-portal read-only smoke check also passes: all 14 existing photos
appear in the default grid with loaded thumbnails, Details exposes the expected columns,
and **Menu management → Image → Choose from photo library** opens with 14 ready photos,
no initial candidate and working cancellation. A private test-setup attempt initially missed
the SPA reload after selecting the session tenant; correcting that setup required no product
change. No media, approval, order or payment mutation was issued. This joins the served React
UI and authenticated BFF reads; it does not replace the pending operator checklist or joined
upload/scanning/deletion lifecycle test.

The then-fresh operator pack `ea275d1df8f04e84ae2d1ff6f9c6ba5b` had a valid installed binding and
fresh QR candidate, with all seventeen cases pending. Historical acceptance passes are not
transferred. Source/runtime checks grant no operator/phone/hardware acceptance, public
release readiness or production verification.

## Next slices

This is a reusable portal component/API foundation, currently consumed by menu-image and screensaver editors. It does not yet add a native POS picker, other-product portals, arbitrary unowned uploads, multi-file drag/drop, bulk actions, metadata renaming/display names, replacement, documents/videos, custom sorting, uploader display or persisted view/filter preferences. Those remain in the [expansion roadmap](../Planning/Product-Expansion-Roadmap.md). Continue incrementally on the existing Media metadata/storage and safety pipeline rather than introducing a second library/store.
