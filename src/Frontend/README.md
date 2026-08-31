# NexaConnect frontend foundations

This npm workspace contains the versioned, browser-safe foundations shared by NexaConnect portals. It requires Node.js 20.19 or later.

Customer Portal Payment Review browser contract tests run with `npm run test:e2e:payment-review`; see [setup and evidence boundaries](e2e/payment-review/README.md). They use synthetic BFF responses, not live OIDC or provider credentials.

The Phase 7 Product Owner Portal is implemented in `apps/product-owner-portal`. The Phase 8 tenant-scoped Customer Portal is implemented in `apps/customer-portal`; it consumes the same presentation foundations but retains its own Customer BFF session, protected organization/product selection, configuration, and deployment boundary. See each app README for local and deployment configuration.

## Packages

| Package | Responsibility |
| --- | --- |
| `@nexaconnect/design-system` | Ant Design theme tokens, provider, and approved primitive exports. |
| `@nexaconnect/layout` | Responsive portal shell and capability-aware navigation presentation. |
| `@nexaconnect/api-client` | Typed BFF request/result contracts, RFC 7807 errors, cookie credentials, and correlation propagation. |
| `@nexaconnect/form-validation` | Zod-based validation and field-error mapping. |
| `@nexaconnect/localization` | Portal-provided message catalogs, fallback lookup, interpolation, and locale formatters. |
| `@nexaconnect/error-handling` | Safe API/network error normalization and a React error boundary. |
| `@nexaconnect/authorization-ui` | Presentation-only capability checks and conditional rendering. |
| `@nexaconnect/telemetry` | Portal-named UI events, correlation IDs, and sensitive-attribute filtering. |

Run `npm install`, `npm run check`, and `npm test` from this directory. Package output is generated into each package's ignored `dist` directory. Consumers import only public package exports.

The opt-in [Phase 8 joined browser acceptance](e2e/phase8/README.md) uses Playwright against the real local Customer Portal stack. It covers OIDC sign-in, tenant selection and cross-tenant denial, direct Media upload, safety completion, generated-variant downloads, and deletion. Missing credentials and seed identifiers skip the suite; normal frontend checks never contact external services.

## Trust boundaries

The authorization UI helpers receive an evaluator from the consuming portal. They may hide navigation or actions for usability, but they do not define roles, resolve organizations, validate sessions, or authorize requests. Each Customer, Product Administration, and Product Owner portal must build its evaluator from its own BFF contracts and keep its deployment, OIDC client, cookie, audience, and policy model independent. Every BFF and owning service must authorize every operation even when the UI already hid or disabled it.

The API client uses same-origin BFF cookies. It does not accept or store OAuth tokens. State-changing requests still require the anti-forgery contract selected by the owning BFF; callers can pass the resulting safe request header through `RequestOptions`.

Telemetry attributes are allow-by-construction primitives and keys that suggest tokens, cookies, secrets, passwords, authorization data, bodies, personal contacts, or card data are dropped. Portals should record stable route templates rather than raw URLs and must configure a distinct service name, such as `nexaconnect-customer-portal` or `nexaconnect-admin-portal`.
