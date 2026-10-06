# Frontend dependency and runtime policy

The shared frontend workspace requires Node 22.12+ on the 22.x line or Node 24+ (`^22.12.0 || >=24.0.0`). Node 20 is no longer supported by the workspace policy. Use Node 22 or 24 for verification; other versions accepted by the range are not covered by the CI matrix. This is a build/test runtime requirement, including BFF publish operations that build the portals; it does not change the browser session or service contracts.

Vitest is pinned to 4.1.11, SourceMap.js resolves to 1.2.2, and the affected Tinypool dependency is removed. Vite remains pinned to 7.3.6 and runtime application dependencies are unchanged. The existing jsdom/include/plugin test configuration remains compatible and requires no migration edit.

From `src/Frontend`, run:

```powershell
npm ci --ignore-scripts
npm run audit:production
npm run audit:all
npm run check
npm test
npm run build
```

`audit:production` checks the tree with development dependencies omitted. `audit:all` explicitly includes the build and test dependencies. Both fail for moderate, high or critical findings; there are no advisory suppressions or overrides. Registry/network failures also fail the command. Audit results are time-dependent and zero findings do not constitute a complete security review. `--ignore-scripts` avoids dependency lifecycle execution during installation; explicit workspace test/build commands still execute repository code. The manifest declares the runtime range; default npm engine warnings are not a substitute for selecting a supported Node version.

The frontend verification workflow runs clean install, both audits, TypeScript, unit tests and both portal builds on Node 22 and 24. The cash-close, financial-completeness, end-of-day, day-close and cashier-to-day-close joined CI workflows run both audits after their clean install, before the acceptance launcher. Remote CI execution and configuring required branch protection remain separate operational steps.

Local verification and joined acceptance evidence are recorded in [the implementation handoff](../Architecture/Evidence/Frontend-Dependency-Hardening.md). Existing Vite chunk-size warnings remain a separate performance follow-up. No service ownership, financial invariant, tenant authorization, database schema, or public API changes in this slice.
