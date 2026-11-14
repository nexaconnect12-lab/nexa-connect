# Phase 0 — cashier and manager design review

Use this review pack with the [delivery plan](Professional-UI-Delivery-Plan.md#phase-0-baseline-and-approve-the-design). The wireframes use synthetic data. The baseline below comes from source and the deployment guide. Record the actual installed release and operator observations during the walkthrough.

## Current screens to compare

| Screen | Current layout and behavior | What to observe |
| --- | --- | --- |
| Windows Checkout | Compact toolbar with secondary filters and scanner controls in an expander. Responsive product grid, bounded cart, pinned total and recovery guidance. | Can the cashier find a category, identify the active filter, and change a line without losing sight of the amount due? |
| Windows Payment | Complete payment shows the configured method. Manual transfers require receipt verification. Uncertainty directs staff to verify the original payment. | Can the cashier identify the next action and find the existing payment after a lost response? |
| Customer Portal Sales | A scoped branch picker, explicit UTC range up to 31 days, sales totals, and separate recorded completeness evidence. | Can the manager select the correct branch/window, explain what the numbers represent and locate the order reference? |

[Windows source](../../src/Clients/NexaConnect.POS/MainWindow.xaml) · [Installed checkout guide](../Deployment/POS-Compact-Cashier-Workspace.md) · [Sales source](../../src/Frontend/apps/customer-portal/src/SalesReportPanel.tsx)

## Wireframe A — Windows Checkout

```text
Checkout                             Riverside branch · Terminal 1     More
┌─────────────────────────────────────────────────┬─────────────────────────┐
│ Search menu                                     │ Current order           │
│ [ All ] [ Mains ] [ Drinks • ] [ Desserts ]      │ Chicken rice     1      │
│                                                 │                 ฿80.00  │
│ [ Chicken rice ] [ Iced tea   ] [ Lime soda   ]   │ Iced tea       − 2 +    │
│ [      ฿80.00  ] [    ฿35.00  ] [ Sold out   ]   │                 ฿70.00  │
│                                                 │                         │
│ Products scroll here                            │ Cart lines scroll here  │
├─────────────────────────────────────────────────┼─────────────────────────┤
│                                                 │ [ Continue · ฿150.00 ] │
└─────────────────────────────────────────────────┴─────────────────────────┘
```

Use the existing branch-approved photos when available. Keep the primary action and amount together as the list scrolls. Long names should wrap and remain legible. For an empty cart, explain that the cashier needs to add an item. When an original payment needs recovery, show its status and verification action in the same location as the ordinary next action. Confirm the final action label against the existing checkout behavior.

## Wireframe B — Windows Payment

```text
Payment                                                     Back to checkout
┌───────────────────────────────────┬────────────────────────────────────────┐
│ Cash                              │ Original order · DEMO-014             │
│ [7] [8] [9]                      │ Chicken rice       1           ฿80.00 │
│ [4] [5] [6]                      │ Iced tea            2           ฿70.00 │
│ [1] [2] [3]                      │                                        │
│ [.] [0] [Clear]                  │ Amount due                     ฿150.00 │
│                                   │ [ Confirm cash received ]              │
└───────────────────────────────────┴────────────────────────────────────────┘

After an interrupted confirmation, the action area reads:
Payment outcome unknown for DEMO-014 · ฿150.00
[ Verify original payment ]
```

Treat the keypad as a proposed interaction. Check which existing amount controls it can drive before implementation. Show only enabled tenders. Keep amount, method and original order visible during verification. If access changes, explain how the authorized cashier can resume the saved operation.

## Wireframe C — Customer Portal Sales

```text
Reporting / Sales                                         Riverside branch
Branch [ Riverside ▾ ]  From (UTC) [ … ]  To (UTC) [ … ]    [ Load report ]

 Completed sales ฿150.00     Refunds ฿0.00      Net sales ฿150.00
 Recorded completeness check · Not checked for this branch and period

 Order reference       Ordered at (UTC)      Channel    Status       Total
 DEMO-014              14 Nov, 03:00         POS        Paid      ฿150.00
```

Keep the selected branch and applied period visible. Preserve existing UTC window semantics and label them clearly. Show unavailable, empty, stale and permission-denied states in the report area. For finalized figures or reconciliation, guide the manager to the applicable existing close workflow. Validate scoped order navigation with the current contracts before making references clickable.

## Operator walkthrough

### Installed-target preflight

The 14 November read-only check found a staged physical POS 0.8.10 transfer bundle. The physical pilot status did not verify an installation, and the newest local POS acceptance worklist failed its target-binding check. Use the physical pilot for the walkthrough once the terminal operator confirms the installed version and matching test profile. Keep any earlier observations with their original release.

Start with the low-risk baseline on the confirmed target. At Checkout, search for a test item, add it, increase its quantity, and check the line total and overall total. Record elapsed time, wrong actions and any help needed. Clear the unsubmitted test cart when done. Then use an existing synthetic paid order for the manager's Sales lookup before adding a new cash transaction or exercising recovery.

Run the current-screen tasks first, using a designated test environment and synthetic orders. Use a prepared isolated acceptance target for interrupted-response scenarios. For each task, record elapsed time, wrong actions, requests for help, whether it completed and the operator's feedback. Use the wireframes as a second walkthrough and ask the operator to point to each next action. Measure prototype performance again when there is a working implementation.

| Task | Expected outcome | Current release observations | Wireframe observations |
| --- | --- | --- | --- |
| Find an item and change its quantity | Correct item, count and total | Pending | Pending |
| Collect test cash and reprint the receipt | One payment and the original receipt | Pending | Pending |
| Resume an interrupted payment | Verify the saved original payment with no second collection | Pending | Pending |
| Find the original sale | Correct branch, period, order reference and amount | Pending | Pending |
| Reconcile the day | Explain report completeness and verify close through the existing manager flow | Pending | Pending |
| Mark a test item sold out | Change the intended item in the intended branch | Pending | Pending |

Record the installed release/profile, branch and terminal, Windows resolution/scaling, input mode, printer/scanner models, browser and viewport, supported languages, operator roles and observation date. Capture each usability issue with its screen, reproduction steps and impact. Before starting Phases 1 and 2, have product and the pilot operators agree on the layouts, task thresholds and candidate scope. Physical observations and approval are pending.
