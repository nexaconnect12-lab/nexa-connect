# First-draft reference setup (slice 2)

Use the [owned Windows stack](First-Draft-Test-Stack.md). Start first, then:

```powershell
pwsh -File scripts/provision-first-draft.ps1 -RunId <generated-run-id> -ConfirmTestResources
pwsh -File scripts/get-first-draft-login.ps1 -RunId <generated-run-id> -Role cashier
```

Retrieve each account locally: `cashier`, `kitchen-operator`, `store-manager`,
`accountant`. Usernames are `draft-<role>`. Passwords remain current-user DPAPI
protected; the explicit retrieval command displays one password, so do not run it
in CI, shared logs, recordings or transcripts. Use separate browser sessions for
the sole tester's Kitchen, manager and accountant accounts. Cashier authenticates
through the existing POS PKCE client. The portal URL is printed without credentials.

Setup creates the fictional Singapore branch with **THB / Asia/Singapore**, one
Windows store/active terminal, rice bowl (80), noodles (95), iced tea (30), existing
`kitchen`/`bar` station codes and 100 units per product. Zero tax/service charge is
the existing default; these are test prices and do not imply Singapore fiscal compliance.
The POS configuration in the owned run directory is updated with the stable graph;
both `pos/appsettings.json` and retained `pos-configuration.json` survive rebuilds.
Existing local POS recovery is untouched and WPF is not automatically launched.

Cashier, Kitchen and accountant assignments are branch-scoped. Kitchen has only
Catalog menu read and Kitchen read/transition. Accountant has the existing read
permissions and no manager mutations. The existing `store-manager` role covers
its restaurant, with its existing product management permissions; it receives no
platform/tenant admin role or terminal-enrollment permission. Its one financial
approval limit is 1000 THB for `pos.day-close.late-cash.post`; permission plus a live
limit check is still required. No manual-tender refund implementation is added.
A second restaurant/branch and separate unassigned organization are denial fixtures,
not additional operating test branches.

The lifecycle lock excludes concurrent provisioning/start/reset. Generated local
URLs, Docker context/container ownership and running process identities are checked.
`NexaConnect.FirstDraftProvisioning` accepts only the generated run path, Development,
loopback databases named `nexa_review_it_<runId>_<owner>` and the owning runtime login.
Platform Directory, Restaurant, Authorization, Catalog and Inventory use their existing
Application/Infrastructure operations. Reference-only POS store/terminal and Authorization
limit tooling stays in each owner's Infrastructure, uses parameterized transactions and
rejects non-draft databases. No cross-service SQL, migration-history mutation or synthetic
shift, cash session, order, payment, receipt or settlement is seeded.

An atomic reference journal retains IDs after each stage. Initial interrupted setup
can resume; already present products/stock are preserved. Until initial completion,
membership and assignment steps may reactivate intended test access. Once completed,
re-provisioning is a no-op: it does not replenish consumed stock, overwrite prices,
reactivate revoked assignments/limits or reset passwords. Explicitly reset/create a new
run to replace the fixture; do not delete the journal to force a rerun. Independent
service commits are not one distributed transaction. Bounded JSON failure metadata
uses service `nexaconnect-draft-provisioner`, stage/category/SQLSTATE only; inspect the
protected journal and owning service logs. Reference-tool calls are administrative
offline tooling and do not pretend to prove customer authorization.

Portal operational branch discovery checks the intended workflow's live permissions;
see [API](../API/Operational-Branches.md). Station choices use existing menu codes,
with all-stations available if station discovery fails. Menu editing is still an
authorized Catalog API operation, not a complete portal menu-management screen.

Disposable verification (mutates fictional menu/stock, revokes Kitchen access and resets):

```powershell
pwsh -File scripts/test-first-draft-provisioning.ps1 -ConfirmTestResources
```

Requires .NET 10, Node, `npm ci` in `src/Frontend`, installed Playwright Chromium and
local Docker Desktop. It exercises real PKCE and BFF OIDC, all four roles, exact branch
and tenant denial, amount limits, discovered selectors and repeated provisioning after
menu/stock changes and revocation. Evidence is `provisioning-verification.json` in the
protected run directory. Browser traces/screenshots and token persistence are disabled.
This is reference/access acceptance; joined checkout, interruption recovery, printing
and next-day financial acceptance remain slices 3–5.
