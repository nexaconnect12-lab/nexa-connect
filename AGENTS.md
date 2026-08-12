# NexaConnect Agent Instructions

These instructions apply to the entire repository.

## Project context

Read the following documents before making a change that affects application behavior, public contracts, data ownership, deployment, security, or architecture:

- `README.md`
- `AI/architecture/project_overview.md`
- `docs/Architecture/Project-Architecture.md`
- The documentation in `docs/` and the component `README.md` files related to the change

Additional agent role guides live in `.openai/agents/`:

- `.openai/agents/implementation-agent.md`
- `.openai/agents/documentation-agent.md`

Callable Codex custom-agent definitions live in `.codex/agents/`. When subagents are available, use `documentation_maintainer` as a final audit after implementation work that changes behavior, configuration, contracts, data, deployment, or architecture. The primary agent must wait for the audit, resolve its findings, and remains accountable for the finished documentation.

## Mandatory implementation workflow

An implementation task is not complete when the code works. It is complete only after the implementation, tests, and documentation describe the same system.

For every implementation:

1. Inspect the current implementation and its existing documentation before editing.
2. Make the smallest coherent code and configuration change.
3. Add or update tests appropriate to the risk of the change.
4. Update the relevant documentation in the same change set.
5. Review `AI/architecture/project_overview.md` and `docs/Architecture/Project-Architecture.md` against the finished implementation and update every affected section.
6. Run the `documentation_maintainer` audit when subagents are available and resolve any code/documentation drift it finds.
7. Run the relevant verification commands and inspect the final diff for code/documentation drift.

Every new or materially changed HTTP service, BFF route, or cross-service adapter must adopt the shared observability foundation in the same implementation. Preserve validated correlation identifiers across outbound calls and add safe structured events for failure and authorization boundaries. Background workers must provide equivalent structured JSON and OTLP telemetry; a reusable worker-host extension remains planned. Never log tokens, cookies, request/response bodies, payment data, unrestricted personal data, secrets, or arbitrary headers. Document the service name and debugging queries with the component change.

Do not claim an implementation is complete while required documentation remains stale. Do not make cosmetic documentation edits merely to create a diff; if an overview or architecture file is genuinely unaffected, leave it unchanged and state that it was reviewed in the handoff.

## Service layering and persistence rules

- Organize business services into API, Application, Domain, and Infrastructure responsibilities. These may begin as folders in one project and become separate projects only when compile-time boundaries add value.
- Keep controllers and other API endpoints limited to transport concerns: authentication context, request validation, application-use-case invocation, and HTTP response mapping. They must not contain SQL, database commands, or business workflow rules.
- Keep Domain code independent of ASP.NET Core, database providers, HTTP clients, message brokers, and other frameworks. Keep Application code dependent on Domain and on interfaces for external capabilities, not on their concrete implementations.
- Put database access, external HTTP clients, identity-provider integration, messaging, file storage, and other framework-specific implementations in Infrastructure. Expose them to Application through narrow, service-owned interfaces.
- Do not place inline, ad hoc, or hardcoded SQL in API, Application, or Domain code. Database operations must pass through an Infrastructure persistence abstraction.
- Prefer the service's approved persistence mechanism. When raw SQL is genuinely required in Infrastructure or migration tooling, parameterize every runtime data value and never concatenate untrusted input into SQL. Compose identifiers only from validated, allow-listed metadata and quote them with the database provider. Use least-privilege credentials, define transaction boundaries, and cover security-sensitive queries with integration tests.
- Enforce authorization, tenant boundaries, financial limits, and other business decisions in Domain or Application behavior. Database constraints and queries may support those rules but must not be their only undocumented implementation.
- Do not place service-specific business rules in shared projects. Shared code is limited to stable contracts, low-level primitives, and carefully selected cross-cutting infrastructure.
- Existing code may predate these boundaries. Any implementation that materially changes such code must move the touched behavior toward this structure rather than adding more controller-level persistence or workflow logic.

## Domain-driven design rules

- Treat each documented business capability as a bounded context with its own ubiquitous language, domain model, persistence ownership, and versioned integration contracts. Do not share domain entities or database models across bounded contexts.
- Model business invariants inside aggregates, entities, value objects, and domain services. Aggregate roots define consistency and transaction boundaries; callers must not mutate an aggregate's internal entities directly.
- Keep Application use cases responsible for orchestration, authorization context, transaction coordination, and interaction with repositories or external ports. Do not put domain decisions in controllers, message handlers, repositories, or SQL queries.
- Define repositories around aggregate needs rather than exposing database tables, provider types, or a generic CRUD repository. Repository interfaces belong to Domain or Application; implementations belong to Infrastructure.
- Keep domain events internal to their bounded context. Publish separate, explicitly versioned integration events across service boundaries, normally through the transactional outbox.
- Translate external service contracts through an anti-corruption layer when their concepts do not match the local domain model. Never make another bounded context's DTOs the local domain model.
- Apply tactical DDD in proportion to business complexity. Core workflows such as ordering, kitchen execution, payments, shifts, authorization, and synchronization require explicit domain models; simple reference-data CRUD may remain straightforward Application and Infrastructure code without artificial aggregates.
- Use architecture and domain tests to protect aggregate invariants, dependency direction, bounded-context ownership, and the separation between domain and integration events.
- Follow [ADR-005](docs/Architecture/Decisions/ADR-005-domain-driven-design.md) for the accepted DDD approach.

## Documentation routing

Update every destination affected by the implementation:

| Change area | Required documentation review |
| --- | --- |
| API routes, requests, responses, errors, versioning, or gateway behavior | `docs/API/` and the affected service or gateway `README.md` |
| Service boundaries, dependencies, communication, data flow, clients, or topology | `docs/Architecture/`, `AI/architecture/project_overview.md`, and affected component `README.md` files |
| A significant architectural decision or trade-off | Add or update an ADR in `docs/Architecture/Decisions/` and update both architecture summaries |
| Tables, indexes, migrations, ownership, seed data, or persistence rules | `docs/Database/` and the affected tool or service `README.md` |
| Authentication, authorization, claims, clients, roles, or secrets | `docs/Identity/` and any affected deployment documentation |
| Containers, environment variables, infrastructure, startup, release, or operations | `docs/Deployment/`, relevant `docker/*/README.md`, `.env.example` files, and root `README.md` when setup changes |
| A component's responsibilities, configuration, usage, or status | That component's nearest `README.md` |
| Project scope, capabilities, major implementation status, or system inventory | `AI/architecture/project_overview.md` and root `README.md` when externally relevant |

When a change crosses areas, update all applicable destinations. Documentation must describe current behavior, prerequisites, configuration, failure modes, and verification steps where those details matter.

## Canonical documents

- `AI/architecture/project_overview.md` is the concise project overview used for AI and contributor context.
- `docs/Architecture/Project-Architecture.md` is the canonical system architecture.
- `docs/Architecture/Restaurant-POS-Architecture.md` is the canonical restaurant domain and branch-offline architecture.
- ADRs in `docs/Architecture/Decisions/` record durable architectural decisions.
- Component `README.md` files document local setup and component-specific behavior.

Keep summaries aligned with the canonical detailed document. Do not duplicate large sections when a short summary and link are sufficient.

## Completion handoff

Every implementation handoff must list:

- What was implemented.
- Tests and verification performed.
- Documentation files updated.
- Whether the project overview and project architecture changed; if not, confirm they were reviewed and why no update was needed.
- Any known limitations or follow-up work.
