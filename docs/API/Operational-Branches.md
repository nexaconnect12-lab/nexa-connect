# Operational branch discovery

`GET /api/restaurant/v1/customer/organizations/{organizationId}/operational-branches?capability=...`
returns active `BranchSummary` rows that pass live Platform Directory access and an exact
organization/restaurant/branch Authorization decision. Customer realm roles
`customer-owner`, `customer-admin`, `customer-user` and `customer-viewer` can enter;
realm roles alone grant no product access. Suspended, closed and denied branches are omitted.

| Capability | Required product permission |
| --- | --- |
| `kitchen` | `kitchen.ticket.read` |
| `day-close` | `pos.day-close.read` |
| `reports` | `reporting.dashboard.read` |
| `payment-review` | `order.payment-review.read` |
| `catalog` | `catalog.menu.read` |

Unknown capabilities or invalid organization IDs return 400. No authorized active
branches returns 200 with an empty array. Authentication/realm denial is 401/403;
HTTP dependency failure returns 503 without partial rows. Responses use `no-store`.
No station registry or new schema is introduced. Kitchen station choices derive
from the existing authorized Catalog menu; all-stations still includes retained
tickets whose historical station code is absent from today's menu.

Customer BFF `GET /bff/customer/operational-branches?capability=...` resolves the
organization from its protected, live-validated tenant selection, forwards only
the current customer bearer and validated correlation context through the Restaurant
client, and uses no-store responses. Browser IDs and selector choices are never
authorization proof; each owning workflow reauthorizes its commands and reads.
The admin branch-management route and its stronger permissions remain separate.

Service names: `nexaconnect-restaurant`, `nexaconnect-customer-bff`. Query structured
logs for `Operational branch discovery` / `Operational discovery downstream failure`
and `CorrelationId`; no tokens, cookies, bodies, arbitrary headers or financial values
are logged. Discovery has no cached permission snapshot; reload after access changes.

Kitchen, Payment Review, end-of-day, sales and cash-close reports use selectors.
Changing branch clears prior workflow state; tenant changes remount operational
panels. Discovery failure blocks selection and prompts a reload. Store UUID remains
a required cash-close report filter; the draft terminal configuration provides it.
