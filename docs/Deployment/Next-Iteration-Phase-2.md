# Next-iteration Phase 2 — professional UI foundation

Implemented in source on 21 October 2026, starting from commit `c0fdadb`. This is a
presentation foundation for the [next-iteration roadmap](../Planning/Next-Iteration-Roadmap.md),
not an installer update, deployment or completed user acceptance.

## Implemented scope

Both React portals retain Ant Design and their separate BFF/session/authorization boundaries.
The shared design provider supplies the established green/neutral palette, system typography,
tabular numerals, 44-pixel standard form/button controls, clear focus, form labels, contained
table overflow and reduced-motion styling. Branding remains provisional; hardware sizes and
final logo/brand assets have not been confirmed.

The portal shell now has branded desktop navigation, a compact-screen drawer, wrapped session
and workspace controls, current-section context, a main landmark and keyboard skip link.
Existing hash navigation/capability filtering and logout endpoints remain unchanged. The
authenticated-workspace label appears only after the owning app has loaded its session;
it is not a new permission or independent authentication check.

Shared `RequestState` provides labeled loading/empty/error primitives. Initial session reads,
platform summary loading/errors and generic Customer feature reads use appropriate feedback;
safe retry invokes only the owning read. Existing feature panels retain their specialized
financial error/uncertainty handling. No financial command is automatically retried by a
layout or request-state control. The workspace selector's malformed separator is corrected.

WPF remains native. `Presentation/PosTheme.xaml` supplies reusable styles for buttons, inputs,
tabs, headings/cards, password/date/check controls and grids. Keyboard-selected tabs are
distinct without applying header colours to operational content. The workspace header wraps
context, monetary displays remain readable, and the bounded scrolling status area retains
existing guidance/errors. The local cart sum is labeled **Cart estimate**; authoritative
server quote/payment amounts and calculations are unchanged. Menu price runs are explicitly
one-way display bindings.

`WorkspaceFrame` and the native resource dictionary can support later focused views. No
`/kitchen` pathname, second POS window, gallery, screensaver feature or new business workflow
is introduced. Phase 1 lock/authentication, recovery IDs and service-owned financial rules
remain intact. No backend route, schema, identity grant, deployment variable or new telemetry
boundary changes; existing safe API-client/service observability remains applicable.

## Verification

Executed against this working tree:

- TypeScript project check and both portal production builds passed.
- Frontend Unit: **76 tests in 22 files passed**.
- New synthetic UI foundation: **10 browser cases passed**, no retries/skips.
- Existing synthetic Kitchen: **6 cases passed**.
- Existing synthetic Cash-close: **4 cases passed**.
- Existing synthetic Payment Review: **5 cases passed**.
- WPF build passed with **zero warnings/errors**.
- Rendered actual WPF XAML off-screen at **1280×850** and **1000×680**, checkout and locked
  states, using synthetic menu/cart/status values. Visually inspected all four layouts;
  the bounded checks found no clipping of the selected primary controls.
- Visually inspected the actual React shells in synthetic desktop/mobile screenshots;
  mobile browser checks use **390×844**, desktop **1280×720**.

```powershell
# From src/Frontend
npm run check
npm test
npm run build
npm run test:e2e:ui-foundation
npm run test:e2e:kitchen
npm run test:e2e:cash-close
npm run test:e2e:payment-review
# From repository root
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj --no-restore
```

The first sandbox Vitest attempt failed before executing tests due to temporary-file rename
permissions; the normal-access run passed. A sandbox browser attempt stalled during Vite
startup and was stopped; normal-access suites passed. The initial off-screen fixture found
read-only menu-run binding assumptions, which were made explicitly one-way; visual review
then caught and corrected a tab-header colour leaking into content. Corrected previews were
regenerated. These attempts are not counted as passing evidence.

A strengthened mobile keyboard check initially passed eight cases and failed two: the
drawer restored focus to the skip link after selecting a page. Selection now focuses the
main landmark after the drawer's own focus restoration; dismissal retains normal drawer
focus behavior. The corrected ten-case suite passed without weakening assertions or
extending timeouts.

Vite still reports large bundles above its 500 kB advisory threshold. Both builds succeed;
code splitting/loading budgets remain follow-up work before hosted performance acceptance.
No dependency version was changed.

## Local visual artifacts and limits

Synthetic browser images are under `src/Frontend/test-results/ui-foundation` with per-test
desktop/mobile paths. Off-screen WPF images and `layout-check.json` are under
`.runstate/ui-foundation-preview/images`; its private rendering host reads XAML/styles,
removes application event handlers and never constructs the POS app, authenticates, registers
callbacks or reads terminal recovery. It does not prove click behavior, real session state,
physical touch, actual display DPI or payment processing. These ignored local artifacts are
not part of a clean-checkout release or independently signed acceptance evidence.

Browser screenshots and requests contain only explicit fixture values. The suites verify
presentation/guard preservation against mocked responses, not real OIDC/tenant authorization.
The existing server-backed release gates remain separate. No retained first-draft state,
installer or UAT binding was reset, activated or marked passed; no public deployment,
commit/push or new release package is part of this phase.

## Consolidated user acceptance still pending

After phase 8 freezes the replacement candidate, check the three surfaces with actual roles
and chosen hardware: tab/menu navigation, keyboard focus, touch targets, long labels/tables,
form validation, readable amounts and retained failure/uncertain-payment guidance. Recheck
the original-operator lock/resume and verify no screen layout change hides recovery controls.
Repeat cashier/Kitchen/PDF/next-day settlement flows in phase 9. Record exact resolutions/DPI
and user findings; do not infer full accessibility compliance or hardware acceptance from
these limited layout/browser checks.

Detailed onboarding, menu/table/refund/payment expansion remains separately scoped. Continue
with Phase 3's direct Kitchen entry; its existing `#kitchen` queue is preserved today.
