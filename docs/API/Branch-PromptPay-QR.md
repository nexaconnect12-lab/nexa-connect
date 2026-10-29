# Branch PromptPay QR API

All routes are authenticated, no-store and correlated. Application use cases enforce live
tenant/branch authority. No payment-provider or settlement endpoint is added.

| Owner | Route suffix | Behavior |
| --- | --- | --- |
| Restaurant | `GET /api/restaurant/v1/customer/organizations/{org}/branches/{branch}/promptpay-qr` | Menu-readable active THB selection: organizationId, restaurantId, branchId, version, nullable assetId |
| Restaurant | `GET .../promptpay-qr/manage-access` | Same metadata with live configuration.manage |
| Restaurant | `PUT .../promptpay-qr` | Configuration.manage; expectedVersion, nullable assetId, approved=true; returns saved selection |
| Restaurant | `GET /api/restaurant/v1/internal/organizations/{org}/branches/{branch}/promptpay-owner` | Exact existing Media workload policy; validates active THB owner, no caller-selected subject |
| Media | `GET /api/media/v1/customer/organizations/{org}/branches/{branch}/promptpay-qr` | Menu-readable bundle with selection, issuedAtUtc, expiresAtUtc and optional PNG image/hash/assetId |
| Media | `GET .../promptpay-qr/management-preview` | Same delivery with configuration.manage |
| Media | `GET .../promptpay-assets/{asset}` | Configuration.manage and exact branch-owned ready PNG check |
| Media | `POST .../promptpay-uploads` | Configuration.manage; fileName, contentType=image/png, sizeBytes, checksumSha256; returns org/branch/assetId/version/uploadUrl/expiresAtUtc |
| Media | `POST .../promptpay-uploads/{asset}/complete` | Configuration.manage; expectedVersion; returns scoped assetId/version/status after safety pipeline |

The Media owner is fixed server-side to `restaurant/promptpay_qr/{branch}`. Generic Media
upload/completion cannot create or complete this purpose. Signed PUT is bounded to its
original object/type/checksum and ten-minute expiry; standard quota/signature/ClamAV applies.
Approval additionally requires a safe, decodable static PNG, 64–2048 pixels per side and
2 MiB maximum. PNG delivery preserves dimensions losslessly; no gallery/photo display variant
is used. Ready status alone does not approve a branch image or validate bank QR semantics.

BFF root `/bff/customer/promptpay-qr` offers GET `/csrf`, GET/PUT `/branches/{branch}`,
GET `/branches/{branch}/image`, POST `/branches/{branch}/uploads` and POST
`/branches/{branch}/uploads/{asset}/complete`. Protected selected tenant and stable subject
come from the session, with live NexaConnect membership. All writes require `X-Nexa-CSRF`.
JSON write bodies cap at 4096 bytes; metadata upstream responses cap at 16 KiB and image
responses at 3,000,000 bytes. Browser PUT goes directly to the allowed signed storage origin without
staff bearer/cookies. Guest edge does not publish these routes.

Failures map to 400 invalid input/explicit approval, 401 no session, 403 live authority denied,
404 absent/inactive scope or asset, 409 stale branch/upload state and 503 dependency/safety
unavailable. BFF suppresses downstream bodies. Do not automatically retry an uncertain write.
Restaurant selection uses branch CAS and a transactional audit/outbox. Delivery rereads live
selection/authority after object I/O and leases at most 60 seconds. Original uncertain tender
verification reuses saved payment intent; this API never authorizes or settles funds.

Cross-service validation is not atomic with subsequent asset deletion or permission changes.
An already issued image may remain visible until lease expiry; new confirmation refreshes
the current approved selection. No QR semantic/payee validation or immediate revocation is claimed.

See [operator setup and observability](../Deployment/POS-Branch-PromptPay-QR.md).
