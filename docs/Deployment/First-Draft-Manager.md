# First-draft manager and financial evidence (slice 5)

The draft uses one THB branch with `Asia/Singapore` business dates. The user tests cashier,
Kitchen, manager and accountant through separate identities. Manual PromptPay is fictional
human confirmation in automation; no bank transfer, automatic reconciliation, fiscal
certificate or manual-tender refund is implemented.

## Automated acceptance

From Windows PowerShell 7.4+ with the [draft prerequisites](First-Draft-Test-Stack.md):

```powershell
./scripts/test-first-draft-manager-guards.ps1
./scripts/test-first-draft-manager.ps1 -ConfirmDisposableInfrastructure
```

The manager runner executes two separately owned, fresh local environments sequentially.
Each stage must explicitly return its generated evidence path, pass and verify cleanup.
The aggregate receipt under `.runstate/first-draft-manager/<id>/verification.json` validates
stage identity, exact owned path, booleans, scenario count, timezone and source revision.
It does not scan for a convenient previous successful run or accept a failed/partial stage.

1. **Normal clock:** existing draft checkout/recovery creates real cash and fictional
   PromptPay sales. Manager and accountant see the three Paid sales through Reporting;
   cashier reporting access is denied. PromptPay never enters the cash drawer. Actual
   drawer/shift close creates a fictional five-THB shortage. Stale close and concurrent
   review commands are rejected, original review replay succeeds and changed replay
   conflicts. Accountant cannot approve. Original sale receipts and sales/refund totals
   stay fixed. Current-day closing is rejected. This dataset also contains deliberate
   unfinished/cancellation-review work; it does not demonstrate readiness for settlement.
2. **Guarded historical Singapore day:** the existing test-only cashier host restricts
   clock injection to the generated database/realm/path/loopback host. Real cashier APIs
   produce the historical sales, drawer close and review. Actual BFF manager workflows
   exercise cutoff, seal, approval, temporary fencing and permanent settlement; two-manager
   contention, stale evidence, revocation, transport outages and restart/resume retain
   their protections. Delayed settlement publication produces CSV `gaps`/`missing` with
   original POS totals; resuming the owning dispatcher yields `matched`. Eighteen distinct
   real-OIDC cases plus six Authorization persistence cases must pass without skips/retries.

Historical reference provisioning creates the main branch and a separate correction-test
branch in Singapore timezone. The latter isolates late-cash custody/posting assertions; it
is not a second operating branch in the selected draft. Reference/authorization fixtures
restrict accountant read revocation/restoration to the main branch under test, including
after correction-test branch provisioning. The fixtures do not substitute seeded
financial transitions for real commands. Older non-cashier
financial acceptance modes retain their documented synthetic-fixture boundaries.

For separate diagnosis, run:

```powershell
./scripts/test-first-draft-checkout.ps1 -ConfirmDisposableInfrastructure -VerifyManagerFlow
./scripts/test-financial-completeness-portal.ps1 -CashierDayCutoff -BusinessTimeZone Asia/Singapore -ConfirmDisposableInfrastructure
```

The older cutoff launcher defaults to Bangkok for existing callers. Its explicit timezone
option and clock fixture are acceptance-only; normal production hosts expose no clock
override. Both host environment variables are explicit, and Order receives `Services:POS`
for the terminal admission added in Slice 4. Disposable runtime grants remain broader
than production least privilege. Cleanup removes only each generated project and retained
child handles; private logs/diagnostics remain local.

Reporting's customer entry-role filters now include `customer-user`, matching the scoped
draft identities. Every route still performs live organization/branch/product authorization;
the realm entry role alone grants no report or financial authority. Platform roles remain
separate. No migration or new product permission is added.

## Solo manager workflow on the retained draft

Use a deliberately retained, provisioned draft for this drill, not a retired disposable
acceptance environment. Keep the original Windows terminal configuration and recovery.

