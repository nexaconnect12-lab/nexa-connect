# First-draft slice 2 evidence

Base revision `daa9a0f`; verification ran against the uncommitted slice 2 working tree
on Windows, 14 October 2026. This is local evidence, not clean-checkout CI or production acceptance.

Implemented [reference/access setup](../../Deployment/First-Draft-Provisioning.md)
and [operational discovery](../../API/Operational-Branches.md): owning runtime logins,
THB / Asia/Singapore branch/store/terminal, three products with kitchen/bar stations,
four scoped identities, 1000 THB late-cash manager limit, reference journal/resume,
completed no-op re-provisioning and state-clearing selectors. No schema migration,
canonical station-management model, manual-tender refund or financial-transition seed.

Recorded verification:

| Check | Local result |
| --- | --- |
| .NET Unit, Windows DPAPI opt-in | 810 passed, 0 skipped |
| Architecture | 6 passed, 0 skipped |
| Existing draft lifecycle guards | 29 passed |
| Kitchen browser regressions | 6 passed |
| End-of-day / prepare / cutoff browser regressions | 11 / 8 / 8 passed |
| Seal / approval / finalization / late-cash browser regressions | 22 passed |
| Settlement / payment review / cash-close browser regressions | 7 / 5 / 4 passed |
| Financial-completeness browser regressions | 9 passed |
| Frontend unit tests / production build | 76 passed / passed |
| Real PKCE/OIDC and live reference/access verification | passed; owned cleanup verified |

The first live evidence is
`.runstate/first-draft/382c8c1faa754a7eaa30ae1aef3819e6/provisioning-verification.json`.
It verifies four real identities, BFF tenant selection, exact role decisions,
cross-restaurant/branch and tenant denial, amount boundary, menu/stock coherence,
Kitchen branch/station selection, clearing old queue state, preserving menu/stock edits,
stable journal IDs and retained authorization revocation on repeated setup. Its preceding
failed initial setup exposed and fixed CLI build/output and PostgreSQL command boundaries;
the retained journal resumed without duplicating stock or reference IDs. Browser diagnostics
contain no screenshots, traces, stored tokens or passwords. Evidence keeps ordinary clocks;
no joined sales, shifts or receipts are seeded/proven by these checks.

The final fresh run is
`.runstate/first-draft/5e71775106ed429b9e1492c71553424a/provisioning-verification.json`:
all live checks passed and owned cleanup is verified. Final Unit and Kitchen regressions
were rerun after the last owner-local and station-clearing edits. Total affected browser
regressions: 80 passed. Frontend production build retains its existing large-bundle advisory.

The first required `documentation_maintainer` audit attempt failed because the account
usage limit was reached. A subsequent retry and final follow-up audit passed during
slice 3 completion. The primary resolved API role lists, branch-selector instructions,
stack/provisioning status, ownership/replay/failure boundaries and canonical summary drift.
The successful audit also reviewed the later retained-port and host-environment fixes.
`git diff --check` passed.

Documentation updated: root README; both `AI/architecture/project_overview.md` and
`docs/Architecture/Project-Architecture.md`; this evidence; November first-draft plan;
API Operational-Branches, Authorization-Assignments, Business-Service-API-Slices,
Kitchen-Queue, Payment-Review-Operator-UI, Financial-Reporting-Completeness and
Cash-Close-Reporting; Database-Guidelines; Identity Client-Matrix; deployment First-Draft-Test-Stack
and First-Draft-Provisioning; docker/first-draft README; POS client, Frontend, Customer BFF,
Restaurant, Authorization, Catalog, Inventory, Kitchen and POS service READMEs; new
FirstDraftProvisioning tool README and first-draft browser verification README.

At the slice 2 boundary, slices 3–7 remained. Joined cashier/Kitchen failure recovery is
subsequently verified in [slice 3 evidence](First-Draft-Slice-3.md); slices 4–7 remain for
Windows POS/PDF printing, further financial transitions, release hardening and solo UAT.
No environment remains running from verification.
