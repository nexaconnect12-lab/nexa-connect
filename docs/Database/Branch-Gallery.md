# Branch gallery persistence

Restaurant retains ownership of `branches.business_configuration.ScreensaverGallery`.
The value stores `UpdateBranchGallery`: `ExpectedVersion`, ordered `Staff` and `Customer`
asset/title/caption lists and `SecondsPerSlide`. Reads expose the current branch row version,
not the historical expected version inside JSON. The key is additive and older pricing
updates preserve it via JSON merge; gallery writes preserve other branch properties.

No migration/table/index is added. Restaurant migration 3 permits the existing configuration
audit vocabulary. One organization/active-restaurant/active-branch/version-predicated update,
append-only branch audit and `restaurant.audit.v1` outbox insertion share a transaction.
Concurrent/stale writes do not silently overwrite and outbox failure rolls all changes back.
Runtime values are parameterized. Existing branch identity indexes cover these reads/writes.

Before a gallery permission decision, an organization-bound lookup resolves the branch's
active owning restaurant. Both branch and restaurant must be active. Application passes
the resolved restaurant and requested branch to Authorization; the lookup does not grant
access. No schema or migration is added.

Media assets, processing rows, objects and deletion remain Media-owned; the selection stores
only asset IDs/public text. Ready validation is an API call, never another service's database.
It is not atomic with deletion; delivery repeats readiness and cache use has a short lease.

`BranchGalleryPersistenceTests` applies real Restaurant migrations in a generated quoted
schema of an explicitly owned disposable loopback database. It verifies tenant boundaries,
concurrency, preservation with pricing updates and transaction rollback. It also checks
the hierarchy lookup against wrong organizations, missing branches and inactive branches
or parent restaurants. See
[commands/evidence](../Deployment/Next-Iteration-Phase-5.md) and
[ADR-040](../Architecture/Decisions/ADR-040-branch-approved-screensaver-gallery.md).
