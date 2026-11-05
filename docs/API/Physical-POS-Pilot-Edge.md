# Physical POS pilot transport boundary

`NexaConnect.PosPilotEdge` is a private Development/Testing transport tool for the
[separate fresh-data LAN pilot](../Deployment/POS-Physical-Device-Onboarding.md).
It introduces no business endpoint or database owner. The existing POS, Order, Catalog,
Payment and Media contracts and live authorization remain authoritative.

## Admission and forwarding

The listener uses one configured private IPv4 HTTPS origin and admits only its development
host and designated device source addresses. It requires that exact Host and, when present,
that exact Origin. Client-supplied forwarding headers are not trusted. Fixed public-origin
and connection-derived proxy headers replace them. The existing validated correlation is
propagated through the shared HTTP infrastructure.

For business routes, `X-Nexa-Terminal-Id` and `X-Client-Operation-Id`, when supplied,
must each contain one nonempty GUID. The edge forwards the canonical GUID value;
malformed, empty or multi-value input returns 400 before upstream access. Their absence
does not create a new identity: owning endpoints retain any mandatory terminal, operation,
scope and idempotency validation. Forwarding preserves the original native/outbox operation
rather than generating another operation or retrying its command.

Structured JSON and validated correlations use the shared observability foundation.
OTLP export is explicitly rejected before exporter registration, pending separate
verification of outbound identity-URL and hosting-scope redaction. This transport does
not currently support enabling OTLP as a configuration-only change.

| Forwarded surface | Methods and conditions |
| --- | --- |
| `/realms/nexa-review-it-<runId>/.well-known/openid-configuration` | GET/POST in the one configured realm |
| Same realm `/protocol/openid-connect/{auth,token,userinfo,certs,logout}` | GET/POST; existing public-client PKCE and service authentication remain owning identity behavior |
| Same realm `/login-actions/{authenticate,restart,reset-credentials,required-action,action-token}` | GET/POST; login forms/cookies pass only through the identity upstream |
| `/resources/…` | GET of restricted resource paths |
| `/api/{pos,order,catalog,payment,media}/v1/…` | GET/POST/PUT/PATCH/DELETE with a bearer header; tokens are validated by the owning service |
| Exact `/api/media/v1/gallery-readers/organizations/{uuid}/branches/{uuid}` | GET may use the existing bounded `X-Nexa-Gallery-Reader` capability; no other bearer exemption |

Admin/master, internal, guest and metrics surfaces are not published. Unsupported methods,
encoded/traversal paths and requests outside admission receive a disclosure-safe 404.
Missing API bearer returns 401 before upstream access. Cross-origin requests return 403;
admission exhaustion returns 429 with `Retry-After: 60`. Bodies above 65,536 bytes return
413. This boundary is not a general media-upload or guest ordering ingress.

Upstreams are distinct, configured loopback HTTP origins; clients cannot choose them.
Business API forwarding excludes browser cookies. Native `X-Nexa-Terminal-Id` and
`X-Client-Operation-Id` headers are forwarded only as canonical nonempty GUIDs; malformed
or multiple values return 400 before upstream access. Original operation identity is
preserved and never replaced by the proxy. Identity responses may retain login
cookies and only same-origin redirects or the exact native
`nexaconnect-pos://oauth/callback` destination. External redirects and upstream failures
return 503. Original upstream error statuses otherwise remain intact. Transport-generated
errors do not disclose exception details. Responses use `Cache-Control: no-store`,
`X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin` and
`X-Frame-Options: DENY`. Identity responses preserve the upstream
`Content-Security-Policy` rather than replacing the identity provider's policy.
The referrer policy discloses no login path/query and preserves the browser's same-origin
form Origin. Requests explicitly carrying `Origin: null` or another origin remain denied;
they are not accepted to bypass browser-login failures.

Forwarding has a 25-second HTTP-client timeout, bounded request/response buffering and
no financial retry. Do not infer failed settlement/refund from timeout or 503: preserve
the native original-operation recovery and verify the original command through its
owning service. This boundary adds no payment success, refund reset or offline authority.

## Verification status

`PosPilotEdgeTests` uses TestServer and synthetic upstreams for routing/admission,
bearer/tenant/correlation forwarding, proxy-header replacement, cookie separation,
gallery capability, redirect, origin and body limits. The Windows onboarding check
tests normal certificate trust, canonical issuer/PKCE and anonymous API denial without
sign-in. Neither establishes real Keycloak/browser operation, authenticated branch/role
denial, device installation or financial/provider acceptance. Those remain separate
activation and operator gates described in the operating guide.
