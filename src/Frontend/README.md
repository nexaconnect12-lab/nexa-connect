# NexaConnect frontend foundations

The Customer Portal adds separate sealed day-close controls. Load Ready cutoff evidence before new sealing; the browser sends its reviewed version, preserves uncertain command identity, and shows pending changes at the last check without storing financial data locally. Run preparation/cutoff/seal Vitest tests plus `npm run test:e2e:day-seal` (six synthetic scenarios) and the existing cutoff suite. See [contract](../../docs/API/Day-Close-Seals.md).

Day-cutoff protocol 2 shows retained source revisions and requires complete selected-event delivery plus current revision evidence before Ready. Legacy blocked history remains readable; every changing branch conservatively needs new day evidence. Run `npm run check`, focused preparation/cutoff Vitest tests and `npm run test:e2e:day-cutoff`; see [rollout](../../docs/Deployment/Day-Close-Cutoffs.md).

The [joined cashier-to-cutoff gate](../../docs/Deployment/Cashier-Day-Cutoff-Acceptance.md) exercises the actual source-manifest/API/BFF boundary and retained-event comparison with real OIDC, cashier commands, actual broker publication, run-owned process interruption and late financial evidence. The expanded gate requires thirteen browser and six Authorization persistence cases plus verified cleanup; prior twelve-case cutoff runs remain historical. Current sealing execution is recorded in the seal evidence linked above. Shared service observability remains enabled; proof/artifacts exclude credentials and financial bodies. This is retained-observation acceptance, not settlement finalization. See [execution status](../../docs/Architecture/Evidence/Cashier-Day-Cutoff-Acceptance.md).

Customer Portal adds [retained day-cutoff evidence](../../docs/API/Day-Close-Cutoffs.md) controls and strict generation/currentness/gap validation. Run the frontend checks/tests/build and deploy the coordinated source/POS migrations before exposing controls. Preparation remains separate; joined online cash acceptance passed locally with twelve browser and six Authorization persistence cases and verified cleanup on 2026-10-06. Remote CI and target-production acceptance remain open.

`npm run test:e2e:day-cutoff` runs eight [synthetic browser contracts](e2e/day-cutoff/README.md) against the actual Customer Portal and intercepted BFF responses. They cover capture/refresh, read-only accountant controls, exact-operation retry/resume/replacement, stale responses, tenant clearing and malformed readiness. All eight passed locally on 2026-10-06 without skips/retries; screenshots, video and traces are disabled. This pass does not establish joined real-OIDC/source-service acceptance.

The [joined cashier-to-day-close gate](../../docs/Deployment/Cashier-Day-Close-Acceptance.md) implements real cashier PKCE, authorized shift/cash/checkout/manual-settlement/receipt/review commands, actual Order→POS/Reporting publication and Customer Portal preparation with manager contention and POS restart. Its reference-only fixture seeds no financial transitions. An acceptance-only host injects historical command time into actual Order/POS applications; production uses the system clock with no configurable clock switch. Five browser scenarios, six Authorization persistence cases and verified cleanup are required; execution evidence remains separate from production settlement/cutoff certification.

Frontend dependency hardening pins Vitest 4.1.11 and SourceMap.js 1.2.2, removes Tinypool, and requires Node 22.12+ (22.x) or 24+. CI separately audits production and full dependency trees, rejecting moderate-or-higher findings; the frontend compatibility job checks Node 22 and 24. See [runtime, audit and verification policy](../../docs/Deployment/Frontend-Dependency-Hardening.md).

The [joined day-close suite](e2e/day-close-live/README.md) implements eleven real-OIDC scenarios through the guarded disposable launcher. Run `npm run test:day-close:guards` for its four configuration/evidence checks; `test:e2e:day-close:live` requires the complete launcher environment and runner-owned POS restart control. Local Windows acceptance passed eleven browser and six Authorization persistence cases without skips/retries, with verified cleanup on 2026-10-06; remote CI and production acceptance remain separate.

