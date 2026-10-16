# First-draft Slice 8 — release preparation

Prepared on 16 October 2026 for one Windows PC, Microsoft Print to PDF, a THB branch,
Asia/Singapore and one tester using separate cashier/Kitchen/manager/accountant identities.
Use the [UAT script](../../Deployment/First-Draft-UAT.md) and
[Windows runbook](../../Deployment/First-Draft-Windows-POS.md).

## Original frozen candidate

The checkout initially was clean at `309f0545084c8e688a324d400b54ca79034a582b`.
Executable input hash was
`504b8c79c7b84f558fab290888cb216ee05377c162d0b7647e68ea085e270078`.
All ten configured GitHub workflows associated with that PR head passed, including both Node 22/24
frontend jobs. Their archived receipts identify GitHub's clean synthetic merge commit
`8f35a5d365b668549a8edeb2f9a45c94bb9f47d9`, rather than the branch-head SHA. GitHub's
head-to-merge comparison returned zero changed files with no oversized result, establishing
identical source contents while preserving the distinct commit provenance. This is
PR merge-check evidence, not a branch-head dispatch or release signature.
Latest-attempt job steps were checked with no skipped steps. Ten bounded
artifact ZIPs were downloaded and SHA-256 checked against GitHub metadata before their
30 October expiry; no raw service/browser logs or credentials were downloaded.
These workflow results cover the original source contents through the synthetic merge,
not the corrective changes below.

Local full gate `47fb670049cf4acc996c5c0defeddfe3` failed in the cash-close portal's
`late` fixture operation. It remains failed, with cleanup verified for its disposable
graph. Before failure it passed 834 Unit, 6 Architecture, 2 health HTTP, 76 frontend
tests, both builds and zero-vulnerability production/full audits, 123 PostgreSQL cases,
9 settlement Reporting cases, 6 restricted replay cases and alert-rule checks. The
original fixture error suppressed its cause; diagnosis/revalidation is tracked separately.
No full local release or UAT readiness is inferred from the successful individual stages.

## Corrections and preparation

- Fixed packaging's clean-Git null/empty-output failure. Publication now uses the shared
  typed candidate identity and compares revision/hash/dirty state before/after publishing.
  Twenty-four terminal guards cover a real disposable Git checkout, clean/changed inputs,
  typed dirty state and the existing package/scope checks. UAT guards: 44 passed.
- Added allowlisted fixture diagnostics and five cash-close settings/reporter/diagnostic
  tests, including arbitrary-output redaction; all five passed. The release runner now
  includes these checks. No timeout extension, retry, financial fixture bypass or skipped
  required scenario is introduced.
- Focused diagnostic run `86b342199f4148de8caad5d63b98b240` passed all five browser
  scenarios and six Authorization persistence tests with verified cleanup. The original
  failure did not recur, so its precise cause is unconfirmed; this pass does not rewrite
  the failed original gate or remove the need for full corrected-candidate verification.
- All five script suites passed: 24 terminal, 44 UAT, 31 release, 32 lifecycle and 19
  manager checks. `git diff --check` passed. The temporary Git fixture needs normal local
  metadata access; the sandboxed attempt was denied and the normal-access run passed.
- Started/provisioned retained run `d972fcff109d435691b9eb9afcd4ddf7` using normal clocks
  and owner repositories, with menu/stock/roles and no seeded financial transitions.
  Portal: `https://127.0.0.1:49537/`. A clearly marked non-bank image enables fictional
  manual PromptPay confirmation; no real QR destination or transfer exists.
- Published package `3b772d3b695b4ac5a35b16820692e411` with the corrective working-tree
  identity. Its initial installation attempt failed the read-only configuration/recovery
  preflight; no activation or callback registration/launch occurred. Existing current-user
  `pos-state.db`/WAL/SHM files were preserved. The generic preflight result alone does not
  prove which configuration, scope, schema, protection or integrity condition failed.
- .NET Windows Desktop 10.0.11, running Spooler and Microsoft Print to PDF are present.
  This is a prerequisite check, not saved-PDF or interactive device acceptance.

Private handover directory:
`.runstate/first-draft-handover/150594a8b9f24c9199d7ca076ee45221`.
It holds the original PR merge-check CI index, digest-verified bounded ZIPs, synthetic image and
release-specific instructions; never distribute the entire protected run or recovery.
The retained package resides in its run's `terminal-packages` directory and is not a
clean-commit release while these corrections remain uncommitted.

## Corrected pre-commit gate

Full run `6738c9de30b84109a42f798d18092d53` passed against unchanged corrected inputs
`e1b8b806f7d713869621c37ecf69df57cb14f3de0f6a7cd2d0fbdd47eac2ebac`, based on
`309f0545084c8e688a324d400b54ca79034a582b` with `sourceDirty: true`.
It passed 834 Unit, 6 Architecture, 2 health HTTP and 76 frontend cases, both builds,
both zero-vulnerability audits, 123 PostgreSQL, 9 Reporting and 6 replay cases.
All safety guards, both five-case Node guard suites and cash-close alert checks passed.
Joined stages passed with verified cleanup:

| Stage | Result and run |
| --- | --- |
| Cash-close portal | 5 browser + 6 Authorization persistence cases; `4e53fc3e2e5c4156b7e3647f1ea0e421` |
| Normal-clock checkout/Kitchen/manager | All 9 checks true, including isolated restore and resumed manager reads; `488c46c18f1546b597b3ca12f16f9b2b` |
| Isolated restore | 11 databases and 6 configuration files; `704329968cb24a9993349758833b5a6a` within the normal run. New clone application-graph, broker/Redis and POS-state restoration remain unverified. |
| Historical Singapore close/settlement/CSV | 18 browser + 6 Authorization persistence cases; `4044d567d9cc4011b4f183b27496a43f` |

The earlier failed run remains failed and its precise fixture failure cause remains
unconfirmed. The complete corrective run proves these recorded inputs locally; it does
not turn the original failure into a pass or establish clean-commit CI. The user
subsequently authorized committing/pushing the corrections to the existing branch.
This versioned document records the pre-commit checkpoint. Later frozen-candidate
receipts and package/UAT IDs are recorded in the private handover index to preserve
the final clean checkout during release verification.

## Release and acceptance conditions

Freeze/commit and obtain CI for the final candidate, run the gate from that clean source,
republish/bind the matching package and release receipt, and resolve the configuration/recovery preflight
through the documented original-profile recovery procedure or a separately approved test
profile. Do not delete or transplant protected recovery to make installation pass.

Actual Windows sign-in/session/recovery, saved PDF print/reprint, the real Singapore
next-day settlement/ledger/CSV drill, all operator UAT observations and final acceptance
remain pending. Manual refunds, production/fiscal/provider operation, physical devices
and full offline certification remain excluded. No new service/API/schema/permission,
domain behavior or financial ownership is introduced. Both architecture summaries and
relevant component/deployment/planning documentation describe these operational changes.
