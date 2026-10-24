# NexaConnect design system

`NexaDesignProvider` supplies the existing Ant Design theme and shared foundation styles:
green/neutral palette, system typography, tabular numerals, 44-pixel standard controls,
visible keyboard focus, readable form labels, contained table overflow and reduced motion.
Import it once at each portal root. Styles are emitted by the provider, so package consumers
do not need a separate CSS-copy build step or external font download.

`RequestState` provides labeled loading, empty and error feedback. Only pass `onRetry` for
an owning read query; do not attach automatic replay of a financial mutation. It renders
supplied safe messages and adds no authorization or request behavior. Existing API-client
and BFF/service decisions remain authoritative.

Run `npm run check`, `npm test` and `npm run build` from `src/Frontend`. The synthetic
`npm run test:e2e:ui-foundation` verifies integration with both portals. See
[Phase 2 verification](../../../../docs/Deployment/Next-Iteration-Phase-2.md).
