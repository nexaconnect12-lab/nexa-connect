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

Final fresh-run, frontend test/build and documentation-audit evidence are recorded below
once completed. Slices 3–7 remain required for joined cashier/Kitchen failure recovery,
Windows POS/PDF printing, real financial transitions, release hardening and solo UAT.
