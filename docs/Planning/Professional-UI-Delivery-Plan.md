# NexaConnect professional UI delivery plan

This plan implements the [EPOS-informed design priorities](Market-Readiness-Roadmap.md#professional-ui-roadmap-informed-by-the-epos-demo) within the existing R06 professional UI work. The first release targets a staffed Windows counter and a branch manager. Guest QR is a follow-on candidate if selected for the pilot. Use the screenshots as workflow references and apply NexaConnect branding.

## Sequence and ownership

| Phase | Lead | Depends on | Reviewable output |
| --- | --- | --- | --- |
| 0. Baseline and prototypes | Product and design | Pilot device and operator access | Approved Checkout, Payment and Sales prototypes with current-screen task measurements |
| 1. Windows checkout and payment | POS engineering | Phase 0 | Installed candidate for a cashier dress rehearsal |
| 2. Manager Sales and Menu management | Web engineering | Phase 0 | Portal candidate for a manager dress rehearsal |
| 3. Guest QR and incoming orders | POS and web engineering | Phase 1 interaction patterns and pilot QR scope decision | Optional end-to-end QR candidate |
| 4. Release and merchant trial | Release owner, operations and merchant approver | Phases 1–2, Phase 3 if included, and hosted readiness | Accepted pilot release and recorded rollout decision |
| 5. New commercial capabilities | Product and owning service teams | Merchant discovery and an approved business design per capability | Prioritized implementation proposals |

Phases 1 and 2 can proceed in parallel after design approval. Hosting and restore preparation can proceed alongside UI work. The sequence assumes access to a pilot terminal and representative operators. Assign calendar dates after Phase 0 confirms scope, availability and engineering capacity.

## Phase 0. Baseline and approve the design

Use the [Phase 0 review pack](Professional-UI-Phase-0.md) for the source baseline, wireframes and operator observations.

1. Bind the baseline to the current installed release and record terminal resolution, Windows scaling, touch/keyboard use, printer and scanner, browser sizes and supported languages.
2. Observe a cashier finding an item, editing quantities, collecting cash, reprinting and verifying an interrupted payment. Observe a manager finding an original sale, reconciling a day and marking an item sold out. Capture task time, wrong actions and requests for help.
3. Prototype Checkout and Payment together and Sales as the representative portal page. Use synthetic orders and show long item names, sold-out and empty states, pending or uncertain payment, permission denial and stale data. Agree on typography, spacing, focus, status colours with text labels, and primary action placement.
4. Ask the pilot cashier and manager to complete the same tasks with the proposed layouts. Prioritize problems that affect collection, kitchen handoff or close. Confirm which controls can use existing contracts and file separate work for missing data or commands.

**Exit gate.** Product and pilot operators approve the layouts, critical tasks and comparison thresholds. The release owner records which surfaces, tenders and hardware belong in the first candidate.

## Phase 1. Windows checkout and payment

### First build slice — checkout layout

The checkout layout is implemented in source. The isolated Windows layout fixture passes
in Standard and Touch at three logical viewport sizes. Installed operator acceptance
remains pending.

The pinned dock uses **Verify original order** for a saved checkout. An uncertain
payment exposes **Open Payment** from Menu and **Verify payment** in Payment.

Start in [MainWindow.xaml](../../src/Clients/NexaConnect.POS/MainWindow.xaml) and reuse the existing WPF presentation resources. Make product search and category selection easy to scan, keep product names and prices legible, and keep cart lines and the existing primary checkout action in view. Keep the total and the next action visible when the menu or cart scrolls. Place the original-order recovery action in the same area so the cashier sees it before attempting another sale.

Use the [Checkout wireframe](Professional-UI-Phase-0.md#wireframe-a--windows-checkout) as the review target. Verify the empty cart, a long cart with multilingual items, sold-out or unavailable items where surfaced today, and an unresolved original payment. Run the compact workspace checks in Standard and Touch modes, including the smallest supported logical viewport, and compare the rendered checkout before and after. Reuse current menu, cart, payment and recovery handlers.

Preview this layout with synthetic data during design review. Release acceptance still uses the installed target and cashier tasks described below.

1. Build on the [compact cashier workspace](../Deployment/POS-Compact-Cashier-Workspace.md). Refine category navigation, photo/name/price tiles and the persistent cart. Keep quantity editing and the payment action reachable while the cart scrolls. Check touch and keyboard paths and keep barcode input usable.
2. Refine the payment view around the original order, amount due, cash entry and the supported tender controls. Keep recovery prominent and preserve the existing checkout and tender handlers in [MainWindow.CheckoutPayments.cs](../../src/Clients/NexaConnect.POS/MainWindow.CheckoutPayments.cs). Use distinct labels for reprint, payment confirmation and verification.
3. Run relevant POS regressions and the current [guided acceptance pack](../Deployment/POS-UX24-Guided-Acceptance.md) on the matching physical install. Exercise long carts, multilingual labels, scaling, scanner use, paper output, PIN lock/resume, and a lost response followed by restart and verification.

**Exit gate.** The cashier completes the baseline tasks at the agreed threshold. An interrupted checkout retains the same order and payment identity and reconciles with one payment, one kitchen delivery and the expected receipt and cash movement.

### Payment layout source status

The responsive original-order and tender panels are implemented. The pinned dock uses
the original amount and switches to verification when payment is uncertain. Cash uses
the server-pinned amount and exact-receipt confirmation. Track any keypad as a future
amount-received design. The targeted native fixture passed. Run the installed cashier
checks before approving the pilot release.

## Phase 2. Manager Sales and Menu management

1. Use the [shared web design system](../../src/Frontend/packages/design-system/README.md) for page headers, visible branch context, filters, table actions, status labels and feedback. Apply the pattern first to [Sales](../../src/Frontend/apps/customer-portal/src/SalesReportPanel.tsx), then to [Menu management](../../src/Frontend/apps/customer-portal/src/MenuManagementPanel.tsx).
2. Put the most useful supported sales filters and columns first. Preserve filters and scope when drilling into an authorized record and returning to the list. Clarify settlement status and which totals are provisional. Confirm available report contracts before scheduling optional columns, new report dimensions or an export.
3. Apply consistent search, category and availability controls to the menu list. Reuse existing edit, photo approval and save workflows. Show validation, stale edits and permission denial where the manager can act on them.
4. Extend applicable portal browser checks for keyboard use, filtering and navigation, loading/empty/error states, revoked access and tenant boundaries. Verify any included CSV against its filtered scope and authoritative totals.

**Exit gate.** A manager can independently find an original sale, reconcile a day and mark an item sold out. The correct branch is visible throughout, and totals and authorized exports match the underlying records.

## Phase 3. Guest QR and incoming orders

1. Use the [existing QR roadmap](QR-Ordering-Roadmap.md) and staff review flow. Refine mobile category navigation, sold-out labels, cart editing, table context and server quote display. Use explicit status text from submission through staff acceptance and payment.
2. Refine the [Windows QR workspace](../Deployment/POS-QR-Orders-Workspace.md) with order age, status counts, table/reference and clear actions for Pending review, Awaiting payment and Paid/history. Compare a wide layout with a filtered list on the smallest supported terminal.
3. Run the [QR device acceptance](../Deployment/QR9-Acceptance.md) on the actual HTTPS route using iPhone Safari and Android Chrome. Cover scanning, back navigation, expired sessions, stale prices, duplicate taps, and staff collection of the original order.

**Exit gate.** A guest completes the flow on both phone targets. During a simulated rush, staff find the oldest request and a known reference, handle a rejection and complete a successful order with one kitchen delivery and one payment outcome. Promote QR only with the matching evidence.

## Phase 4. Freeze the release and run the merchant trial

1. Bind source revision, CI evidence, service and schema versions, installer and profile. Check install/upgrade and recoverability on the target terminal. Route staging alerts to a named responder and complete the hosted restore and authenticated recovery gate.
2. Run a complete synthetic shift through next-business-day settlement. Match checkout, kitchen, receipt, refund, cash movement and settlement evidence. Recheck the relevant cases on the matching candidate after fixes.
3. Train staff on the supported workflow and run the supervised merchant trial. Track daily reconciliation, task time, operator interventions and support incidents. Follow the [market release decision](Market-Readiness-Roadmap.md#release-decision) for rollout approval.

**Exit gate.** The release owner, operations and merchant approve the evidence. An unexplained variance, duplicate payment, lost order, unauthorized access or failed restore stops rollout.

## Phase 5. Decide which capabilities to build next

Use merchant evidence to rank modifiers and set menus, discounts, split billing and mixed tenders, richer reports, pickup/delivery and new payment methods. For each selected capability, record the operator need, the smallest usable workflow, owning services, required contract or data changes, and the acceptance gate.

- For modifiers and promotions, agree on server pricing, accepted snapshots, kitchen and receipt display, inventory impact and refund treatment.
- For split bills and mixed tenders, agree on allocation, rounding, partial settlement, concurrency, retries, original-tender refunds and day-close reporting.
- For channel and provider expansion, agree on branch availability, permissions, payment expiry and recovery, reconciliation, and operational support.

Track approved work in the [product expansion roadmap](Product-Expansion-Roadmap.md). If discovery identifies an essential pilot requirement, bring its decision forward before freezing the candidate.
