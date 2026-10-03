# Authoritative checkout pricing

The online THB checkout now uses Restaurant-owned branch settings and an Order-owned commercial snapshot. POS previews server pricing, asks for confirmation, and saves the quote fingerprint with the original checkout identity before submission. This slice does not add discounts, modifiers, refunds, offline quoting, fiscal receipts or printer support.

## Pricing policy

Restaurant's existing customer configuration GET/PUT and Customer BFF configuration route add `taxPercent` (0–100, at most two decimals) and `taxInclusive` (boolean). GET also supplies branch `currency`. Missing stored tax settings mean zero tax/exclusive; omitted PUT tax fields preserve current settings. Explicit zero/false resets them. Service-charge percentage is also limited to two decimals. Existing scoped configuration read/manage permissions, expected-version fencing, transactional audit and outbox remain in use. No Restaurant migration is needed: settings extend its owned JSON configuration.

The internal `GET /api/restaurant/v1/branches/{branchId}/pricing` requires the service-workload policy, exact `azp=nexaconnect-order-service`, organization header and `X-Nexa-Application-Code: nexa_connect`. Only active THB branches under an active restaurant in the requested organization are returned; other scopes return `404`, invalid workload/context `403`. Order verifies all returned scope identifiers.

Order calculates once per quote over the whole bill:

1. Menu amount is the sum of authoritative Catalog unit prices times quantities.
2. Exclusive prices: subtotal equals menu amount. Inclusive prices: subtotal is menu amount divided by `1 + taxPercent/100`, rounded to two decimals.
3. Service charge is the rounded subtotal times `serviceChargePercent/100`.
4. Exclusive tax is rounded tax on subtotal plus service charge. Inclusive tax is menu amount minus subtotal, plus rounded tax on service charge.
5. Total is subtotal plus service charge plus tax, exactly.

Rounding uses decimal arithmetic and midpoint away from zero. Inclusive prices preserve the original menu amount exactly when service charge is zero. Example: THB 107 inclusive at 7% with 10% service charge produces subtotal 100, service charge 10, tax 7.70, total 117.70. This is an explicit application policy, not jurisdiction-specific tax certification. One rate applies to the entire bill, including service charge; mixed classifications, exemptions and line-level tax allocation are outside this slice.

Quotes accept at most 200 distinct products and 10,000 units per product. Catalog prices must have at most two decimals; the menu amount must be positive and no greater than THB 99,999,999. Invalid configuration, unavailable items and unsupported currency fail closed.

## Quote and place

`POST /api/order/v1/workflows/quote` accepts the existing placement scope, currency, payment method and product/quantity lines. It does not create an order or reserve inventory. Customers require tenant context and `order.place` for the branch; existing trusted workload placement access remains. Arbitrary client prices/totals are not used. Both quote and place cap request size at 64 KiB.

The quote returns `{ fingerprint, pricing, lines }`. Pricing contains `policyVersion`, tax mode/rate, service rate, menu amount, subtotal, service charge, tax and total. Lines contain the server-resolved product/name/unit price/quantity/preparation-station snapshot. The SHA-256 fingerprint covers scope, payment method, policy/version, calculated breakdown and lines; it is a comparison token, not authentication or a reusable payment credential. Quotes are not stored and do not reserve a price.

`POST /api/order/v1/workflows/place` adds `pricingFingerprint`. First placement recalculates against current dependency responses. Missing/stale fingerprints return `409` with `code=pricing_changed` before persistence or downstream effects. The caller must obtain and explicitly confirm a new quote. A policy update after those reads does not retroactively change an accepted order: acceptance uses the snapshot read by that attempt, without a distributed lock across Restaurant/Catalog/Order.

An existing matching checkout returns its stored result before consulting Catalog/Restaurant. Scope, method, products/quantities and the accepted fingerprint must match; conflicting identity reuse is rejected. The original order ID and idempotency key must survive retries, including price reconfirmation. A concurrent winner owns workflow advancement; other requests read its result. Intermediate states remain subject to the existing recovery worker. Pricing mismatch is not permission to collect payment again or generate another order identity.

