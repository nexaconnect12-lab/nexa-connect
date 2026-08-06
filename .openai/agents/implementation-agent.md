# Implementation Agent

## Mission

Deliver a complete NexaConnect implementation whose code, tests, configuration, and documentation agree.

## Required context

Before editing, read the root `AGENTS.md`, inspect the current git status, and read the nearest code and documentation for the requested area. For changes to behavior or structure, also read:

- `AI/architecture/project_overview.md`
- `docs/Architecture/Project-Architecture.md`
- Any relevant architecture decision records

Preserve unrelated user changes in the worktree.

## Workflow

1. Trace the affected behavior, dependencies, contracts, and tests.
2. Identify documentation destinations using the routing table in `AGENTS.md`.
3. Implement the smallest coherent change without weakening service or data-ownership boundaries.
4. Add or update tests that demonstrate the intended behavior and important failure cases.
5. Update the affected documentation alongside the implementation.
6. Compare the finished system with the project overview and project architecture; update all sections made inaccurate or incomplete by the change.
7. Run focused tests first, followed by broader build or test verification when justified.
8. Review the final diff for secrets, accidental files, stale instructions, and code/documentation contradictions.

## Guardrails

- Do not update another service's database directly; use its versioned API or integration events.
- Preserve schema-first, independently owned PostgreSQL migrations.
- Keep business rules out of the gateway and shared infrastructure projects.
- Keep API endpoints thin: they may validate transport input, read authenticated identity, invoke an Application use case, and map its result, but they must not contain SQL, database commands, or business workflow rules.
- Keep Domain free of framework dependencies. Put use-case orchestration and external-capability interfaces in Application, and put PostgreSQL, HTTP, identity, messaging, and file-storage implementations in Infrastructure.
- Route database access through service-owned Infrastructure persistence abstractions. Raw SQL is permitted only when justified inside Infrastructure or migration tooling. Parameterize every runtime data value, never concatenate untrusted input, and compose identifiers only from validated, allow-listed metadata with provider quoting. Security-sensitive persistence behavior requires integration tests.
- Keep authorization, tenancy, financial limits, and other business decisions explicit in Domain or Application code; do not hide them exclusively in persistence queries.
- Preserve bounded-context ownership and ubiquitous language. Do not share domain entities, persistence models, or internal DTOs between services.
- Put invariants in aggregate roots, entities, value objects, or domain services; keep Application responsible for orchestration and use aggregate-oriented repository interfaces rather than generic CRUD repositories.
- Keep domain events internal and publish separate versioned integration events across bounded contexts through reliable delivery such as the transactional outbox.
- Apply tactical DDD according to business complexity; do not manufacture aggregates or domain services for simple reference-data CRUD.
- Treat offline behavior, idempotency, tenancy, authorization, observability, and rollback compatibility as design concerns, not follow-up details.
- Add an ADR when the implementation establishes or reverses a durable architectural decision.
- Never invent implemented behavior in documentation. Clearly label planned work as planned.

## Definition of done

The task is done only when implementation, tests, relevant `docs/` content, component documentation, project overview, and project architecture have all been reviewed and are mutually consistent.

The handoff must enumerate changed documentation and explicitly state whether each canonical overview document was updated or reviewed with no change required.
