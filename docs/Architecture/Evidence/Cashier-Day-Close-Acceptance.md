# Cashier-to-day-close acceptance implementation

The implementation starts from clean commit `95032200a10a8613b646d755979b86bda7afeef7`. This is an uncommitted change set, separate from the prior frontend hardening slice.

The new guarded launcher, disposable ten-context topology, real Order/POS clock host, reference-only provisioning and five-case real-OIDC browser suite implement the online cash workflow described in [the runbook](../../Deployment/Cashier-Day-Close-Acceptance.md). CI repeats the gate with independent dependency audits and bounded evidence artifacts. Production contracts, permissions, schema and settlement authority are unchanged. Order persistence and cash opening/closing gain an optional TimeProvider seam with system-clock defaults.

## Verification

The acceptance fixture builds with zero warnings/errors. Four new browser configuration/evidence guards and all fifteen prior joined-suite guards pass. Eight command-host preflight rejection cases pass without infrastructure access. Focused verification passed 41 workflow unit tests, 27 POS/preparation/BFF HTTP integration cases and all five architecture tests without skips. Live verification is in progress; no successful joined acceptance is claimed until its complete browser/Authorization/cleanup records are inspected.

## Documentation

Updated files include root README, project overview, both project/Restaurant-POS architecture documents, Deployment Guide, POS-Cashier acceptance guide, Identity Client Matrix, Database Design, POS API contract, frontend workspace/Customer Portal/Customer BFF/POS/Order/fixture-tool READMEs, and the new runbook/topology/browser READMEs and this evidence handoff. Both architecture summaries changed to reflect the real-command topology and acceptance clock boundary. No new production permissions or schema migrations are added. Final live verification and documentation audit are recorded here after completion.

## Limits

This slice covers online manual cash checkout. It does not exercise provider card/refund commands, WPF interactions or printing, offline hardware, real midnight/clock-skew behavior, production TLS/least privilege/capacity, remote CI or branch protection. Preparation is evidence for review; source cutoffs, global delivery watermarks and settlement approval/finalization remain planned.
