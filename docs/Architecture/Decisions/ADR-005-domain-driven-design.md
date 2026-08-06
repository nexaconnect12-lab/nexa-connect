# ADR-005: Domain-Driven Design for Business Capabilities

- **Status:** Accepted
- **Date:** 2026-08-06
- **Scope:** NexaConnect business services, domain models, and cross-context integration

## Context

NexaConnect contains business capabilities with different language, rules, consistency needs, and release lifecycles. Ordering, kitchen execution, payments, POS operations, inventory, restaurant management, authorization, and reporting must evolve without sharing mutable models or bypassing ownership boundaries. The early scaffold also contains controller-level workflows and persistence code that do not provide a durable home for business invariants.

Applying one shared data model or organizing business behavior around framework and database concerns would couple services, weaken ownership, and make offline and financial rules difficult to test. Applying every tactical DDD pattern indiscriminately would create unnecessary abstraction for simple reference-data operations.

## Decision

NexaConnect adopts Domain-Driven Design for its business capabilities.

Strategic DDD defines the primary boundaries:

- Each business capability is treated as a bounded context with its own ubiquitous language, domain model, persistence ownership, and versioned integration contracts.
- The current restaurant bounded-context map is maintained in [Restaurant POS Architecture](../Restaurant-POS-Architecture.md#4-business-capability-boundaries-and-bounded-contexts).
- Bounded contexts do not share domain entities, persistence models, internal DTOs, or database tables.
- Cross-context communication uses versioned APIs or integration events. An anti-corruption layer translates concepts when an external contract does not match the local model.
- Context boundaries follow business ownership. A one-service-per-context deployment is preferred but is not mandatory when operational simplicity favors a modular deployable with equally explicit internal boundaries.

Tactical DDD is applied where business complexity warrants it:

- Aggregates and aggregate roots enforce invariants and define transactional consistency boundaries.
- Entities have stable identity; value objects represent immutable domain concepts and validate their own invariants.
- Domain services contain domain behavior that does not naturally belong to one entity or value object.
- Application use cases orchestrate aggregates, authorization context, repositories, transactions, and external ports without owning domain decisions.
- Repositories are aggregate-oriented. Their interfaces belong to Domain or Application and their implementations belong to Infrastructure; generic table-level repositories are not the default.
- Domain events remain internal to a bounded context. Separate, explicitly versioned integration events cross context boundaries and use reliable publication such as the transactional outbox.

Simple reference-data CRUD may use straightforward Application and Infrastructure code without artificial aggregates, domain services, or events. The complexity of the domain determines the tactical patterns used.

Existing code is migrated incrementally. New code follows this decision immediately, and material changes to nonconforming code move the touched behavior toward the target structure.

## Consequences

### Positive

- Business rules have explicit, framework-independent ownership and can be tested directly.
- Service and data ownership align with business language and organizational responsibility.
- Aggregate transaction boundaries make consistency choices explicit.
- Domain and integration events are no longer conflated.
- External contracts can evolve without becoming internal domain models.

### Costs and risks

- Teams must maintain a shared understanding of each context's ubiquitous language and context map.
- Mapping between API, domain, persistence, and integration models adds code where boundaries require it.
- Poorly chosen aggregates can create contention or cross-aggregate transaction pressure and require revision.
- Tactical DDD can become ceremony if applied to simple CRUD without business complexity.
- Existing controller-level workflows and persistence code require incremental refactoring.

## Alternatives rejected

### Transaction scripts in controllers

Rejected because HTTP endpoints, SQL, orchestration, and business invariants become coupled and difficult to test independently.

### One shared enterprise domain model

Rejected because the same term can have different meaning and lifecycle in different bounded contexts, and shared models create deployment and ownership coupling.

### Mandatory rich domain models for every operation

Rejected because simple reference data does not benefit from artificial aggregates and domain services. DDD is applied according to business complexity.