Day-close preparation browser contracts run with `npm run test:e2e:day-close`. The actual Customer Portal uses synthetic BFF replies to verify CSRF/version/operation payloads, readiness invalidation, accountant controls, uncertain replay, restart-resume and stale filter/tenant/malformed response handling. See [scope and verification](../../docs/Deployment/Day-Close-Preparation.md); live joined preparation acceptance remains separate.

The [joined financial suite](e2e/financial-completeness-live/README.md) adds real OIDC/BFF/Reporting validation through the disposable launcher. Run `npm run test:financial-completeness:guards` for fail-closed settings/evidence checks; live execution requires that launcher environment.

Financial completeness browser contracts run with `npm run test:e2e:financial-completeness`. The Customer Sales report displays recorded reconciliation status, provenance and separate financial inventories using the same explicit closed UTC filters as fresh totals. It clears stale evidence on filters/tenant/reload/denial and sends no financial mutation. See [test setup and evidence boundaries](e2e/financial-completeness/README.md).

Single-store cash-close Reporting browser contracts run with `npm run test:e2e:cash-close`. The read-only screen uses synthetic fixtures for these tests; see [test setup and evidence boundaries](e2e/cash-close/README.md).

Payment Review now has an opt-in real-OIDC suite: `npm run test:e2e:payment-review:live`, with `npm run test:payment-review:guards` for fail-closed configuration/evidence checks. See [live prerequisites and evidence limits](e2e/payment-review-live/README.md). It is not provisioned by the isolated infrastructure matrix and has not been live-executed here.

This npm workspace contains the versioned, browser-safe foundations shared by NexaConnect portals. It requires Node.js 22.12+ (22.x) or 24+.

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

Run `npm ci --ignore-scripts`, both audit scripts, `npm run check`, and `npm test` from this directory. Package output is generated into each package's ignored `dist` directory. Consumers import only public package exports.

The opt-in [Phase 8 joined browser acceptance](e2e/phase8/README.md) uses Playwright against the real local Customer Portal stack. It covers OIDC sign-in, tenant selection and cross-tenant denial, direct Media upload, safety completion, generated-variant downloads, and deletion. Missing credentials and seed identifiers skip the suite; normal frontend checks never contact external services.

## Trust boundaries

The authorization UI helpers receive an evaluator from the consuming portal. They may hide navigation or actions for usability, but they do not define roles, resolve organizations, validate sessions, or authorize requests. Each Customer, Product Administration, and Product Owner portal must build its evaluator from its own BFF contracts and keep its deployment, OIDC client, cookie, audience, and policy model independent. Every BFF and owning service must authorize every operation even when the UI already hid or disabled it.

The API client uses same-origin BFF cookies. It does not accept or store OAuth tokens. State-changing requests still require the anti-forgery contract selected by the owning BFF; callers can pass the resulting safe request header through `RequestOptions`.

Telemetry attributes are allow-by-construction primitives and keys that suggest tokens, cookies, secrets, passwords, authorization data, bodies, personal contacts, or card data are dropped. Portals should record stable route templates rather than raw URLs and must configure a distinct service name, such as `nexaconnect-customer-portal` or `nexaconnect-admin-portal`.

Kitchen queue browser contracts run with `npm run test:e2e:kitchen`; see [fixtures and evidence boundaries](e2e/kitchen/README.md). The tenant-scoped screen uses the Customer BFF and existing Kitchen permissions; it is an online ticket-level surface.

For the separate five-case real-OIDC/service/broker suite, use the [joined cash-close portal launcher](../../docs/Deployment/Cash-Close-Portal-Acceptance.md). It provisions disposable infrastructure and requires all scenarios and cleanup; the synthetic browser command above does not run it.

Local Windows cashier-to-day-close acceptance passed all five real-command browser scenarios and six Authorization persistence cases without skips/retries, with verified cleanup on 2026-10-06. See [exact working-tree execution evidence](../../docs/Architecture/Evidence/Cashier-Day-Close-Acceptance.md). Remote CI and production acceptance remain separate.
