# Customer Service

Owns organization-scoped customer profiles. `POST` and `GET /api/customer/v1/organizations/{organizationId}/customers` provide the initial customer profile slice. Set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Customer` to use the parameterized PostgreSQL repository; development defaults to the in-memory adapter. Contacts, addresses, and loyalty projections remain follow-up work.

Customer Portal calls require the protected organization context to equal the route organization, active Platform Directory membership with enabled `nexa_connect` access, and `customer.profile.create` or `customer.profile.read`. Reads always filter by both `organization_id` and profile ID; tenant mismatches return `404`.
