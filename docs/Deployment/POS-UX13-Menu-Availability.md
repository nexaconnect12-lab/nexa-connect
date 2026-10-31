# POS UX13 — branch menu availability

UX15 consolidates the manager workflow below into **Menu management**: choose a branch,
**Load menu**, select an item, then open **Availability**. The standalone workflow below is retained
for historical/compatibility deep links. See [current workflow and draft guards](POS-UX15-Menu-Management.md).

## Status and manager workflow

UX13 passes the complete source gate and is activated in the retained local pilot on
31 October 2026 with Catalog8 and the matching POS package. All twelve services are Ready,
and POS reopened with a responsive window. Actual manager/POS/phone workflow acceptance
remains separate from installation.
The user reports UX12 normal reopening working;
this does not complete unrelated operator acceptance cases.

1. Open Customer Portal **Menu availability** in the NexaConnect workspace.
2. Select **Availability branch**, then **Load availability**.
3. Search by product name. Choose **Mark sold out** or **Make available**, then confirm.
4. Refresh Windows POS and phone QR menus. POS shows a disabled **Sold out** tile; QR keeps
   unavailable items visible and disables adding them. Existing cart lines remain for review.
5. On conflict, denial, timeout or uncertain result, reload before another edit. Changing
   branch/workspace clears rows and cancels stale responses. At most200 items are supported.

Only current branch `catalog.menu.write` can change availability. Readers use menu.read.
Cashiers/guests receive no management authority. The existing availability flag is separate
from stock, category order, photos, prices and payments; metadata edits preserve sold-out
status. Review quotes and staff acceptance check current availability. Previously quoted
requests may still enter staff review; acceptance conflicts while items are unavailable.
Staff can reject them or reconsider after restoration. Accepted orders and original payments
retain their snapshots, idempotency and recovery. This is not a distributed selling cutoff:
an order validated before a concurrent change can proceed. See [API](../API/Menu-Availability.md).

## Rollout and recovery

Back up retained databases and protected POS files. Stop old Catalog writers, apply Catalog8
with compatibility0.43.0, then deploy matching Catalog/BFF/portal/POS. Keep other schemas,
scope, URLs, certificates, galleries, printer/input preferences and original financial recovery.
Do not mix older metadata writers that reset availability. Existing values start at version0;
the migration does not mark anything sold out or restore existing unavailable items.
No new environment variable, client, grant or device-opening capability is added.

Close POS before switching packages. Verify the fresh source/pilot/package binding and
preservation before reopening. No retained availability writes, orders, payments or hardware
commands are required for activation. [Downgrade](../Database/Menu-Availability.md) is permitted
only before availability history; otherwise forward-fix while preserving original rows.

## Verification and pending acceptance

Candidate `94e9fd93-48ec-49a7-8caa-f99ed25bc198` passes all gates: 1,384 unit tests, seven
architecture tests, 131 HTTP checks, 90 frontend unit tests, 75 QR browser cases, 18 portal
gallery/category/availability cases, 45 existing financial browser cases, native input/
shutdown/printing fixtures and hosting/profile/evidence guards. Twelve owning database cases
pass with cleanup and broker recovery; four drawer-authority database cases pass with cleanup.
Both dependency audits report zero vulnerabilities. The corrected pending-versus-accepted
fixture is part of this final source-bound run; earlier incomplete/failed runs are not reused.
Matching source-bound package `f247c4d52c054b27b91842c747ff0eda` is installed with Catalog8.
No live availability or financial commands were issued. Operator, device and public/production
readiness remain separate.

Activation completed at 06:18 Singapore time on 31 October. Fresh QR pilot
`efe06df53e0c46ca84880a87155a768e` retains the original HTTPS origin and certificate bytes.
Eight protected local files are byte-identical before launch; all eleven pilot databases were
backed up. Original columns/data compare unchanged across 28 Catalog, 54 POS, ten Restaurant,
31 Order and five Media business tables, excluding migration/outbox/inbox metadata. Catalog's
new availability_version column is separately verified as zero for all existing rows and the
new audit table is empty; this is a data-preservation comparison, not byte-identical whole
Catalog COPY sections. Existing layout version1, image approvals and payment settings are retained.
Catalog8 runtime grants and safe PNG delivery pass. Wrong-workload layout/management reads
return403, and guest layout without a cookie returns401.

Fresh operator pack `efeebf483bed4f1c8611b6d5c503ba84` has a valid installed binding and all
17 cases pending. `qrCandidateFresh=true` does not change `currentSourceVerified=false`,
`operatorCasesComplete=false`, `releaseReady=false` or `productionVerified=false`.

Tests include domain version/no-op/overflow, live scope, actual Catalog/BFF HTTP session/
CSRF/cookie/membership/error handling, manager confirmation/conflict/branch clearing,
native sold-out label/cart isolation, and owning PostgreSQL CAS/audit/outbox/rollback/upsert.
The QR owning journey checks that sold-out items block fresh quotes and pending acceptance,
and that an already accepted request still replays the original order after a flag change.
Existing QR browser availability/stale-quote tests remain in the full gate.

Run `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` on the Windows development PC;
the full source-bound gate includes owning database cleanup and broker recovery. No skips,
automatic retries or synthetic passes count as operator acceptance. Physical drawer opening
stays disabled. Automation uses isolated/synthetic data, not retained orders or merchant funds.

After activation, test sold-out/restore on POS, iPhone Safari and Android Chrome; include a
cart created before the change, a pending staff-review request and original accepted-order
collection. Check authorized and denied branch access. Record those actual observations
separately; public/production readiness, physical hardware and other pending cases are not
inferred from source tests or successful installation.
