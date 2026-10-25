# QR7 local guest edge transport boundary

Local test tool `NexaConnect.QrPilotEdge` accepts only Development/Testing, requires exact
HTTPS origin/host and binds one configured private IPv4/unprivileged port. Production
deployment remains the dedicated-server topology. No schema, domain model, identity role
or public financial command changes.

Allowed upstream paths/methods are closed:

| Method | Path |
| --- | --- |
| GET | `/order`, `/order/`, flat known-format `/assets/<name>.(js,css,woff2,png,svg)` |
| GET | `/bff/guest/qr/context`, `/menu`, `/requests` under that guest prefix |
| POST | `/bff/guest/qr/sessions`, `/quote`, `/requests` under that guest prefix |
| POST | `/bff/guest/qr/requests/{guid}/confirm-price` |
| DELETE | `/bff/guest/qr/sessions` |

Queries, percent-encoded/arbitrary paths, staff/Kitchen/identity/admin/private service
routes and other methods fail closed. Mutation Origin must exactly match the configured
origin; POST requires JSON. Existing BFF guest scope/CSRF/origin/capability/business checks
remain authoritative. GET bodies are rejected. Limits are 8192 input bytes, 16 KiB headers,
4 MiB responses, 100 concurrent connections and a 20-second upstream timeout, with bounded
transport admission. GET body 400; wrong route/host 404; origin 403; type 415; size 413;
abuse 429; unavailable/redirect/
upstream diagnostic failure 503. No error body or Location is forwarded on those failures.

Only the QR guest cookie and exact Origin supply guest authority across the edge. The
outbound HTTP Host is constructed from the fixed configured PublicOrigin so the BFF's
exact-host guard remains active; no arbitrary incoming Host is forwarded. Staff
Authorization, tenant, forwarded headers and cookies never cross. Responses expose only the host-only secure,
HttpOnly, SameSiteStrict QR guest cookie; staff cookies/Location are suppressed. Responses
are no-store with no-referrer/nosniff. The pinned upstream is an exact HTTPS localhost BFF:
no redirects, certificate SHA256 pin, explicit SAN match against BackendOrigin's
`localhost`, and lifetime checks. This TLS identity check is independent of the fixed
public HTTP Host and does not install trust or disable validation globally.
No public hostname-derived arbitrary destination or staff token forwarding exists.

Shared JSON/OTLP/correlation logging uses service `nexaconnect-qr-pilot-edge`; untrusted
raw path/query/host values are normalized before edge telemetry. Correlation IDs use the
shared validator and outbound propagation. Bounded failure categories contain no raw
headers/bodies, guest tokens or financial values. See [operations/queries](../Deployment/QR7-Pilot.md).
