# ADR-045 — QR menu and authoritative cart preview

Status: accepted for QR2, 23 October 2026.

QR2 extends QR1 visitor admission with an online menu and transient cart. Restaurant
continues to own admission, table labels and branch pricing policy; Catalog owns menu
price/availability; Order owns commercial resolution. Customer BFF adapts the guest cookie
to explicit capability requests. No staff token is forwarded and no staff API becomes
anonymous. Adding a separate guest pricing engine or trusting browser prices would drift
from cashier policy; forwarding a staff identity would give guests excess authority.

Order's new capability-only menu/quote use case resolves Restaurant context on each
request, selects the organization/branch from that response, then revalidates context
before returning. It uses a distinct credential-free Restaurant context client and an
exact Order workload-only Catalog organization/branch projection. Catalog's bounded
parameterized read never includes another organization's rows, including the same branch
UUID under another owner. Its private station snapshot is used in the shared existing
Order quote resolver and removed from public responses. Restaurant pricing retains the
existing exact Order workload policy. No cross-service SQL, new credentials or permissions.

Guest requests contain product UUIDs/quantities only. Unknown JSON fields are rejected;
organization, table, currency, prices and totals are never guest authority. The cart is
limited to 50 distinct products and 1–99 units each; the menu supports 200 items and fails
closed on overflow rather than silently hiding items. Current currency remains THB.
Catalog availability is a menu flag, not a live stock guarantee. No media URLs/assets are
added because a guest publication policy is not yet implemented.

Order uses existing decimal, inclusive/exclusive tax, service-charge and rounding rules.
The quote fingerprint is an unstored comparison value, not authorization or a reserved
price; expiry is at most two minutes and bounded by the visit. Menu/quote requests perform
no persistence, stock reservation, Kitchen, payment or audit/outbox mutation. QR3 must
define durable guest request identity, exact reviewed snapshot and recovery; QR4 must
revalidate and obtain review of changes before acceptance. Cashier payment remains QR5.

The browser stores quantities only in component memory. Reload/end/expiry clear the cart;
edits, menu refresh, return to the visible page and detected offline state clear totals.
Request generations reject late quotes after edits. Updated menu prices or a changed preceding quote fingerprint for the same cart
produce visible review feedback. Only server-returned totals are displayed as totals.
Both menu and quote use the QR1 HTTPS cookie boundary; quote requires exact Origin/JSON.
Shared BFF admission and a new Order global 1,000/minute bound apply. Default-off flags
allow QR1 context without QR2. No offline authority or permanent customer profile.

Generated pilot business-service edges reject guest capability paths alongside internal
paths; browser access uses the Customer BFF origin. Pilot configuration validation retains
disabled QR flags until the later hosted QR rollout and ingress verification gate.

See [contract](../../API/QR-Menu.md), [operations/evidence](../../Deployment/QR2-Menu.md)
and [QR1 admission decision](ADR-044-table-qr-entry-and-guest-sessions.md). Actual phones,
hosted trusted TLS and clean-source CI/release acceptance remain separate gates.