Placement and `GET /api/order/v1/orders/{id}` expose optional `pricing`; historical unpriced orders return null and retain legacy calculation behavior. Existing recovery accepts their original matching requests without requiring a new quote. The client-priced `POST /api/order/v1/orders` endpoint returns `410`; callers must migrate to quote/place. No new HTTP checkout can select a caller-supplied unit price.

Malformed input returns `400`, customer denial `403`, dependency HTTP failure `503`, pricing reconfirmation `409`, and other invalid workflow transitions retain `422`. Uncertain transport outcomes retain the original recovery identity.

Customer BFF adds `/bff/customer/orders/branches/{branchId}/quote` alongside `/place`; both derive organization from the protected tenant cookie, revalidate access, retain customer-session/CSRF middleware, and forward only the typed contract. `pricingFingerprint` is forwarded on placement. The current portal change supplies the branch pricing controls; the cashier confirmation UI is in WPF POS.

## Persistence and settlement

Apply Order migration **8**, minimum application version **0.17.0**, before this Order binary. It adds immutable `pricing_snapshot`, `pricing_fingerprint`, and a unique restaurant/`placement_key`. Snapshot, lines, idempotency and initial event commit in the same transaction. Subsequent writes preserve accepted pricing and preparation stations. Durable placement-key lookup survives expiry/removal of technical idempotency records. Payment and manual settlement use the snapshot's final amount; manual settlement still requires exact equality, tenant ownership, permission decisions and its existing idempotency fence. Existing POS cash projection receives that final amount unchanged.

Legacy rows are not backfilled or repriced. Migration `8→7` refuses while any priced order exists, even with destructive confirmation. Roll forward after acceptance; do not deploy an older Order binary over priced history. Empty pricing migrations can be removed/reapplied. Migration steps commit individually, so a lower-version guard can fail after earlier downgrade steps committed; inspect migration status and roll forward.

POS stores the confirmed fingerprint and a `needsPricingReview` flag in the protected pending-checkout payload, plus the returned breakdown in pending settlement. A price change retains the same checkout identity and requires another explicit quote confirmation. Restart and uncertainty continue through Verify original order. Declining an initial quote creates no recovery row. Declining a requote preserves the pending checkout. No offline pricing authorization is introduced.

## Deployment, diagnostics and verification

Deploy Restaurant first, migrate Order to 8, then deploy Order, Customer BFF and POS together. Retain `Services__Restaurant`, the Order workload's valid Restaurant audience, and its client-credentials settings. Existing configured service charges become effective on new priced orders: operators must review branch settings before rollout. Disable new checkout during a mixed-client rollout; old clients cannot create new orders without confirmation. Existing accepted recovery remains supported.

Services retain the shared observability foundation and validated outbound correlation IDs. Query `{service_name="nexaconnect-order"} |= "Order pricing"`, `{service_name="nexaconnect-restaurant"} |= "Branch pricing"`, and Customer BFF logs for `Customer order boundary rejected`. POS `nexaconnect-pos-client` records `order.quote`/`order.place` boundaries. No prices, bill bodies, tokens or payment data are logged by the added events.

Run unit tests with `OrderPricing|ProductConfiguration|PosCheckout|CustomerOrderPort`; HTTP tests with `OrderPricingHttp|RestaurantWorkflowCrossService|OrderTenantAuthorization`; architecture tests with `OrderPricing`. Run `scripts/test-order-pricing.ps1 -ConfirmDisposableInfrastructure` for three no-skip PostgreSQL cases: pricing/concurrency/settlement/downgrade guards, Restaurant scope/version/audit, and the actual migration runner. It generates a localhost-only PostgreSQL container, restores process environment variables, retains TRX under `.runstate/order-pricing`, and removes its exact container/volumes. Never substitute production infrastructure.

Fresh physical WPF confirmation/restart acceptance, live OIDC, broker delivery to cash-close Reporting, provider acceptance, and production rollout remain separate gates. Component/HTTP and PostgreSQL checks do not certify those environments.

Local PostgreSQL acceptance passed **3/3, zero skips**, on 2026-09-28, with exact-container cleanup. Retained TRX: `.runstate/order-pricing/355825de17fd40aba20dba00b8700f20/pricing.trx`. The run exposed and corrected an ambiguous branch concurrency update and disposal-before-rollback defect in the existing configuration repository, plus an untyped nullable workflow SQL parameter.
