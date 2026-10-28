# ADR-040: Branch-approved gallery and leased POS screensavers

- Status: Accepted
- Date: 2026-10-23

Restaurant owns the branch's ordered staff/customer selection and public text as the
`ScreensaverGallery` property in existing `branches.business_configuration`. Media keeps
ownership of uploaded/scanned Catalog product assets, storage keys, processing and deletion.
Selection uses the branch concurrency version and existing configuration audit/outbox transaction.
No new table, migration, event vocabulary or permission is introduced.

Selection approval requires live branch `restaurant.configuration.manage`; selected assets
also require the caller's organization-level Media read authority and a ready display variant.
Approval means explicit authorized selection of content that passed the existing Media lifecycle,
not a new human moderation service. Upload authority remains `media.asset.manage` separately.
Restaurant calls Media only for readiness; that operation never calls Restaurant back.

Delivery uses the cashier's existing customer bearer. Media asks Restaurant for the current
branch selection under live `catalog.menu.read`, then delivers only those ready, non-deleted
Catalog-product display variants. Menu-read authority permits only selected public product
content, not organization-wide Media listing/download. The organization/branch must match
at both boundaries. No workload/admin token is shipped to POS or browser.

Media decodes bounded processed variants into PNG for native Windows compatibility. A
version-one bundle contains the filtered selection, image bytes and SHA-256, issue time and
a maximum five-minute lease. Missing/deleted/unready assets are excluded on each delivery;
provider errors fail the request. Validation and reads are not a distributed transaction:
revocation/removal can remain visible until a previously issued lease expires.

POS validates scope, size, hash, PNG header/dimensions and full WPF decode before display.
It atomically replaces one current-user DPAPI cache, without tokens/URLs, and never merges
removed slides into a new bundle. At most six slides per role, twelve unique images,
2 MiB per PNG and 16 MiB encoded images per bundle are allowed. Cache ciphertext is bounded
at 24 MiB; atomic replacement can temporarily occupy another file of the same bound.
While authenticated and active POS refreshes once a minute, independently of cashier activity.
Lock stops authenticated refresh but may use its remaining approved lease. Expired/invalid
content falls back to neutral branding. Explicit denial and sign-out clear cached approval.

One existing lock drives both screens. Staff shows its slideshow with original-operator
Resume/Authenticate. Customer shows only its separate approved slideshow/public text, with
no transaction or authentication controls. Media failure/latency must never block lock/resume
or enter the financial pipeline. Cached promotion is not offline authorization or sales support.

On 27 October 2026, presentation expands approved images across each assigned monitor,
preserving aspect ratio and cropping edges to fill. A compact staff overlay keeps resume
and sign-out available; the customer view retains no controls. Staff fullscreen uses physical
monitor bounds and restores prior window geometry/chrome on resume. A monitor lease rejects
ambiguous/overlapping layouts and changed topology; existing privacy fencing remains active.
This trades complete edge visibility for full-display imagery without changing approval,
authentication, lease or financial authority. It does not establish OS kiosk restrictions.

A subsequent 27 October refinement initially hides the staff authentication panel. A first
tap/click/keypress reveals it centered without starting authentication; Resume is a separate
action. Escape/Back returns to the slideshow while locked. Held wake-key events cannot
activate Resume, and active authentication keeps cancellation visible. Customer presentation
remains free of controls. This preserves unobstructed imagery without weakening the original
operator identity check or exposing operational content.

Alternatives: a new Media-owned branch configuration table would duplicate Restaurant scope
ownership; unrestricted signed URLs/indefinite cache would weaken withdrawal and least privilege;
a new gallery role is unnecessary for this bounded public selection. The short lease trades
long offline promotion for a clear withdrawal limit. Public-serving infrastructure, real provider
and physical display acceptance remain deployment/UAT gates.

See [contract](../../API/Screensaver-Gallery.md) and [operations/evidence](../../Deployment/Next-Iteration-Phase-5.md).
