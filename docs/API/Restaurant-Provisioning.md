# Restaurant provisioning API

## Customer branch management

`GET /api/restaurant/v1/customer/organizations/{organizationId}/branches` requires `customer-owner` or `customer-admin`, active Platform Directory access, and `restaurant.branch.read`. `POST` accepts `{ restaurantId, code, name, timeZone, currency }`; `PUT .../{branchId}` accepts `{ name, timeZone, currency, status, expectedVersion }`. Mutations require `restaurant.branch.manage`. Ownership is enforced by organization-leading queries. Invalid input returns `400`, denial `403`, missing hierarchy `404`, and code/concurrency conflicts `409`.

Restaurant owns the initial restaurant and branch hierarchy. The Platform Admin BFF exposes a narrow onboarding proxy, and the Product Owner compatibility portal exposes forms for these two proxy routes; ongoing restaurant configuration remains a NexaConnect Admin responsibility.

## Platform hierarchy directory

`GET /api/restaurant/v1/restaurants?organizationId={organizationId}` requires `platform-owner` or `platform-admin` and returns restaurants for the exact organization ordered by name and ID. Each item contains `restaurantId`, `organizationId`, `code`, `name`, `currency`, `timeZone`, and `status`. The corresponding BFF route is `GET /bff/platform-admin/restaurants?organizationId=...` and requires its `PlatformAdmin` policy.

`GET /api/restaurant/v1/restaurants/{restaurantId}/branches` uses the same authorization and returns that restaurant's branches ordered by name and ID. Each item contains `branchId`, `restaurantId`, `organizationId`, `code`, `name`, `currency`, `timeZone`, and `status`. The equivalent BFF route retains the same suffix. Both listing routes return `200` with an empty collection when no matching records exist, including an unknown restaurant ID. A missing, empty, or malformed `organizationId` query value and an all-zero owner identifier return `400`; a malformed restaurant path does not match the GUID-constrained route. Unauthenticated callers receive `401`, and authenticated non-platform administrators receive `403`. Restaurant owns and queries these records; the BFF does not access its database.

## Create or reactivate a restaurant

`POST /api/restaurant/v1/restaurants` requires `platform-owner` or `platform-admin`. The BFF route is `POST /bff/platform-admin/restaurants` and applies its `PlatformAdmin` policy.

```json
{
  "organizationId": "fd4cce08-ae47-453d-bac1-b7b2c7524f62",
  "code": "demo-restaurant",
  "name": "Demo Restaurant",
  "currency": "SGD",
  "timeZone": "Asia/Singapore"
}
```

The operation normalizes code to lowercase, name/time zone whitespace, and currency to uppercase. Code must match `^[a-z0-9][a-z0-9_-]{0,63}$`; currency must contain exactly three ASCII letters. It returns `201` with `{ restaurantId, organizationId, code, name }`. Repeating the organization/code updates reference fields and reactivates the same restaurant. Invalid input returns `400`; unauthenticated callers receive `401`, and authenticated non-platform administrators receive `403`.

## Create or reactivate a branch

`POST /api/restaurant/v1/restaurants/{restaurantId}/branches` has the same authorization requirements. The BFF route uses the same suffix.

```json
{
  "code": "main",
  "name": "Main Branch",
  "currency": "SGD",
  "timeZone": "Asia/Singapore"
}
```

It returns `201` with `{ branchId, restaurantId, organizationId, code, name }`. Repeating restaurant/code updates and reactivates the same branch. Validation failures return `400`; an absent or inactive parent restaurant returns `404`. Logs record only scoped identifiers and safe status information.
