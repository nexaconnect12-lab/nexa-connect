# UI foundation browser contracts

Run `npm run build` then `npm run test:e2e:ui-foundation` from `src/Frontend`, with installed
Playwright Chromium. The suite starts its own Vite servers on loopback ports 5196 and 5197
with strict port ownership and no reuse. It intercepts all BFF calls with explicit synthetic
fixtures; no real login, secrets, database or payment provider is used.

Ten cases (five per portal) check desktop keyboard skip/landmarks and separate logout paths,
mobile navigation and page overflow, pending-session status, read-error retry without writes,
and platform role filtering/customer endpoint isolation. These are browser UI contracts,
not proof of backend authorization or a live user session. No retries/skips are configured.

Synthetic desktop/mobile screenshots are deliberately written beneath
`test-results/ui-foundation`; traces/video/automatic failure screenshots are disabled. Never
reuse screenshot capture against a real customer session without reviewing data exposure.
See [the execution report](../../../../docs/Deployment/Next-Iteration-Phase-2.md).
