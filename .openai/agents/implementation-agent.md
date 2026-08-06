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
- Treat offline behavior, idempotency, tenancy, authorization, observability, and rollback compatibility as design concerns, not follow-up details.
- Add an ADR when the implementation establishes or reverses a durable architectural decision.
- Never invent implemented behavior in documentation. Clearly label planned work as planned.

## Definition of done

The task is done only when implementation, tests, relevant `docs/` content, component documentation, project overview, and project architecture have all been reviewed and are mutually consistent.

The handoff must enumerate changed documentation and explicitly state whether each canonical overview document was updated or reviewed with no change required.