| Step | Action and expected result |
| --- | --- |
| 1 | Cashier completes cash/fictional PromptPay checkout. Wait for POS/Reporting projection; verify original Order IDs and receipts rather than collecting again. |
| 2 | In Windows POS **Shift & cash**, refresh the drawer. Expected cash is opening plus cash movements; PromptPay is a separate tender. Count cash, review the current version, close the drawer, then close the shift. |
| 3 | Sign in separately as manager. POS **Cash review** provides current drawer evidence/history and authorized variance decisions; the portal **Cash-close report** is a lagging snapshot and cannot approve a variance. Select the branch, enter the configured Store UUID and choose a bounded UTC interval. |
| 4 | Review **Sales** and receipt/tender evidence. Sales, gross receipts, completed provider refunds and drawer variance have different meanings. A variance decision is not a refund, sale or extra collection. |
| 5 | After the next Singapore midnight, open **End-of-day** for the completed branch-local date. Resolve every blocker; do not change clocks or database rows to manufacture readiness. Record unfinished/completed-food review cases for operator investigation. |
| 6 | Use **Load cutoff evidence / Capture cutoff evidence**, review and seal current evidence, load/review approval, then prepare finalization. Refresh after changed versions, source changes or expiry. |
| 7 | Load settlement progress and finalize the exact reviewed approval/preparation. On response loss, load progress/retry the same operation; retain its identity and original immutable receipt. Permanent source barriers protect the approved day. |
| 8 | Compare the settlement ledger and download CSV for one branch and a narrow date range. Inspect the delivery/proof status, source seals and separate corrections; gaps require investigation even when original totals are present. |
| 9 | Repeat read-only evidence with accountant and verify mutation denial. Use isolated browser sessions for role/tenant switching; no unrestricted shared account. |

The older **Prepare day close** panel is an observational preparation and can remain blocked
without an operator-recorded financial-completeness check for that exact day. A portal read
does not record that check. It is separate from the fresh source-cutoff/seal/approval path
above, and cannot substitute for its reviewed evidence or finalization preparation.

The cash-close report still requires a Store UUID; store discovery is not implemented.
Read that public identifier from the retained draft configuration (substitute its RunId):

```powershell
(Get-Content .runstate/first-draft/<run-id>/pos-configuration.json -Raw | ConvertFrom-Json).Pos.StoreId
```

The next-day drill is **pending user execution** until an actual completed Singapore date
is exercised in that retained environment. Historical automation cannot attest it. Record
exact build/run/scope, date/timezone, cases passed/failed/blocked, sanitized Order/settlement
IDs and intervention notes. Never attach credentials, tokens, private journals, databases
or raw financial/service logs. Slice 4's interactive Windows/PDF gate also remains separate.

## Financial interpretation and recovery

- Sum CSV financial totals only on `settlement` rows. Correction item detail is already
  included in its original settlement's variance proof where stated; do not add it again
  to variance or net sales. Later correction posting never rewrites an old receipt.
- `matched` means the selected source/projection observation matches, not a global delivery
  watermark, fiscal certification or synchronous projection guarantee. Reload after delivery.
- Accountants can read ledger/CSV but cannot finalize, approve variance or post corrections.
  A denied/unavailable POS source prevents successful CSV download even if Reporting has rows.
- Manual cash/PromptPay refunds remain unsupported. Preserve the Paid receipt and escalate
  to the operator responsible for the test. Do not post a negative cash movement, release
  stock, void completed food or use a late correction to disguise a refund.
- On a committed settlement, preserve its receipt and source barriers. Investigate retained
  late work through existing review/correction controls; no immutable-history rollback.

Existing JSON/correlation telemetry remains active: `nexaconnect-pos` for cash review,
preparation and settlement; `nexaconnect-reporting` for source/delivery comparison and
`Settlement report`; `nexaconnect-customer-bff` for protected routing and
`Settlement reporting BFF`. Follow validated `CorrelationId`; never log or share tender
values, financial bodies or bearer/session credentials.

See [executed evidence](../Architecture/Evidence/First-Draft-Slice-5.md),
[CSV contract](../API/Settlement-Export.md) and [settlement recovery](Day-Close-Settlements.md).
