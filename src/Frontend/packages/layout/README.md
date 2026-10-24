# NexaConnect portal layout

`PortalLayout` supplies a branded shell, desktop sidebar, compact-screen navigation drawer,
wrapped header actions, current-section context and a keyboard skip link to the main landmark.
Pass `title`, optional `description`, navigation `items`, `selectedKey`, optional
`headerActions` and page content. Navigation callbacks preserve the owning portal's existing
hash routes. The drawer closes on selection and can be dismissed without selecting anything.
Selecting a section moves keyboard focus to the main landmark after the drawer closes;
dismissing the drawer uses its normal focus restoration.

The owning app must establish its session before rendering the layout's authenticated-workspace
label. Supply `AuthorizationUiProvider`; capability filtering remains a presentation hint,
not a substitute for BFF/service authorization. Customer and Product Owner clients/cookies
and endpoint namespaces remain separate. Include `NexaDesignProvider` at the root for styles.

`WorkspaceFrame` is a presentation-only title/description/actions/content frame for future
focused views. It implements no Kitchen pathname, customer monitor or screensaver behavior.

Verify from `src/Frontend` with `npm run check`, `npm run build`, and
`npm run test:e2e:ui-foundation`. See [Phase 2](../../../../docs/Deployment/Next-Iteration-Phase-2.md).
