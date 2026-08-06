# Customer Service

Owns organization-scoped customer profiles. `POST` and `GET /api/customer/v1/organizations/{organizationId}/customers` provide the initial customer profile slice. The current adapter is in-memory; durable PostgreSQL persistence, contacts, addresses, and loyalty projections remain follow-up work.
