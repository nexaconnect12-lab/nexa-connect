# Restaurant provisioning API

Restaurant owns the initial restaurant and branch hierarchy. The Platform Admin BFF exposes a narrow onboarding proxy; ongoing restaurant configuration remains a NexaConnect Admin responsibility.

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
