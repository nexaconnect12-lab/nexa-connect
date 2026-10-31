# POS UX15 — unified Menu management

UX15 passes complete source verification and is activated in the retained local pilot on
31 October 2026. All twelve services are Ready, the served portal bundle contains the unified
workspace, and matching POS reopened responsively. Operator acceptance remains separate.
Existing Catalog9 schema and independent save contracts remain unchanged.

## Workflow

In Customer Portal's NexaConnect workspace, open **Menu management**, choose a branch,
then **Load menu**. Search by name and select an item. The shared selection provides:

- **Details**: edit name, THB price and preparation station; review before confirming.
- **Image**: choose from the product media library or upload JPEG/PNG/WebP, refresh after
  processing, inspect the preview, approve public display and save explicitly.
- **Availability**: confirm sold-out or available status for the selected item.
- **Categories & order**: manage branch categories and the selected item's category/order,
  then explicitly save the organization draft.

Branch, item and section switches ask before discarding unsaved drafts. Portal navigation,
workspace changes and sign-out use the same guard; browser unload uses its standard warning.
While a request is in progress these switches are blocked. Authorization/workspace invalidation
still clears scoped content. Search does not discard the selected item or its draft.
Conflict, denial, timeout or uncertain save invalidates the editor until **Load menu**.
The UI does not retry saves automatically. Image previews and approvals never transfer to
another item. Uploads and media deletions are independent durable operations: discarding an
assignment draft does not undo them. A successfully uploaded image still needs explicit selection
and public-display approval.

Each section retains its own authorization, CSRF token, expected version and save operation.
There is no combined save transaction. Availability versions never replace details versions.
Menu photos, screensaver assets and PromptPay QR configuration remain separate. **Media management**
retains approved screensaver galleries and directs menu-image work to **Menu management → Image**.
Historical standalone deep links `#menu-details`, `#menu-availability`, `#menu-organization` and
`#menu-images` remain supported for compatibility, but are absent from primary navigation;
the shared workspace's draft guards apply to the new `#menu-management` entry.

## Architecture and rollout

This is a frontend composition change. Existing BFF/API contracts, live authorization,
Catalog ownership, audit/outbox transactions and independent concurrency controls remain in use.
No schema, roles, secrets, payment methods, accepted orders or financial recovery changes are required.
Catalog9 / minimum compatibility0.44.0 remains the current baseline. An ADR is unnecessary:
the existing bounded contexts and transaction decisions remain unchanged.

Deploy the matching Customer Portal build through the normal source-bound pilot release workflow.
Preserve retained data, media approvals, certificates, settings and protected POS recovery.
Refreshing the local pilot still requires completing current POS work and closing POS first.
Implementation verification and installed-target/operator acceptance are separate.

## Verification

Candidate `9f1fb1bc-faec-49d3-b36f-829c239bf63c` passes the complete source-bound gate:
1,390 unit, seven architecture, 136 HTTP and 90 frontend unit tests; 75 QR, 34 portal
menu/gallery and 45 financial browser cases; native POS build/input/session/printing fixtures;
hosting/profile/evidence guards; thirteen owning persistence cases with broker recovery/cleanup
and four drawer-authority cases with cleanup. Both dependency audits pass. Twelve of the
34 portal cases cover the unified workspace. One initial WebKit menu-load timeout passed
in isolation and in the subsequent complete run, without a source change. The real authenticated
Phase 8 upload lifecycle harness is updated and syntax-checked but remains opt-in/unexecuted.

The portal browser suite covers shared selection/search; independent metadata, availability and
organization saves; unsaved-switch cancellation/discard; preview/approval isolation; failed previews;
save conflicts; busy navigation; and portal/workspace/sign-out guards, direct hash navigation and stalled-read recovery. Existing standalone editor
and gallery cases remain regression coverage. TypeScript, frontend unit/build and the complete
source-bound release gate must pass before packaging or activation.

Matching POS package `d81746ff59fa43a0ba989f3b294b2a44` is installed and validated against
the current source using the terminal inventory; the complete gate independently verifies its
full source inventory. Fresh QR pilot `0521b6a68a0d4a3180a322ffffe2c10c` retains the existing
origin and certificate bytes. Eight protected local files remain byte-identical before launch;
all eleven databases were backed up. Existing business columns/data compare unchanged in
30 Catalog, 54 POS, ten Restaurant, 31 Order and five Media tables (migration/outbox/inbox
metadata excluded); details versions and audit history are retained, without resetting them.
Scope/configuration/decrypted credential values match the backup; original encrypted secret
bytes were restored after comparison. Read-only PNG delivery, exact-workload denial and
Catalog9 runtime grants pass. No menu, approval, financial or drawer command was issued.

Fresh operator pack `0943922cb2554a4d8c74dac2b34d9d27` has a valid installed-target binding
and all seventeen cases pending; the fresh phone packs also remain pending. Actual manager/
POS/phone workflow acceptance and production release readiness are not inferred from activation.
