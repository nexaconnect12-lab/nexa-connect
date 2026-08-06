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

Do not claim an implementation is complete while required documentation remains stale. Do not make cosmetic documentation edits merely to create a diff; if an overview or architecture file is genuinely unaffected, leave it unchanged and state that it was reviewed in the handoff.

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
