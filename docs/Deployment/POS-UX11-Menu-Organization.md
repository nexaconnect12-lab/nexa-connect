# POS UX11 — menu categories and organized browsing

UX15 consolidates the manager workflow below into **Menu management**: choose a branch,
**Load menu**, select an item, then open **Categories & order**. The standalone workflow below is retained
for historical/compatibility deep links. See [current workflow and draft guards](POS-UX15-Menu-Management.md).

UX11 is activated in the retained local pilot on 31 October 2026 with Catalog7 and the
matching POS package. All twelve services are Ready. The QR origin/certificates, terminal
scope, payment configuration and existing image approvals are retained. Actual manager,
POS and phone workflow acceptance remains separate from this verified local installation.

## Manager workflow

1. In Customer Portal choose the NexaConnect workspace and **Menu categories**.
2. Choose **Menu organization branch**, then **Load menu organization**.
3. Enter category names and choose **Add category**. Rename categories in place and use **Up/Down** to arrange category display order.
4. Find menu items by name and choose their category from the searchable dropdown. Use item **Up/Down** within a category to arrange products. Approved photos are shown where available; missing images do not block editing.
5. **Remove category** returns its products to Uncategorized in the local draft. Products, images, prices and availability are retained. Click **Save menu organization** to apply the complete draft.
6. Refresh Windows POS and phone QR menus. A stale/uncertain save invalidates the editor: reload before editing or retrying. Branch/workspace changes discard unsaved drafts and cancel stale requests.

At most50 categories and200 products can be organized per branch, matching the guest-menu bound. New or unassigned products appear in Uncategorized. This slice organizes existing products; it does not add modifiers, stock availability editing, combos, kiosk ordering or category-specific pricing.

## Cashier and guest browsing

Windows POS **Refresh menu** loads optional categories using the current configured branch and live menu.read permission. Category, text and existing station filters compose. **Clear filters** resets category, text and station. Filtering/sorting uses UI wrappers around original financial menu objects; barcode lookup, cart, original quotes, payments and recovery are unchanged. Metadata failure shows all items and an unavailable-category message. Late responses are fenced by session/menu generations; lock/sign-out/menu reload clears category labels and resets the filter. Categories are not persisted in protected recovery. Refresh again after resuming if category labels were cleared.

QR menus show the same published category and product order, plus text search, approved images and current availability. Selecting categories or searching never removes cart items or alters a reviewed quote. Layout failure falls back to the complete text menu. Actual cart edits, menu refresh, quote expiry and visibility changes retain their existing review rules. Guest ordering/payment authority is unchanged.

## Rollout and prerequisites

Apply Catalog7 with compatibility0.42.0 before deploying matching Catalog, Order, Customer BFF/frontend, guest edge and Windows POS. Keep all other owning schemas, existing service/workload addresses and authentication policy unchanged. No new grant is added: managers use live branch catalog.menu.write, readers use catalog.menu.read, and only exact Order workload may use the private Catalog route. Scope discovery uses the corrected reusable Restaurant branch adapter; repeated checks remain live. See [API/diagnostics](../API/Menu-Organization.md) and [schema/grants/rollback](../Database/Menu-Organization.md).

Back up retained databases and protected files before activation. Close POS before switching its package; preserve original cashier/shift/payment recovery, input/density/printer preferences, approved playlists, payment configuration, certificates and QR entries. Existing images remain explicitly approved under their separate photo workflow; category saves do not approve images. Drawer opening remains disabled pending vendor details. Keep deployment/phone/POS acceptance records bound to the fresh candidate; carry forward no operator pass.

## Verification

Source candidate `617f0042-4e4e-4975-a4af-fa1707d26112` passes the complete gate:
1,379 unit tests,7 architecture tests,127 HTTP checks,90 frontend unit tests,75 QR browser
cases and15 portal gallery/category cases, plus existing financial browser matrices and
isolated native fixtures. Eleven owning database cases verify cleanup/broker recovery;
four drawer owning cases verify cleanup. Both dependency audits report zero vulnerabilities.
Matching source-bound POS package `f032275d95f34de2aee991d9837a6cfe` is installed, with fresh
QR pilot `7bd844f5827e46008eafa1e395f57d7f` and the same HTTPS origin. Ten protected local
files remain byte-identical before launch; all eleven pilot databases were backed up.
Preservation comparison covers26 Catalog/54 POS/10 Restaurant/31 Order/five Media original
business-table COPY sections, all unchanged (migration/outbox/inbox metadata is excluded).
Catalog7 runtime grants, empty default layout, safe image delivery, exact-workload denial,
trusted HTTPS and guest-cookie requirements pass read-only checks. The closed POS's leftover
windowless process exited independently; recovery integrity/counts are unchanged. This
observation does not diagnose or fix the earlier shutdown-remnant issue.
Documentation audit passed; no actual category save, sale or hardware action was performed
against retained data during verification.

The source gate completed on 30 October at 22:24 Singapore time; activation completed on
31 October at 00:13. These are separate observations. Read-only layout returned version0
with status200; wrong workload403 and guest layout without a cookie401 were verified.
Fresh operator pack `c5fb731445574ea8a405acfa039bd735` has a valid installed binding and
all 17 cases pending. Its `qrCandidateFresh=true` does not change `currentSourceVerified=false`,
`operatorCasesComplete=false`, `releaseReady=false` or `productionVerified=false`.

- Domain/Application/native tests: `MenuOrganizationTests`, `MenuOrganizationTransportTests`, `CatalogBranchScopeReuseTests`, `QrMenuTests`.
- HTTP: `MenuOrganizationBoundaryTests`, `QrMenuTransportTests` and existing scoped guest/BFF tests.
- Owning PostgreSQL: `MenuOrganizationPersistenceTests` via the generated QR fixture.
- Browser: gallery suite includes category creation/assignment/order/removal/conflict/branch clearing and existing photo approval; QR Chromium/WebKit/desktop includes category/search/cart preservation and failure fallback.
- Native: hidden Windows fixture verifies category ordering/filter composition, unchanged original item/cart, privacy clearing and invalid optional-data fallback. Existing photo/scanner/payment/session/density tests still run.
- Full source-bound candidate: `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure`; new tests must execute without skips/retries and disposable infrastructure must be cleaned up.

Actual installed manager creation/save, photo approval, Windows keyboard/mouse/touch and iOS/Android category browsing remain operator acceptance. Automated mocks and synthetic identities do not complete those observations or release readiness. Offline layout caching, multi-level categories, multi-category products, multi-image galleries and bulk drag-and-drop are outside UX11.
