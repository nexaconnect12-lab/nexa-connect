# NexaConnect OpenAI Agent Guides

This directory contains the detailed, human-readable role guides for agents working on NexaConnect.

Codex discovers durable repository instructions from the root `AGENTS.md`. Callable project-scoped custom agents use the supported definitions in `.codex/agents/*.toml`; those definitions direct each agent to the fuller guides stored here.

## Available roles

- `agents/implementation-agent.md` owns a change from repository inspection through implementation, tests, documentation, and final verification.
- `agents/documentation-agent.md` audits a completed implementation and synchronizes the relevant project documentation, overview, and architecture.

These roles are complementary. The implementation agent remains responsible for documentation even when the documentation agent is used as a separate review pass.

## Layout

- `AGENTS.md` at the repository root defines the mandatory completion contract.
- `.codex/agents/implementation.toml` registers the callable `implementation` agent.
- `.codex/agents/documentation-maintainer.toml` registers the callable `documentation_maintainer` agent.
- `.openai/agents/*.md` contains the maintainable role playbooks requested for this project.
